using ChessFixer.Core;
using ChessFixer.Core.Pairing;
using ChessFixer.Core.Standings;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Hubs;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services;

/// <summary>
/// All tournament rules. Stateless: every call loads the tournament from the database.
/// <para>
/// While a tournament is New or WaitingForNextRound, the next round is already paired and stored
/// as a round without StartedAt (the "pending round"), so the dashboard can show it. Every change
/// that can affect the pairing (players, results, settings) re-pairs the pending round.
/// </para>
/// </summary>
public class TournamentService(ChessFixerDbContext db, IHubContext<TournamentHub> hub, TimeProvider time)
{
    // ---------- Queries ----------

    public async Task<List<TournamentSummaryDto>> GetAllAsync() =>
        await db.Tournaments
            .OrderByDescending(t => t.Id)
            .Select(t => new TournamentSummaryDto(
                t.Id, t.Title, t.State,
                t.Players.Count(p => p.IsActive),
                t.Rounds.Count(r => r.StartedAt != null)))
            .ToListAsync();

    public async Task<TournamentDto> GetAsync(int id) => ToDto(await LoadAsync(id));

    public async Task<List<TournamentPlayerDto>> GetPlayersAsync(int id)
    {
        var tournament = await LoadAsync(id);
        return tournament.Players
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.Player.Name)
            .Select(p => new TournamentPlayerDto(p.PlayerId, p.Player.Name, p.Player.Rating, p.IsActive))
            .ToList();
    }

    /// <summary>All rounds in order; the last one may be the pending (not started) round.</summary>
    public async Task<List<RoundDto>> GetRoundsAsync(int id)
    {
        var tournament = await LoadAsync(id);
        return tournament.Rounds
            .OrderBy(r => r.Number)
            .Select(r => new RoundDto(
                r.Number,
                r.StartedAt is not null,
                r.Games.OrderBy(g => g.Board).Select(g => new GameDto(
                    g.Id,
                    g.Board,
                    new GamePlayerDto(g.White.PlayerId, g.White.Player.Name),
                    g.Black is null ? null : new GamePlayerDto(g.Black.PlayerId, g.Black.Player.Name),
                    g.Result)).ToList()))
            .ToList();
    }

    public async Task<List<StandingDto>> GetStandingsAsync(int id)
    {
        var tournament = await LoadAsync(id);
        var participants = tournament.Players.ToDictionary(p => p.Id);

        return StandingsCalculator
            .Calculate(participants.Keys, PlayedGames(tournament), ScoringOf(tournament))
            .Select(row =>
            {
                var participant = participants[row.PlayerId];
                return new StandingDto(
                    row.Rank, participant.PlayerId, participant.Player.Name, participant.IsActive,
                    row.Points, row.Buchholz, row.SonnebornBerger,
                    row.Played, row.Wins, row.Draws, row.Losses);
            })
            .ToList();
    }

    // ---------- Commands ----------

    public async Task<TournamentDto> CreateAsync(CreateTournamentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ApiException(ApiErrorCode.TitleRequired);

        var tournament = new Tournament { Title = request.Title.Trim() };
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();

        await NotifyAsync(tournament.Id);
        return ToDto(tournament);
    }

    public Task<TournamentDto> UpdateAsync(int id, UpdateTournamentRequest request) =>
        MutateAsync(id, async tournament =>
        {
            if (string.IsNullOrWhiteSpace(request.Title)) throw new ApiException(ApiErrorCode.TitleRequired);

            var title = request.Title.Trim();
            var settings = request.Settings;
            if (title == tournament.Title && settings == SettingsOf(tournament)) return;

            // Once round 1 has started nothing can change, not even the title.
            if (IsSettingsLocked(tournament)) throw new ApiException(ApiErrorCode.SettingsLocked);

            tournament.Title = title;
            if (settings == SettingsOf(tournament)) return;
            if (settings.GameTimeMinutes <= 0 || settings.IncrementSeconds < 0
                || settings.PointsForWin < 0 || settings.PointsForDraw < 0
                || settings.PointsForLoss < 0 || settings.PointsForBye < 0
                || !Enum.IsDefined(settings.PairingSystem))
                throw new ApiException(ApiErrorCode.InvalidSettings);

            tournament.PairingSystem = settings.PairingSystem;
            tournament.GameTimeMinutes = settings.GameTimeMinutes;
            tournament.IncrementSeconds = settings.IncrementSeconds;
            tournament.PointsForWin = settings.PointsForWin;
            tournament.PointsForDraw = settings.PointsForDraw;
            tournament.PointsForLoss = settings.PointsForLoss;
            tournament.PointsForBye = settings.PointsForBye;

            await RefreshPendingRoundAsync(tournament);
        });

    public Task<TournamentDto> AddPlayerAsync(int id, int playerId) =>
        MutateAsync(id, async tournament =>
        {
            EnsurePlayersCanChange(tournament);
            var player = await db.Players.FindAsync(playerId) ?? throw new ApiException(ApiErrorCode.PlayerNotFound);

            var participant = tournament.Players.SingleOrDefault(p => p.PlayerId == playerId);
            if (participant is { IsActive: true }) throw new ApiException(ApiErrorCode.PlayerAlreadyInTournament);

            if (participant is null)
                tournament.Players.Add(new TournamentPlayer { Player = player });
            else
                participant.IsActive = true;

            await RefreshPendingRoundAsync(tournament);
        });

    /// <summary>
    /// A player without games is removed completely; a player with games stays in the standings
    /// but is no longer paired.
    /// </summary>
    public Task<TournamentDto> RemovePlayerAsync(int id, int playerId) =>
        MutateAsync(id, async tournament =>
        {
            EnsurePlayersCanChange(tournament);
            var participant = tournament.Players.SingleOrDefault(p => p.PlayerId == playerId && p.IsActive)
                              ?? throw new ApiException(ApiErrorCode.PlayerNotInTournament);

            await RemovePendingRoundAsync(tournament);

            var hasGames = StartedRounds(tournament)
                .SelectMany(r => r.Games)
                .Any(g => g.WhiteId == participant.Id || g.BlackId == participant.Id);

            if (hasGames)
                participant.IsActive = false;
            else
            {
                tournament.Players.Remove(participant);
                db.TournamentPlayers.Remove(participant);
            }

            await RefreshPendingRoundAsync(tournament);
        });

    public Task<TournamentDto> StartNextRoundAsync(int id) =>
        MutateAsync(id, async tournament =>
        {
            if (tournament.State == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
            if (tournament.State == TournamentState.RoundActive) throw new ApiException(ApiErrorCode.RoundInProgress);

            // Normally the pending round exists already. If pairing failed earlier, try again to report why.
            var round = PendingRound(tournament) ?? await PairPendingRoundAsync(tournament, throwOnError: true);

            round!.StartedAt = time.GetUtcNow().UtcDateTime;
            tournament.State = TournamentState.RoundActive;
        });

    public Task<TournamentDto> SetResultAsync(int id, int gameId, GameResult result) =>
        MutateAsync(id, async tournament =>
        {
            var game = tournament.Rounds.SelectMany(r => r.Games).SingleOrDefault(g => g.Id == gameId)
                       ?? throw new ApiException(ApiErrorCode.GameNotFound);

            if (tournament.State == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);

            // Only the most recent started round can be (corrected) entered.
            var lastStarted = StartedRounds(tournament).LastOrDefault();
            if (lastStarted is null || game.RoundId != lastStarted.Id) throw new ApiException(ApiErrorCode.ResultNotEditable);
            if (game.BlackId is null) throw new ApiException(ApiErrorCode.ResultNotEditable);
            if (result == GameResult.Bye || !Enum.IsDefined(result)) throw new ApiException(ApiErrorCode.InvalidResult);

            if (game.Result == result) return;
            game.Result = result;

            if (lastStarted.Games.All(g => g.Result != GameResult.NotPlayed))
            {
                tournament.State = TournamentState.WaitingForNextRound;
                await RefreshPendingRoundAsync(tournament);
            }
            else
            {
                tournament.State = TournamentState.RoundActive;
                await RemovePendingRoundAsync(tournament);
            }
        });

    public Task<TournamentDto> CloseAsync(int id) =>
        MutateAsync(id, async tournament =>
        {
            if (tournament.State == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
            if (tournament.State == TournamentState.RoundActive) throw new ApiException(ApiErrorCode.RoundInProgress);

            await RemovePendingRoundAsync(tournament);
            tournament.State = TournamentState.Closed;
        });

    // ---------- Helpers ----------

    /// <summary>Loads the tournament, applies a change in a transaction, saves and notifies clients.</summary>
    private async Task<TournamentDto> MutateAsync(int id, Func<Tournament, Task> change)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tournament = await LoadAsync(id);

        await change(tournament);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await NotifyAsync(id);
        return ToDto(tournament);
    }

    private async Task<Tournament> LoadAsync(int id) =>
        await db.Tournaments
            .Include(t => t.Players).ThenInclude(p => p.Player)
            .Include(t => t.Rounds).ThenInclude(r => r.Games)
            .AsSplitQuery()
            .SingleOrDefaultAsync(t => t.Id == id)
        ?? throw new ApiException(ApiErrorCode.TournamentNotFound);

    private async Task NotifyAsync(int tournamentId)
    {
        await hub.Clients.All.SendAsync(TournamentHubContract.TournamentChanged, tournamentId);
        await hub.Clients.All.SendAsync(TournamentHubContract.TournamentListChanged);
    }

    private static void EnsurePlayersCanChange(Tournament tournament)
    {
        switch (tournament.State)
        {
            case TournamentState.Closed: throw new ApiException(ApiErrorCode.TournamentClosed);
            case TournamentState.RoundActive: throw new ApiException(ApiErrorCode.RoundInProgress);
        }

        if (tournament.PairingSystem == PairingSystem.RoundRobin && StartedRounds(tournament).Any())
            throw new ApiException(ApiErrorCode.RoundRobinPlayersLocked);
    }

    private async Task RefreshPendingRoundAsync(Tournament tournament)
    {
        await RemovePendingRoundAsync(tournament);
        if (tournament.State is TournamentState.New or TournamentState.WaitingForNextRound)
            await PairPendingRoundAsync(tournament, throwOnError: false);
    }

    private async Task RemovePendingRoundAsync(Tournament tournament)
    {
        if (PendingRound(tournament) is not { } pending) return;

        tournament.Rounds.Remove(pending);
        db.Rounds.Remove(pending);
        // Save now so the new pending round can reuse the round number (unique index).
        await db.SaveChangesAsync();
    }

    private async Task<Round?> PairPendingRoundAsync(Tournament tournament, bool throwOnError)
    {
        var activePlayers = tournament.Players.Where(p => p.IsActive).ToList();
        var input = new PairingInput(
            activePlayers.Select(p => new PairingPlayer(p.Id, p.Player.Rating)).ToList(),
            PlayedGames(tournament),
            ScoringOf(tournament));

        IReadOnlyList<BoardPairing> pairings;
        try
        {
            pairings = PairingStrategies.For(tournament.PairingSystem).PairNextRound(input);
        }
        catch (PairingException ex)
        {
            if (throwOnError) throw new ApiException(ToErrorCode(ex.Error));
            return null;
        }

        var participants = activePlayers.ToDictionary(p => p.Id);
        var round = new Round { Number = StartedRounds(tournament).Count() + 1 };
        round.Games.AddRange(pairings.Select(p => new Game
        {
            Board = p.Board,
            White = participants[p.WhiteId],
            Black = p.BlackId is { } blackId ? participants[blackId] : null,
            Result = p.IsBye ? GameResult.Bye : GameResult.NotPlayed
        }));
        tournament.Rounds.Add(round);
        await db.SaveChangesAsync();
        return round;
    }

    private static ApiErrorCode ToErrorCode(PairingError error) => error switch
    {
        PairingError.NotEnoughPlayers => ApiErrorCode.NotEnoughPlayers,
        PairingError.TooManyPlayers => ApiErrorCode.TooManyPlayers,
        PairingError.AllRoundsPlayed => ApiErrorCode.AllRoundsPlayed,
        _ => ApiErrorCode.NoValidPairing
    };

    private static Round? PendingRound(Tournament tournament) =>
        tournament.Rounds.SingleOrDefault(r => r.StartedAt is null);

    private static IEnumerable<Round> StartedRounds(Tournament tournament) =>
        tournament.Rounds.Where(r => r.StartedAt is not null).OrderBy(r => r.Number);

    private static bool IsSettingsLocked(Tournament tournament) => StartedRounds(tournament).Any();

    private static List<PlayedGame> PlayedGames(Tournament tournament) =>
        StartedRounds(tournament)
            .SelectMany(r => r.Games.Select(g => new PlayedGame(r.Number, g.WhiteId, g.BlackId, g.Result)))
            .ToList();

    private static ScoringRules ScoringOf(Tournament t) =>
        new(t.PointsForWin, t.PointsForDraw, t.PointsForLoss, t.PointsForBye);

    private static TournamentSettingsDto SettingsOf(Tournament t) => new(
        t.PairingSystem, t.GameTimeMinutes, t.IncrementSeconds,
        t.PointsForWin, t.PointsForDraw, t.PointsForLoss, t.PointsForBye);

    private static TournamentDto ToDto(Tournament t) => new(
        t.Id,
        t.Title,
        t.State,
        SettingsOf(t),
        StartedRounds(t).Count(),
        IsSettingsLocked(t),
        t.PairingSystem == PairingSystem.RoundRobin
            ? RoundRobinPairing.TotalRounds(t.Players.Count(p => p.IsActive))
            : null);
}
