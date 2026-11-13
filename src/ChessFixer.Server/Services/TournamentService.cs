using ChessFixer.Core;
using ChessFixer.Core.Pairing;
using ChessFixer.Core.Rating;
using ChessFixer.Core.Standings;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Hubs;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Server.Services.Ratings;
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
    WritingStyleService writingStyles,
    ReportQueue reportQueue,
    RatingLogService ratingLog)
{
    private const int MaxLocationLength = 200;
    private const int MaxDescriptionLength = 2000;

    // ---------- Queries ----------

    public async Task<List<TournamentSummaryDto>> GetAllAsync() =>
        await db.Tournaments
            .OrderByDescending(t => t.Id)
            .Select(t => new TournamentSummaryDto(
                t.Id, t.Title,
                // Same rules as StateOf, translated to SQL.
                t.ClosedAt != null ? TournamentState.Closed
                : !t.Rounds.Any(r => r.StartedAt != null) ? TournamentState.New
                : t.Rounds.Where(r => r.StartedAt != null).OrderByDescending(r => r.Number).First()
                    .Games.Any(g => g.Result == GameResult.NotPlayed) ? TournamentState.RoundActive
                : TournamentState.WaitingForNextRound,
                t.PairingSystem,
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
            .Select(p => new TournamentPlayerDto(p.PlayerId, p.Player.Name, p.Player.Rating, p.IsActive, p.Player.IsDeleted, p.IsPresent))
            .ToList();
    }

    /// <summary>The prompt that produced the current report.</summary>
    public async Task<ReportPromptDto> GetReportPromptAsync(int id)
    {
        if (!await db.Tournaments.AnyAsync(t => t.Id == id)) throw new ApiException(ApiErrorCode.TournamentNotFound);

        var prompt = await db.TournamentReports
            .Where(r => r.TournamentId == id)
            .Select(r => new { r.SystemPrompt, r.UserPrompt, r.Model })
            .SingleOrDefaultAsync();

        if (prompt?.SystemPrompt is null || prompt.UserPrompt is null)
            throw new ApiException(ApiErrorCode.ReportNotAvailable);
        return new ReportPromptDto(prompt.SystemPrompt, prompt.UserPrompt, prompt.Model);
    }

    /// <summary>All rounds in order; the last one may be the pending (not started) round.</summary>
    public async Task<List<RoundDto>> GetRoundsAsync(int id)
    {
        var tournament = await LoadAsync(id);
        var meetings = Meetings(tournament);
        int TimesMetBefore(Round round, Game game) => TimesMet(meetings, round, game);
        // The round reports without their prompts (those can be long).
        var reports = await db.RoundReports
            .Where(r => r.Round.TournamentId == id)
            .Select(r => new
            {
                r.RoundId,
                Dto = new TournamentReportDto(r.Status, r.Text, r.GeneratedAt, r.Error, r.Model, r.UserPrompt != null,
                    r.Events, r.WritingStyleId, r.WritingStyleName)
            })
            .ToDictionaryAsync(r => r.RoundId, r => r.Dto);
        var noReport = new TournamentReportDto(ReportStatus.None, null, null, null, null, false);

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
                    g.Result,
                    TimesMetBefore(r, g),
                    g.IsCatchUp,
                    g.PlayedAt)).ToList(),
                r.StartedAt,
                reports.GetValueOrDefault(r.Id, noReport)))
            .ToList();
    }

    /// <param name="afterRound">The standings after this round (for a round report); null for the current standings.</param>
    public async Task<List<StandingDto>> GetStandingsAsync(int id, int? afterRound = null)
    {
        var tournament = await LoadAsync(id);
        var participants = tournament.Players.ToDictionary(p => p.Id);
        var games = PlayedGames(tournament).Where(g => afterRound is null || g.Round <= afterRound).ToList();
        var isCompetition = tournament.PairingSystem == PairingSystem.Competition;
        var schedule = isCompetition ? new CompetitionSchedule(tournament.TimesAgainstEachOther, games) : null;
        var active = ActiveParticipantIds(tournament);

        return StandingsCalculator
            .Calculate(participants.Keys, games, ScoringOf(tournament), tournament.PairingSystem)
            .Select(row =>
            {
                var participant = participants[row.PlayerId];
                var maxPoints = row.Played * tournament.PointsForWin;
                return new StandingDto(
                    row.Rank, participant.PlayerId, participant.Player.Name, participant.IsActive,
                    row.Points, row.Buchholz, row.SonnebornBerger,
                    row.Played, row.Wins, row.Draws, row.Losses, participant.Player.IsDeleted,
                    Remaining: participant.IsActive ? schedule?.RemainingFor(participant.Id, active) : schedule is null ? null : 0,
                    Percentage: isCompetition && maxPoints > 0 ? Math.Round(100 * row.Points / maxPoints, 1) : null);
            })
            .ToList();
    }

    /// <summary>
    /// Competition: every player against every other player, with the games played and still to play.
    /// </summary>
    public async Task<CompetitionScheduleDto> GetScheduleAsync(int id)
    {
        var tournament = await LoadAsync(id);
        EnsureCompetition(tournament);

        var schedule = ScheduleOf(tournament);
        var participants = tournament.Players.ToDictionary(p => p.Id);
        var rounds = StartedRounds(tournament).ToList();
        return new CompetitionScheduleDto(
            tournament.TimesAgainstEachOther,
            tournament.Players
                .OrderBy(p => p.Id)
                .Select(p => new SchedulePlayerDto(p.PlayerId, p.Player.Name, p.Player.Rating, p.IsActive, p.Player.IsDeleted))
                .ToList(),
            rounds
                .SelectMany(r => r.Games.Where(g => g.Black is not null).Select(g => new ScheduleGameDto(
                    g.Id, r.Number, g.White.PlayerId, g.Black!.PlayerId, g.Result, g.IsCatchUp)))
                .ToList(),
            schedule.OpenGames(tournament.Players.OrderBy(p => p.Id).Select(p => p.Id).ToList())
                .Select(g => new OpenGameDto(
                    participants[g.PlayerA].PlayerId, participants[g.PlayerB].PlayerId,
                    g.WhiteId is { } white ? participants[white].PlayerId : null))
                .ToList());
    }

    private static (int, int) PairKey(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Every game played in the started rounds, as its round and pair of players (forfeits not counted,
    /// as in the pairing).</summary>
    private static List<(int Round, (int, int) Pair)> Meetings(Tournament tournament) =>
        StartedRounds(tournament)
            .SelectMany(r => r.Games.Where(g => g.BlackId is not null
                    && g.Result is not (GameResult.WhiteWinsByForfeit or GameResult.BlackWinsByForfeit))
                .Select(g => (r.Number, PairKey(g.WhiteId, g.BlackId!.Value))))
            .ToList();

    /// <summary>How often the two players of the game met in the rounds before its round.</summary>
    private static int TimesMet(List<(int Round, (int, int) Pair)> meetings, Round round, Game game) =>
        game.BlackId is { } blackId
            ? meetings.Count(m => m.Round < round.Number && m.Pair == PairKey(game.WhiteId, blackId))
            : 0;

    /// <summary>The player's place in every tournament they played in, and all their games (newest first).</summary>
    public async Task<PlayerStatisticsDto> GetPlayerStatisticsAsync(int playerId)
    {
        if (!await db.Players.AnyAsync(p => p.Id == playerId)) throw new ApiException(ApiErrorCode.PlayerNotFound);

        var tournamentIds = await db.TournamentPlayers
            .Where(tp => tp.PlayerId == playerId)
            .Select(tp => tp.TournamentId)
            .OrderByDescending(id => id)
            .ToListAsync();

        var clubNames = await db.Clubs.ToDictionaryAsync(c => c.Id, c => c.Name);
        var results = new List<PlayerTournamentResultDto>();
        var games = new List<PlayerGameDto>();
        foreach (var tournamentId in tournamentIds)
        {
            var tournament = await LoadAsync(tournamentId);
            var participant = tournament.Players.Single(p => p.PlayerId == playerId);
            var scoring = ScoringOf(tournament);

            var standings = StandingsCalculator.Calculate(
                tournament.Players.Select(p => p.Id), PlayedGames(tournament), scoring, tournament.PairingSystem);
            var row = standings.Single(r => r.PlayerId == participant.Id);
            results.Add(new PlayerTournamentResultDto(
                tournament.Id, tournament.Title, StateOf(tournament), row.Rank, standings.Count,
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
                    opponent is null ? null : new GamePlayerDto(
                        opponent.PlayerId, opponent.Player.Name, opponent.Player.IsDeleted, opponent.Player.IsGuest,
                        opponent.Player is { IsGuest: true, ClubId: { } clubId } ? clubNames.GetValueOrDefault(clubId) : null),
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
            EnsureBetweenRounds(tournament);

            // Once round 1 has started, only the title, location, description and boards can change.
            var onlyFreeSettingsChanged = settings with { Boards = current.Boards } == current;
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

    internal static void ValidateSettings(TournamentSettingsDto? settings)
    {
        if (settings is null
            || settings.GameTimeMinutes <= 0 || settings.IncrementSeconds < 0 || settings.Boards < 1
            || settings.PointsForWin < 0 || settings.PointsForDraw < 0
            || settings.PointsForLoss < 0 || settings.PointsForBye < 0 || settings.PointsForAbsence < 0
            || settings.TimesAgainstEachOther is < 1 or > 2
            || !Enum.IsDefined(settings.PairingSystem))
            throw new ApiException(ApiErrorCode.InvalidSettings);
    }

    private static void ApplySettings(Tournament tournament, TournamentSettingsDto settings)
    {
        tournament.PairingSystem = settings.PairingSystem;
        tournament.TimesAgainstEachOther = settings.TimesAgainstEachOther;
        tournament.GameTimeMinutes = settings.GameTimeMinutes;
        tournament.IncrementSeconds = settings.IncrementSeconds;
        tournament.PointsForWin = settings.PointsForWin;
        tournament.PointsForDraw = settings.PointsForDraw;
        tournament.PointsForLoss = settings.PointsForLoss;
        tournament.PointsForBye = settings.PointsForBye;
        tournament.PointsForAbsence = settings.PointsForAbsence;
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
            {
                participant.IsActive = true;
                participant.IsPresent = true;
            }

            await RefreshPendingRoundAsync(tournament);
            await AddLateGamesAsync(tournament);
        });

    /// <summary>
    /// Marks an active player as playing ("doet mee") or not. A player who does not play stays in the tournament and
    /// keeps their results, but is left out of the pairing of the next round (which gets an absence line for them
    /// instead). During a round a player can only be switched on (a latecomer): see <see cref="AddLateGamesAsync"/>.
    /// </summary>
    public Task<TournamentDto> SetPresenceAsync(int id, int playerId, bool isPresent) =>
        MutateAsync(id, async tournament =>
        {
            if (!isPresent || StateOf(tournament) != TournamentState.RoundActive) EnsureBetweenRounds(tournament);
            var participant = tournament.Players.SingleOrDefault(p => p.PlayerId == playerId && p.IsActive)
                              ?? throw new ApiException(ApiErrorCode.PlayerNotInTournament);
            if (participant.IsPresent == isPresent) return;

            participant.IsPresent = isPresent;
            await RefreshPendingRoundAsync(tournament);
            await AddLateGamesAsync(tournament);
        });

    /// <summary>
    /// During a round: the players who are free this round (the bye or no opponent, late joiners, and players who
    /// did not play but now do) are paired with each other where possible, and those games join the queue of the round.
    /// Swiss: pairs that have met before are not paired again; with an odd number the player with a bye keeps priority
    /// to play (the bye counts in the pairing). Competition: only pairs with a game still to play. The one left over
    /// keeps what they had (bye, no opponent, absence line or nothing).
    /// </summary>
    private async Task AddLateGamesAsync(Tournament tournament)
    {
        if (StateOf(tournament) != TournamentState.RoundActive) return;

        await db.SaveChangesAsync(); // a late joiner needs their id
        var round = StartedRounds(tournament).Last();
        Game? LineOf(TournamentPlayer p) => round.Games.FirstOrDefault(g => g.WhiteId == p.Id || g.BlackId == p.Id);

        var free = tournament.Players
            .Where(p => p is { IsActive: true, IsPresent: true })
            .Where(p => LineOf(p) is null
                        or { BlackId: null, Result: GameResult.Bye or GameResult.Absent or GameResult.NoOpponent })
            .ToDictionary(p => p.Id);
        if (free.Count < 2) return;

        IReadOnlyList<BoardPairing> pairings;
        try
        {
            // The history includes the games of this round, so a competition does not pair a game twice.
            pairings = PairingStrategyOf(tournament).PairNextRound(new PairingInput(
                free.Values.Select(p => new PairingPlayer(p.Id, p.Player.Rating)).ToList(),
                PlayedGames(tournament),
                ScoringOf(tournament)));
        }
        catch (PairingException)
        {
            return;
        }

        var met = tournament.PairingSystem == PairingSystem.Swiss
            ? Meetings(tournament).Select(m => m.Pair).ToHashSet()
            : [];
        var number = round.Games.Max(g => g.Number);
        foreach (var pairing in pairings.Where(p => !p.IsBye && !met.Contains(PairKey(p.WhiteId, p.BlackId!.Value))))
        {
            foreach (var line in new[] { LineOf(free[pairing.WhiteId]), LineOf(free[pairing.BlackId!.Value]) })
            {
                if (line is null) continue;
                round.Games.Remove(line);
                db.Games.Remove(line);
            }
            round.Games.Add(new Game
            {
                Number = ++number,
                White = free[pairing.WhiteId],
                Black = free[pairing.BlackId!.Value],
                Result = GameResult.NotPlayed
            });
        }
        AssignBoards(round, tournament.Boards);
    }

    /// <summary>
    /// A player without games (or absences) is removed completely; a player with games stays in the standings
    /// but no longer plays. In a competition their open games are not paired any more (they are "not played" when
    /// the competition is closed); when they rejoin, they can play them after all.
    /// </summary>
    public Task<TournamentDto> RemovePlayerAsync(int id, int playerId) =>
        MutateAsync(id, async tournament =>
        {
            EnsurePlayersCanChange(tournament);
            var participant = tournament.Players.SingleOrDefault(p => p.PlayerId == playerId && p.IsActive)
                              ?? throw new ApiException(ApiErrorCode.PlayerNotInTournament);

            // A player cannot withdraw in the middle of a game (playing or waiting for a board).
            var inUnfinishedGame = StateOf(tournament) == TournamentState.RoundActive
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

    /// <param name="skipRepeatMeetings">Games in which the players would meet for the third time (or more) are not
    /// played: both players get an absence line for this round instead (they stay present for the next round).</param>
    public Task<TournamentDto> StartNextRoundAsync(int id, bool skipRepeatMeetings = false) =>
        MutateAsync(id, async tournament =>
        {
            EnsureBetweenRounds(tournament);

            // Normally the pending round exists already. If pairing failed earlier, try again to report why.
            var round = PendingRound(tournament) ?? await PairPendingRoundAsync(tournament, throwOnError: true);

            if (skipRepeatMeetings) SkipRepeatMeetings(tournament, round!);
            round!.StartedAt = time.GetUtcNow().UtcDateTime;

            // When every game was skipped the round is over right away: pair the next one.
            if (StateOf(tournament) == TournamentState.WaitingForNextRound)
            {
                await db.SaveChangesAsync();
                await RefreshPendingRoundAsync(tournament);
            }
        });

    /// <summary>
    /// Turns each game of the (not started) round in which the players would meet for the third time or more into
    /// two absence lines, one per player. The other games keep their pairing; the boards are handed out again.
    /// </summary>
    private static void SkipRepeatMeetings(Tournament tournament, Round round)
    {
        var meetings = Meetings(tournament);
        var repeats = round.Games.Where(g => g.Result == GameResult.NotPlayed && TimesMet(meetings, round, g) >= 2).ToList();
        if (repeats.Count == 0) return;

        var number = round.Games.Max(g => g.Number);
        foreach (var game in repeats)
        {
            var black = game.Black!;
            game.Black = null;
            game.BlackId = null;
            game.Result = GameResult.Absent;
            round.Games.Add(new Game { Number = ++number, White = black, Result = GameResult.Absent });
        }

        // Absence lines take no board; the remaining games get the boards again in pairing order.
        foreach (var game in round.Games) game.Board = null;
        AssignBoards(round, tournament.Boards);
    }

    /// <summary>
    /// Cancels the round in progress, as if it was never started: its games (and results) are removed, and the
    /// round is paired again as the pending round.
    /// </summary>
    public Task<TournamentDto> CancelRoundAsync(int id) =>
        MutateAsync(id, async tournament =>
        {
            var state = StateOf(tournament);
            if (state == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
            if (state != TournamentState.RoundActive) throw new ApiException(ApiErrorCode.NoRoundInProgress);

            var round = StartedRounds(tournament).Last();
            // Only games rated before ratings were processed at closing can have a change to take back.
            foreach (var game in round.Games.Where(g => g.IsRated))
                await RevertRatingsAsync(tournament, game, game.Result, RatingChangeReason.RoundCancelled);

            // Catch-up games entered during this round were played anyway: they move to the round before.
            var catchUps = round.Games.Where(g => g.IsCatchUp).ToList();
            if (catchUps.Count > 0)
            {
                var previous = StartedRounds(tournament).Reverse().Skip(1).FirstOrDefault()
                               ?? throw new ApiException(ApiErrorCode.CatchUpGamesInRound);
                var number = previous.Games.Max(g => g.Number);
                foreach (var game in catchUps)
                {
                    game.Number = ++number;
                    game.RoundId = previous.Id;
                }
                // Moves them from this round's games to the previous round's (so they are not deleted with this round).
                db.ChangeTracker.DetectChanges();
            }

            await RemovePendingRoundAsync(tournament);
            tournament.Rounds.Remove(round);
            db.Rounds.Remove(round);

            // A player who withdrew during this round and has no other games is removed completely,
            // as if they withdrew before the round.
            var withdrawnWithoutGames = tournament.Players
                .Where(p => !p.IsActive && !StartedRounds(tournament).SelectMany(r => r.Games)
                    .Any(g => g.WhiteId == p.Id || g.BlackId == p.Id))
                .ToList();
            foreach (var participant in withdrawnWithoutGames)
            {
                tournament.Players.Remove(participant);
                db.TournamentPlayers.Remove(participant);
            }

            // Save now so the round can be paired again under the same number (unique index).
            await db.SaveChangesAsync();
            await RefreshPendingRoundAsync(tournament);
        });

    public Task<TournamentDto> SetResultAsync(int id, int gameId, GameResult result) =>
        MutateAsync(id, async tournament =>
        {
            var game = tournament.Rounds.SelectMany(r => r.Games).SingleOrDefault(g => g.Id == gameId)
                       ?? throw new ApiException(ApiErrorCode.GameNotFound);

            if (StateOf(tournament) == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
            if (game.IsCatchUp)
            {
                await SetCatchUpResultAsync(tournament, game, result);
                return;
            }

            // Only the most recent started round can be (corrected) entered.
            var lastStarted = StartedRounds(tournament).LastOrDefault();
            if (lastStarted is null || game.RoundId != lastStarted.Id) throw new ApiException(ApiErrorCode.ResultNotEditable);
            if (game.BlackId is null) throw new ApiException(ApiErrorCode.ResultNotEditable);
            if (!IsGameResult(result) && result != GameResult.NotPlayed) throw new ApiException(ApiErrorCode.InvalidResult);

            // A game in the queue has not started yet: it has no result.
            if (game.Board is null) throw new ApiException(ApiErrorCode.ResultNotEditable);

            if (game.Result == result) return;
            // Ratings only change when the tournament is closed. A game rated before that (older versions) has its
            // change taken back when the result is corrected; the new result is rated at closing.
            if (game.IsRated) await RevertRatingsAsync(tournament, game, game.Result, RatingChangeReason.GameReverted);
            game.Result = result;

            // A reopened game whose board has been taken by the next game goes back into the queue.
            if (result == GameResult.NotPlayed
                && lastStarted.Games.Any(g => g != game && g.Board == game.Board && g.Result == GameResult.NotPlayed))
                game.Board = null;

            // A finished game frees its board for the next game in the queue.
            AssignBoards(lastStarted, tournament.Boards);

            // The round is over when all games have a result: the next round is paired. A cleared result reopens it.
            if (StateOf(tournament) == TournamentState.WaitingForNextRound)
                await RefreshPendingRoundAsync(tournament);
            else
                await RemovePendingRoundAsync(tournament);
        });

    /// <summary>
    /// Competition: enters a game the two players played outside a club evening. It must be one of their open games
    /// (with these colors), and they must not have it in the round being played. The game is added to the latest
    /// started round as a catch-up game: no board, with the result right away.
    /// </summary>
    public Task<TournamentDto> AddCatchUpGameAsync(int id, CatchUpGameRequest request) =>
        MutateAsync(id, async tournament =>
        {
            EnsureCompetition(tournament);
            var state = StateOf(tournament);
            if (state == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
            if (!IsGameResult(request.Result)) throw new ApiException(ApiErrorCode.InvalidResult);
            var round = StartedRounds(tournament).LastOrDefault()
                        ?? throw new ApiException(ApiErrorCode.GameNotOpen); // nothing to catch up before round 1

            var white = tournament.Players.SingleOrDefault(p => p.PlayerId == request.WhitePlayerId && p.IsActive);
            var black = tournament.Players.SingleOrDefault(p => p.PlayerId == request.BlackPlayerId && p.IsActive);
            if (white is null || black is null) throw new ApiException(ApiErrorCode.PlayerNotInTournament);

            var inThisRound = state == TournamentState.RoundActive && round.Games.Any(g =>
                g.Result == GameResult.NotPlayed && g.BlackId is not null
                && PairKey(g.WhiteId, g.BlackId.Value) == PairKey(white.Id, black.Id));
            if (inThisRound) throw new ApiException(ApiErrorCode.GameInCurrentRound);

            var open = ScheduleOf(tournament).OpenGamesBetween(white.Id, black.Id)
                .Any(g => g.WhiteId is null || g.WhiteId == white.Id);
            if (!open) throw new ApiException(ApiErrorCode.GameNotOpen);

            round.Games.Add(new Game
            {
                Number = round.Games.Max(g => g.Number) + 1,
                White = white,
                Black = black,
                Result = request.Result,
                IsCatchUp = true,
                PlayedAt = time.GetUtcNow().UtcDateTime
            });

            // The pairing of the next round must not contain this game any more.
            if (state == TournamentState.WaitingForNextRound) await RefreshPendingRoundAsync(tournament);
        });

    /// <summary>Corrects the result of a catch-up game; clearing it removes the game (it is open again).</summary>
    private async Task SetCatchUpResultAsync(Tournament tournament, Game game, GameResult result)
    {
        if (result == GameResult.NotPlayed)
        {
            tournament.Rounds.Single(r => r.Id == game.RoundId).Games.Remove(game);
            db.Games.Remove(game);
        }
        else if (IsGameResult(result))
            game.Result = result;
        else
            throw new ApiException(ApiErrorCode.InvalidResult);

        if (StateOf(tournament) == TournamentState.WaitingForNextRound) await RefreshPendingRoundAsync(tournament);
    }

    /// <summary>A result of a game between two players (win, draw, loss, forfeit).</summary>
    private static bool IsGameResult(GameResult result) =>
        result is GameResult.WhiteWins or GameResult.Draw or GameResult.BlackWins
            or GameResult.WhiteWinsByForfeit or GameResult.BlackWinsByForfeit;

    /// <summary>Takes back the rating change of a game that was rated right after its result (older versions,
    /// before ratings were processed at closing), and logs that.</summary>
    /// <param name="result">The result the game was rated with.</param>
    private async Task RevertRatingsAsync(Tournament tournament, Game game, GameResult result, RatingChangeReason reason)
    {
        var white = game.White.Player;
        var black = game.Black!.Player;
        int whiteBefore = white.Rating, blackBefore = black.Rating;
        var round = tournament.Rounds.Single(r => r.Id == game.RoundId).Number;

        white.Rating -= (int)game.WhiteRatingChange!.Value;
        black.Rating -= (int)game.BlackRatingChange!.Value;
        await ratingLog.AddAsync(white, whiteBefore, reason, new RatedGameInfo(
            tournament.Id, tournament.Title, round, black, game.BlackRatingBefore!.Value, result, IsWhite: true));
        await ratingLog.AddAsync(black, blackBefore, reason, new RatedGameInfo(
            tournament.Id, tournament.Title, round, white, game.WhiteRatingBefore!.Value, result, IsWhite: false));

        game.WhiteRatingBefore = game.BlackRatingBefore = null;
        game.WhiteRatingChange = game.BlackRatingChange = null;
        game.WhiteK = game.BlackK = null;
        game.RatedAt = null;
    }

    /// <summary>
    /// Processes the ratings of a closed, rated tournament: every game is calculated with the ratings of the players
    /// before the tournament, the exact changes are added up per player and rounded once (as FIDE does per period).
    /// </summary>
    private async Task ApplyRatingsAsync(Tournament tournament)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var players = await ratingLog.RatedPlayersAsync(tournament.Players.Select(p => p.Player), now.Year);
        var calculated = TournamentRatings.Calculate(tournament, players);

        foreach (var (game, white, black) in calculated)
        {
            game.WhiteRatingBefore = white.Rating;
            game.BlackRatingBefore = black.Rating;
            game.WhiteRatingChange = white.ExactChange;
            game.BlackRatingChange = black.ExactChange;
            game.WhiteK = white.K;
            game.BlackK = black.K;
            game.RatedAt = now;
        }

        foreach (var total in TournamentRatings.Totals(calculated))
        {
            var player = tournament.Players.First(p => p.PlayerId == total.PlayerId).Player;
            var before = player.Rating;
            player.Rating = Math.Max(0, player.Rating + total.Change);
            await ratingLog.AddAsync(player, before, RatingChangeReason.Tournament,
                new RatedGameInfo(tournament.Id, tournament.Title, null, null, null, null, null, total.Games));
        }
    }

    /// <summary>Closes the tournament and processes the ratings. The report is not written automatically: the leader asks for it
    /// (with the report wizard) on the Report tab.</summary>
    public Task<TournamentDto> CloseAsync(int id) =>
        MutateAsync(id, async tournament =>
        {
            EnsureBetweenRounds(tournament);

            await RemovePendingRoundAsync(tournament);
            tournament.ClosedAt = time.GetUtcNow().UtcDateTime;
            if (tournament.IsRated) await ApplyRatingsAsync(tournament);
        });

    /// <summary>
    /// (Re)writes the report of a closed tournament in the background, with the noteworthy events and the
    /// writing style the leader gave in the report wizard.
    /// </summary>
    public async Task<TournamentDto> GenerateReportAsync(int id, GenerateReportRequest request)
    {
        var wizard = await WizardInputAsync(request);

        var result = await MutateAsync(id, tournament =>
        {
            if (StateOf(tournament) != TournamentState.Closed) throw new ApiException(ApiErrorCode.ReportNotAvailable);

            // An earlier report stays until the new one is ready.
            wizard.ApplyTo(tournament.Report ??= new TournamentReport());
            return Task.CompletedTask;
        });

        reportQueue.Enqueue(new ReportJob(id));
        return result;
    }

    /// <summary>
    /// (Re)writes the report of one finished round in the background, like <see cref="GenerateReportAsync"/>.
    /// Possible once every game of the round has a result, also while the tournament goes on.
    /// </summary>
    public async Task<TournamentDto> GenerateRoundReportAsync(int id, int roundNumber, GenerateReportRequest request)
    {
        var wizard = await WizardInputAsync(request);
        int roundId = 0;

        var result = await MutateAsync(id, async tournament =>
        {
            var round = StartedRounds(tournament).SingleOrDefault(r => r.Number == roundNumber)
                        ?? throw new ApiException(ApiErrorCode.ReportNotAvailable);
            if (round.Games.Any(g => g.Result == GameResult.NotPlayed)) throw new ApiException(ApiErrorCode.ReportNotAvailable);

            roundId = round.Id;
            var report = await db.RoundReports.FindAsync(round.Id);
            if (report is null)
            {
                report = new RoundReport { RoundId = round.Id };
                db.RoundReports.Add(report);
            }
            wizard.ApplyTo(report);
        });

        reportQueue.Enqueue(new ReportJob(id, roundId));
        return result;
    }

    /// <summary>The prompt that produced the current report of a round.</summary>
    public async Task<ReportPromptDto> GetRoundReportPromptAsync(int id, int roundNumber)
    {
        if (!await db.Tournaments.AnyAsync(t => t.Id == id)) throw new ApiException(ApiErrorCode.TournamentNotFound);

        var prompt = await db.RoundReports
            .Where(r => r.Round.TournamentId == id && r.Round.Number == roundNumber && r.Round.StartedAt != null)
            .Select(r => new { r.SystemPrompt, r.UserPrompt, r.Model })
            .SingleOrDefaultAsync();

        if (prompt?.SystemPrompt is null || prompt.UserPrompt is null)
            throw new ApiException(ApiErrorCode.ReportNotAvailable);
        return new ReportPromptDto(prompt.SystemPrompt, prompt.UserPrompt, prompt.Model);
    }

    /// <summary>What the leader gave in the report wizard, checked and trimmed.</summary>
    private async Task<ReportWizardInput> WizardInputAsync(GenerateReportRequest request)
    {
        if (!await aiSettings.IsConfiguredAsync()) throw new ApiException(ApiErrorCode.AiNotConfigured);
        // No style chosen: the standard style from the settings; none when that is not set (no style is sent).
        var style = request.WritingStyleId is { } styleId ? await writingStyles.FindAsync(styleId)
            : await aiSettings.GetDefaultWritingStyleIdAsync() is { } defaultId ? await writingStyles.FindAsync(defaultId)
            : null;
        var events = (request.Events ?? [])
            .Select(e => e?.Trim() ?? "")
            .Where(e => e.Length > 0)
            .Select(e => e.Length <= WritingStyleRules.MaxEventLength ? e : e[..WritingStyleRules.MaxEventLength])
            .Take(WritingStyleRules.MaxEvents)
            .ToList();
        return new ReportWizardInput(events, style);
    }

    private sealed record ReportWizardInput(List<string> Events, WritingStyle? Style)
    {
        /// <summary>Marks the report as being written; an earlier text stays until the new one is ready.</summary>
        public void ApplyTo(AiReport report)
        {
            report.Status = ReportStatus.Generating;
            report.Error = null;
            report.Events = Events;
            report.WritingStyleId = Style?.Id;
            report.WritingStyleName = Style?.Name;
            report.WritingStyleDescription = Style?.Description;
        }
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
            .Include(t => t.Report)
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
        if (StateOf(tournament) == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
    }

    private static void EnsureCompetition(Tournament tournament)
    {
        if (tournament.PairingSystem != PairingSystem.Competition) throw new ApiException(ApiErrorCode.NotACompetition);
    }

    /// <summary>Settings, the start of a round and closing are only possible between rounds.</summary>
    private static void EnsureBetweenRounds(Tournament tournament)
    {
        var state = StateOf(tournament);
        if (state == TournamentState.Closed) throw new ApiException(ApiErrorCode.TournamentClosed);
        if (state == TournamentState.RoundActive) throw new ApiException(ApiErrorCode.RoundInProgress);
    }

    /// <summary>
    /// The state is not stored but follows from the data: closed when the leader closed it, new before round 1,
    /// round active while the last started round has games without a result, otherwise waiting for the next round.
    /// Keep in sync with the SQL version in <see cref="GetAllAsync"/>.
    /// </summary>
    private static TournamentState StateOf(Tournament tournament)
    {
        if (tournament.ClosedAt is not null) return TournamentState.Closed;
        var lastStarted = StartedRounds(tournament).LastOrDefault();
        if (lastStarted is null) return TournamentState.New;
        return lastStarted.Games.Any(g => g.Result == GameResult.NotPlayed)
            ? TournamentState.RoundActive
            : TournamentState.WaitingForNextRound;
    }

    private async Task RefreshPendingRoundAsync(Tournament tournament)
    {
        await RemovePendingRoundAsync(tournament);
        if (StateOf(tournament) is TournamentState.New or TournamentState.WaitingForNextRound)
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

    /// <summary>
    /// Pairs the next round and stores it as the pending round: the games, then a line for every player who takes
    /// part but has no opponent (competition) and for every absent player.
    /// </summary>
    private async Task<Round?> PairPendingRoundAsync(Tournament tournament, bool throwOnError)
    {
        var present = tournament.Players.Where(p => p is { IsActive: true, IsPresent: true }).ToList();
        var absent = tournament.Players.Where(p => p is { IsActive: true, IsPresent: false })
            .OrderBy(p => p.Player.Name).ToList();

        var input = new PairingInput(
            present.Select(p => new PairingPlayer(p.Id, p.Player.Rating)).ToList(),
            PlayedGames(tournament),
            ScoringOf(tournament));

        IReadOnlyList<BoardPairing> pairings;
        try
        {
            pairings = PairingStrategyOf(tournament).PairNextRound(input);
        }
        catch (PairingException ex)
        {
            if (throwOnError) throw new ApiException(ToErrorCode(ex.Error));
            return null;
        }

        var participants = present.ToDictionary(p => p.Id);
        var round = new Round { Number = StartedRounds(tournament).Count() + 1 };
        var games = pairings
            .Select(p => new Game
            {
                White = participants[p.WhiteId],
                Black = p.BlackId is { } blackId ? participants[blackId] : null,
                Result = p.IsBye ? GameResult.Bye : GameResult.NotPlayed
            })
            .ToList();
        var paired = pairings.SelectMany(p => new[] { p.WhiteId, p.BlackId ?? p.WhiteId }).ToHashSet();
        games.AddRange(present.Where(p => !paired.Contains(p.Id)).OrderBy(p => p.Player.Name)
            .Select(p => new Game { White = p, Result = GameResult.NoOpponent }));
        games.AddRange(absent.Select(p => new Game { White = p, Result = GameResult.Absent }));
        for (var i = 0; i < games.Count; i++) games[i].Number = i + 1;
        round.Games.AddRange(games);
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
        PairingError.CompetitionComplete => ApiErrorCode.CompetitionComplete,
        PairingError.NoGamesBetweenPresentPlayers => ApiErrorCode.NoGamesBetweenPresentPlayers,
        _ => ApiErrorCode.NoValidPairing
    };

    private static IPairingStrategy PairingStrategyOf(Tournament tournament) => tournament.PairingSystem switch
    {
        PairingSystem.Swiss => new SwissPairing(),
        PairingSystem.Competition => new CompetitionPairing(
            tournament.TimesAgainstEachOther,
            tournament.Players.Select(p => p.Id).Order().ToList(),
            ActiveParticipantIds(tournament)),
        _ => throw new ArgumentOutOfRangeException(nameof(tournament), tournament.PairingSystem, null)
    };

    /// <summary>The participants who did not withdraw (present or not).</summary>
    private static List<int> ActiveParticipantIds(Tournament tournament) =>
        tournament.Players.Where(p => p.IsActive).Select(p => p.Id).Order().ToList();

    /// <summary>Competition: the games played and still to play, counting every started round (also the one in progress).</summary>
    private static CompetitionSchedule ScheduleOf(Tournament tournament) =>
        new(tournament.TimesAgainstEachOther, PlayedGames(tournament));

    /// <summary>Competition: the number of games still to play between the players in the competition.</summary>
    private static int OpenGameCount(Tournament tournament) =>
        tournament.PairingSystem == PairingSystem.Competition
            ? ScheduleOf(tournament).OpenGames(ActiveParticipantIds(tournament)).Count()
            : 0;

    private static Round? PendingRound(Tournament tournament) =>
        tournament.Rounds.SingleOrDefault(r => r.StartedAt is null);

    private static IEnumerable<Round> StartedRounds(Tournament tournament) =>
        tournament.Rounds.Where(r => r.StartedAt is not null).OrderBy(r => r.Number);

    /// <summary>After round 1 the pairing system, the times against each other and the points are fixed.</summary>
    private static bool IsSettingsLocked(Tournament tournament) => StartedRounds(tournament).Any();

    private static List<PlayedGame> PlayedGames(Tournament tournament) =>
        StartedRounds(tournament)
            .SelectMany(r => r.Games.Select(g => new PlayedGame(r.Number, g.WhiteId, g.BlackId, g.Result)))
            .ToList();

    private static ScoringRules ScoringOf(Tournament t) =>
        new(t.PointsForWin, t.PointsForDraw, t.PointsForLoss, t.PointsForBye, t.PointsForAbsence);

    private static TournamentSettingsDto SettingsOf(Tournament t) => new(
        t.PairingSystem, t.GameTimeMinutes, t.IncrementSeconds,
        t.PointsForWin, t.PointsForDraw, t.PointsForLoss, t.PointsForBye, t.Boards, t.IsRated, t.PointsForAbsence,
        t.TimesAgainstEachOther);

    private static TournamentDto ToDto(Tournament t) => new(
        t.Id,
        t.Title,
        t.Location,
        t.Description,
        StateOf(t),
        SettingsOf(t),
        StartedRounds(t).Count(),
        IsSettingsLocked(t),
        t.Report is { } report
            ? new TournamentReportDto(report.Status, report.Text, report.GeneratedAt, report.Error,
                report.Model, report.UserPrompt is not null,
                report.Events, report.WritingStyleId, report.WritingStyleName)
            : new TournamentReportDto(ReportStatus.None, null, null, null, null, false),
        OpenGameCount(t));
}
