using ChessFixer.Core;
using ChessFixer.Core.Pairing;
using ChessFixer.Core.Rating;
using ChessFixer.Core.Standings;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Hubs;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Server.Services.Reports;
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
/// as a round without StartedAt (the "pending round"), so the Matches tab can show it. Every change
/// that can affect the pairing (players, results, settings) re-pairs the pending round.
/// </para>
/// </summary>
public class TournamentService(
    ChessFixerDbContext db,
    IHubContext<TournamentHub> hub,
    TimeProvider time,
    AiSettingsService aiSettings,
    ReportQueue reportQueue)
{
    private const int MaxLocationLength = 200;
    private const int MaxDescriptionLength = 2000;

    // ---------- Queries ----------

    public async Task<List<TournamentSummaryDto>> GetAllAsync() =>
        await db.Tournaments
            .OrderByDescending(t => t.Id)
            .Select(t => new TournamentSummaryDto(
                t.Id, t.Title, t.State, t.PairingSystem,
                t.Players.Count(p => p.IsActive),
                t.Boards, t.GameTimeMinutes, t.IncrementSeconds,
                t.Rounds.Count(r => r.StartedAt != null)))
            .ToListAsync();

    public async Task<TournamentDto> GetAsync(int id) => ToDto(await LoadAsync(id));

    public async Task<List<TournamentPlayerDto>> GetPlayersAsync(int id)
    {
        var tournament = await LoadAsync(id);
        return tournament.Players
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.Player.Name)
            .Select(p => new TournamentPlayerDto(p.PlayerId, p.Player.Name, p.Player.Rating, p.IsActive, p.Player.IsDeleted))
            .ToList();
    }

    /// <summary>The prompt that produced the current report.</summary>
    public async Task<ReportPromptDto> GetReportPromptAsync(int id)
    {
        var prompt = await db.Tournaments
            .Where(t => t.Id == id)
            .Select(t => new { t.ReportSystemPrompt, t.ReportUserPrompt, t.ReportModel })
            .SingleOrDefaultAsync() ?? throw new ApiException(ApiErrorCode.TournamentNotFound);

        if (prompt.ReportSystemPrompt is null || prompt.ReportUserPrompt is null)
            throw new ApiException(ApiErrorCode.ReportNotAvailable);
        return new ReportPromptDto(prompt.ReportSystemPrompt, prompt.ReportUserPrompt, prompt.ReportModel);
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
                r.Games.OrderBy(g => g.Number).Select(g => new GameDto(
                    g.Id,
                    g.Number,
                    g.Board,
                    new GamePlayerDto(g.White.PlayerId, g.White.Player.Name, g.White.Player.IsDeleted),
                    g.Black is null ? null : new GamePlayerDto(g.Black.PlayerId, g.Black.Player.Name, g.Black.Player.IsDeleted),
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
                    row.Played, row.Wins, row.Draws, row.Losses, participant.Player.IsDeleted);
            })
            .ToList();
    }

    /// <summary>The player's place in every tournament they played in, and all their games (newest first).</summary>
    public async Task<PlayerStatisticsDto> GetPlayerStatisticsAsync(int playerId)
    {
        if (!await db.Players.AnyAsync(p => p.Id == playerId)) throw new ApiException(ApiErrorCode.PlayerNotFound);

        var tournamentIds = await db.TournamentPlayers
            .Where(tp => tp.PlayerId == playerId)
            .Select(tp => tp.TournamentId)
            .OrderByDescending(id => id)
            .ToListAsync();

        var results = new List<PlayerTournamentResultDto>();
        var games = new List<PlayerGameDto>();
        foreach (var tournamentId in tournamentIds)
        {
            var tournament = await LoadAsync(tournamentId);
            var participant = tournament.Players.Single(p => p.PlayerId == playerId);
            var scoring = ScoringOf(tournament);

            var standings = StandingsCalculator.Calculate(
                tournament.Players.Select(p => p.Id), PlayedGames(tournament), scoring);
            var row = standings.Single(r => r.PlayerId == participant.Id);
            results.Add(new PlayerTournamentResultDto(
                tournament.Id, tournament.Title, tournament.State, row.Rank, standings.Count,
                row.Points, row.Played, row.Wins, row.Draws, row.Losses));

            var participants = tournament.Players.ToDictionary(p => p.Id);
            foreach (var round in StartedRounds(tournament).Reverse())
            foreach (var game in round.Games.Where(g => g.WhiteId == participant.Id || g.BlackId == participant.Id))
            {
                var isWhite = game.WhiteId == participant.Id;
                var opponentId = isWhite ? game.BlackId : game.WhiteId;
                var opponent = opponentId is { } id ? participants[id] : null;
                var (whitePoints, blackPoints) = scoring.PointsFor(game.Result);
                games.Add(new PlayerGameDto(
                    tournament.Id, tournament.Title, round.Number, isWhite,
                    opponent is null ? null : new GamePlayerDto(opponent.PlayerId, opponent.Player.Name, opponent.Player.IsDeleted),
                    game.Result,
                    game.Result == GameResult.NotPlayed ? null : isWhite ? whitePoints : blackPoints));
            }
        }

        return new PlayerStatisticsDto(
            results.Count,
            results.Sum(r => r.Played),
            results.Sum(r => r.Wins),
            results.Sum(r => r.Draws),
            results.Sum(r => r.Losses),
            results,
            games);
    }

    // ---------- Commands ----------

    public async Task<TournamentDto> CreateAsync(CreateTournamentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ApiException(ApiErrorCode.TitleRequired);

        ValidateSettings(request.Settings);

        var tournament = new Tournament
        {
            Title = request.Title.Trim(),
            Location = Optional(request.Location, MaxLocationLength),
            Description = Optional(request.Description, MaxDescriptionLength)
        };
        ApplySettings(tournament, request.Settings);
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
            var location = Optional(request.Location, MaxLocationLength);
            var description = Optional(request.Description, MaxDescriptionLength);
            var settings = request.Settings;
            var current = SettingsOf(tournament);
            if (title == tournament.Title && location == tournament.Location && description == tournament.Description
                && settings == current) return;

            // Settings can change between rounds, not during a round or after closing.
            if (tournament.State == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
            if (tournament.State == TournamentState.RoundActive) throw new ApiException(ApiErrorCode.RoundInProgress);

            // Once round 1 has started, only the title, location, description, time control and boards can change.
            var onlyFreeSettingsChanged = settings with
            {
                GameTimeMinutes = current.GameTimeMinutes,
                IncrementSeconds = current.IncrementSeconds,
                Boards = current.Boards
            } == current;
            if (IsSettingsLocked(tournament) && !onlyFreeSettingsChanged)
                throw new ApiException(ApiErrorCode.SettingsLocked);

            tournament.Title = title;
            tournament.Location = location;
            tournament.Description = description;
            if (settings == current) return;
            ValidateSettings(settings);
            ApplySettings(tournament, settings);

            await RefreshPendingRoundAsync(tournament);
        });

    /// <summary>Trimmed text, or null when empty. Too long text is cut off.</summary>
    private static string? Optional(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static void ValidateSettings(TournamentSettingsDto? settings)
    {
        if (settings is null
            || settings.GameTimeMinutes <= 0 || settings.IncrementSeconds < 0 || settings.Boards < 1
            || settings.PointsForWin < 0 || settings.PointsForDraw < 0
            || settings.PointsForLoss < 0 || settings.PointsForBye < 0
            || !Enum.IsDefined(settings.PairingSystem))
            throw new ApiException(ApiErrorCode.InvalidSettings);
    }

    private static void ApplySettings(Tournament tournament, TournamentSettingsDto settings)
    {
        tournament.PairingSystem = settings.PairingSystem;
        tournament.GameTimeMinutes = settings.GameTimeMinutes;
        tournament.IncrementSeconds = settings.IncrementSeconds;
        tournament.PointsForWin = settings.PointsForWin;
        tournament.PointsForDraw = settings.PointsForDraw;
        tournament.PointsForLoss = settings.PointsForLoss;
        tournament.PointsForBye = settings.PointsForBye;
        tournament.Boards = settings.Boards;
        tournament.IsRated = settings.IsRated;
    }

    public Task<TournamentDto> AddPlayerAsync(int id, int playerId) =>
        MutateAsync(id, async tournament =>
        {
            EnsurePlayersCanChange(tournament);
            var player = await db.Players.FindAsync(playerId);
            if (player is null or { IsDeleted: true }) throw new ApiException(ApiErrorCode.PlayerNotFound);

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

            // A player cannot withdraw in the middle of a game (playing or waiting for a board).
            var inUnfinishedGame = tournament.State == TournamentState.RoundActive
                && StartedRounds(tournament).Last().Games.Any(g =>
                    g.Result == GameResult.NotPlayed && (g.WhiteId == participant.Id || g.BlackId == participant.Id));
            if (inUnfinishedGame) throw new ApiException(ApiErrorCode.PlayerInActiveGame);

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

            // A game in the queue has not started yet: it has no result.
            if (game.Board is null) throw new ApiException(ApiErrorCode.ResultNotEditable);

            if (game.Result == result) return;
            game.Result = result;
            await UpdateRatingsAsync(tournament, game);

            // A reopened game whose board has been taken by the next game goes back into the queue.
            if (result == GameResult.NotPlayed
                && lastStarted.Games.Any(g => g != game && g.Board == game.Board && g.Result == GameResult.NotPlayed))
                game.Board = null;

            // A finished game frees its board for the next game in the queue.
            AssignBoards(lastStarted, tournament.Boards);

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

    /// <summary>
    /// Ratings change right after each game. A corrected or cleared result first reverts the change of the
    /// previous result (so a hand-set rating in between is kept), then applies the change of the new result.
    /// </summary>
    private async Task UpdateRatingsAsync(Tournament tournament, Game game)
    {
        var white = game.White.Player;
        var black = game.Black!.Player;

        if (game.IsRated)
        {
            white.Rating -= game.WhiteRatingChange!.Value;
            black.Rating -= game.BlackRatingChange!.Value;
            game.WhiteRatingBefore = game.BlackRatingBefore = game.WhiteRatingChange = game.BlackRatingChange = null;
            game.RatedAt = null;
        }

        if (!tournament.IsRated) return;
        var changes = EloCalculator.Changes(
            white.Rating, await RatedGameCountAsync(white.Id, game.Id),
            black.Rating, await RatedGameCountAsync(black.Id, game.Id),
            game.Result);
        if (changes is not var (whiteChange, blackChange)) return;

        game.WhiteRatingBefore = white.Rating;
        game.BlackRatingBefore = black.Rating;
        game.WhiteRatingChange = whiteChange;
        game.BlackRatingChange = blackChange;
        game.RatedAt = time.GetUtcNow().UtcDateTime;
        white.Rating += whiteChange;
        black.Rating += blackChange;
    }

    /// <summary>Number of rated games of a player, apart from the given game (for the K-factor).</summary>
    private Task<int> RatedGameCountAsync(int playerId, int exceptGameId) =>
        db.Games.CountAsync(g => g.Id != exceptGameId && g.RatedAt != null
                                 && (g.White.PlayerId == playerId || g.Black!.PlayerId == playerId));

    /// <summary>Closes the tournament. When an AI is configured, it writes the report in the background.</summary>
    public async Task<TournamentDto> CloseAsync(int id)
    {
        var writeReport = await aiSettings.IsConfiguredAsync();

        var result = await MutateAsync(id, async tournament =>
        {
            if (tournament.State == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
            if (tournament.State == TournamentState.RoundActive) throw new ApiException(ApiErrorCode.RoundInProgress);

            await RemovePendingRoundAsync(tournament);
            tournament.State = TournamentState.Closed;
            if (writeReport) RequestReport(tournament);
        });

        if (writeReport) reportQueue.Enqueue(id);
        return result;
    }

    /// <summary>(Re)writes the report of a closed tournament, e.g. after the AI was configured or failed.</summary>
    public async Task<TournamentDto> GenerateReportAsync(int id)
    {
        if (!await aiSettings.IsConfiguredAsync()) throw new ApiException(ApiErrorCode.AiNotConfigured);

        var result = await MutateAsync(id, tournament =>
        {
            if (tournament.State != TournamentState.Closed) throw new ApiException(ApiErrorCode.ReportNotAvailable);
            RequestReport(tournament);
            return Task.CompletedTask;
        });

        reportQueue.Enqueue(id);
        return result;
    }

    /// <summary>Marks the report as being written; an earlier report stays until the new one is ready.</summary>
    private static void RequestReport(Tournament tournament)
    {
        tournament.ReportStatus = ReportStatus.Generating;
        tournament.ReportError = null;
    }

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

    /// <summary>
    /// Players can join or withdraw at any time, also during a round: a late joiner waits for the next
    /// round; a player who withdraws keeps their current game, which still needs a result.
    /// </summary>
    private static void EnsurePlayersCanChange(Tournament tournament)
    {
        if (tournament.State == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
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
            Number = p.Board,
            White = participants[p.WhiteId],
            Black = p.BlackId is { } blackId ? participants[blackId] : null,
            Result = p.IsBye ? GameResult.Bye : GameResult.NotPlayed
        }));
        AssignBoards(round, tournament.Boards);
        tournament.Rounds.Add(round);
        await db.SaveChangesAsync();
        return round;
    }

    /// <summary>
    /// Gives the games waiting in the queue (in pairing order) the free boards, lowest board first.
    /// A board is free when no unfinished game is on it. Finished games keep their board.
    /// </summary>
    private static void AssignBoards(Round round, int boards)
    {
        var unfinished = round.Games.Where(g => g.Result == GameResult.NotPlayed).ToList();
        var occupied = unfinished.Where(g => g.Board is not null).Select(g => g.Board!.Value).ToHashSet();
        var freeBoards = Enumerable.Range(1, boards).Where(b => !occupied.Contains(b));

        using var free = freeBoards.GetEnumerator();
        foreach (var game in unfinished.Where(g => g.Board is null).OrderBy(g => g.Number))
        {
            if (!free.MoveNext()) break;
            game.Board = free.Current;
        }
    }

    private static ApiErrorCode ToErrorCode(PairingError error) => error switch
    {
        PairingError.NotEnoughPlayers => ApiErrorCode.NotEnoughPlayers,
        PairingError.TooManyPlayers => ApiErrorCode.TooManyPlayers,
        _ => ApiErrorCode.NoValidPairing
    };

    private static Round? PendingRound(Tournament tournament) =>
        tournament.Rounds.SingleOrDefault(r => r.StartedAt is null);

    private static IEnumerable<Round> StartedRounds(Tournament tournament) =>
        tournament.Rounds.Where(r => r.StartedAt is not null).OrderBy(r => r.Number);

    /// <summary>After round 1 the pairing system and the points are fixed.</summary>
    private static bool IsSettingsLocked(Tournament tournament) => StartedRounds(tournament).Any();

    private static List<PlayedGame> PlayedGames(Tournament tournament) =>
        StartedRounds(tournament)
            .SelectMany(r => r.Games.Select(g => new PlayedGame(r.Number, g.WhiteId, g.BlackId, g.Result)))
            .ToList();

    private static ScoringRules ScoringOf(Tournament t) =>
        new(t.PointsForWin, t.PointsForDraw, t.PointsForLoss, t.PointsForBye);

    private static TournamentSettingsDto SettingsOf(Tournament t) => new(
        t.PairingSystem, t.GameTimeMinutes, t.IncrementSeconds,
        t.PointsForWin, t.PointsForDraw, t.PointsForLoss, t.PointsForBye, t.Boards, t.IsRated);

    private static TournamentDto ToDto(Tournament t) => new(
        t.Id,
        t.Title,
        t.Location,
        t.Description,
        t.State,
        SettingsOf(t),
        StartedRounds(t).Count(),
        IsSettingsLocked(t),
        new TournamentReportDto(t.ReportStatus, t.ReportText, t.ReportGeneratedAt, t.ReportError,
            t.ReportModel, t.ReportUserPrompt is not null));
}
