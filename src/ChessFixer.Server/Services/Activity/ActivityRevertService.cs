using System.Text.Json;
using System.Text.RegularExpressions;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Server.Services.Ratings;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Activity;

/// <summary>
/// Reverts an action from the activity log (system administrator only). Only when nothing it changed has been changed
/// again since, otherwise that later change would be undone too.
/// <list type="bullet">
/// <item>Tournament actions are reverted through the <see cref="TournamentService"/>, so its rules and side effects
/// (pairing the next round, the board queue, live updates) apply as for any change.</item>
/// <item>Players, clubs, writing styles, AI settings and leader names are simple rows: their values are put back.</item>
/// </list>
/// The revert is an action itself: it is logged with its own changes and refers to the reverted entry.
/// </summary>
public partial class ActivityRevertService(
    ChessFixerDbContext db, TournamentService tournaments, RatingLogService ratingLog, IHttpContextAccessor http)
{
    /// <summary>Actions whose rows are put back as they were.</summary>
    private static readonly HashSet<string> RowActions =
    [
        "Players.Create", "Players.Update", "Players.Delete",
        "Clubs.Create", "Clubs.Update", "Clubs.Delete",
        "Settings.UpdateAi", "Settings.CreateWritingStyle", "Settings.UpdateWritingStyle", "Settings.DeleteWritingStyle",
        "Leaders.Create", "Leaders.Update"
    ];

    /// <summary>Tournament actions with their counter-action.</summary>
    private static readonly HashSet<string> TournamentActions =
    [
        "Tournaments.Update", "Tournaments.SetResult", "Tournaments.AddPlayer", "Tournaments.RemovePlayer",
        "Tournaments.SetPresence", "Tournaments.StartNextRound"
    ];

    /// <summary>Rows that are put back. The rating log is never changed: a rating that is put back gets a new entry.</summary>
    private static readonly HashSet<string> RowTypes =
        [nameof(Player), nameof(Club), nameof(WritingStyle), nameof(AppSettings), nameof(AppUser)];

    /// <summary>Whether a revert can be tried (whether it succeeds depends on what changed since).</summary>
    public static bool IsRevertible(ActivityLogEntry entry, IReadOnlyList<ActivityChangeDto> changes, bool isReverted)
    {
        if (isReverted || entry is not { Kind: ActivityKind.Action, StatusCode: < 400, Action: { } action }) return false;
        var relevant = changes.Where(c => c.EntityType != nameof(RatingLogEntry)).ToList();
        if (relevant.Count == 0) return false;
        if (TournamentActions.Contains(action)) return true;
        // A secret is not stored, so a change of it cannot be put back (an added row can still be removed).
        return RowActions.Contains(action)
               && relevant.All(c => RowTypes.Contains(c.EntityType)
                                    && (c.Kind == ActivityChangeKind.Added || !c.Properties.Any(p => p.IsMasked)));
    }

    public static List<ActivityChangeDto> ToDtos(IEnumerable<ActivityChange> changes) =>
        changes.OrderBy(c => c.Sequence)
            .Select(c => new ActivityChangeDto(c.EntityType, c.EntityKey, c.Label, c.Kind,
                JsonSerializer.Deserialize<List<ActivityPropertyChangeDto>>(c.Properties) ?? []))
            .ToList();

    public async Task RevertAsync(long entryId)
    {
        var entry = await db.ActivityLog.Include(e => e.Changes).SingleOrDefaultAsync(e => e.Id == entryId)
                    ?? throw new ApiException(ApiErrorCode.NotRevertible);
        if (await db.ActivityLog.AnyAsync(e => e.RevertOfId == entryId)) throw new ApiException(ApiErrorCode.AlreadyReverted);

        var changes = ToDtos(entry.Changes);
        if (!IsRevertible(entry, changes, isReverted: false)) throw new ApiException(ApiErrorCode.NotRevertible);

        // The log entry of this request refers to the reverted entry.
        if (http.HttpContext is { } context) context.Items[ActivityLog.RevertOfItemsKey] = entryId;

        var relevant = changes.Where(c => c.EntityType != nameof(RatingLogEntry)).ToList();
        if (TournamentActions.Contains(entry.Action!))
            await RevertTournamentActionAsync(entry.Action!, entry.Path ?? "", relevant);
        else
            await RevertRowsAsync(relevant);
    }

    // ---------- Tournament actions: the counter-action through the tournament service ----------

    private async Task RevertTournamentActionAsync(string action, string path, List<ActivityChangeDto> changes)
    {
        var match = TournamentPath().Match(path);
        if (!match.Success) throw new ApiException(ApiErrorCode.NotRevertible);
        var tournamentId = int.Parse(match.Groups[1].Value);

        switch (action)
        {
            case "Tournaments.SetResult":
            {
                var change = Modified(changes, nameof(Game), nameof(Game.Result))
                             ?? throw new ApiException(ApiErrorCode.NotRevertible);
                var gameId = int.Parse(change.EntityKey);
                var result = Property(change, nameof(Game.Result));
                var game = await db.Games.FindAsync(gameId) ?? throw new ApiException(ApiErrorCode.ChangedSinceAction);
                if (game.Result.ToString() != result.After) throw new ApiException(ApiErrorCode.ChangedSinceAction);
                await tournaments.SetResultAsync(tournamentId, gameId, Enum.Parse<GameResult>(result.Before!));
                break;
            }
            case "Tournaments.Update":
            {
                var change = Modified(changes, nameof(Tournament), null)
                             ?? throw new ApiException(ApiErrorCode.NotRevertible);
                var tournament = await db.Tournaments.FindAsync(tournamentId) ?? throw new ApiException(ApiErrorCode.TournamentNotFound);
                EnsureUnchanged(tournament, change);
                // Put the old values on the loaded row, build the request from it, and let the service apply it.
                SetValues(tournament, change, before: true);
                var request = new UpdateTournamentRequest(tournament.Title,
                    new TournamentSettingsDto(tournament.PairingSystem, tournament.GameTimeMinutes, tournament.IncrementSeconds,
                        tournament.PointsForWin, tournament.PointsForDraw, tournament.PointsForLoss, tournament.PointsForBye,
                        tournament.Boards, tournament.IsRated, tournament.PointsForAbsence, tournament.TimesAgainstEachOther),
                    tournament.Location, tournament.Description);
                db.ChangeTracker.Clear();
                await tournaments.UpdateAsync(tournamentId, request);
                break;
            }
            case "Tournaments.AddPlayer":
            {
                var participant = await ParticipantAsync(changes);
                if (!participant.IsActive) throw new ApiException(ApiErrorCode.ChangedSinceAction);
                await tournaments.RemovePlayerAsync(tournamentId, participant.PlayerId);
                break;
            }
            case "Tournaments.RemovePlayer":
            {
                var playerId = int.Parse(match.Groups[3].Value is { Length: > 0 } id ? id : throw new ApiException(ApiErrorCode.NotRevertible));
                if (await db.TournamentPlayers.AnyAsync(p => p.TournamentId == tournamentId && p.PlayerId == playerId && p.IsActive))
                    throw new ApiException(ApiErrorCode.ChangedSinceAction);
                await tournaments.AddPlayerAsync(tournamentId, playerId);
                break;
            }
            case "Tournaments.SetPresence":
            {
                var change = Modified(changes, nameof(TournamentPlayer), nameof(TournamentPlayer.IsPresent))
                             ?? throw new ApiException(ApiErrorCode.NotRevertible);
                var participant = await db.TournamentPlayers.FindAsync(int.Parse(change.EntityKey))
                                  ?? throw new ApiException(ApiErrorCode.ChangedSinceAction);
                var isPresent = Property(change, nameof(TournamentPlayer.IsPresent));
                if (!participant.IsActive || (participant.IsPresent ? "true" : "false") != isPresent.After)
                    throw new ApiException(ApiErrorCode.ChangedSinceAction);
                await tournaments.SetPresenceAsync(tournamentId, participant.PlayerId, bool.Parse(isPresent.Before!));
                break;
            }
            case "Tournaments.StartNextRound":
            {
                var change = Modified(changes, nameof(Round), nameof(Round.StartedAt))
                             ?? throw new ApiException(ApiErrorCode.NotRevertible);
                var roundId = int.Parse(change.EntityKey);
                var round = await db.Rounds.Include(r => r.Games).SingleOrDefaultAsync(r => r.Id == roundId)
                            ?? throw new ApiException(ApiErrorCode.ChangedSinceAction);
                // Only while it is the round in progress and no result has been entered: cancelling removes its games.
                var lastStarted = await db.Rounds.Where(r => r.TournamentId == tournamentId && r.StartedAt != null)
                    .MaxAsync(r => (int?)r.Number);
                if (round.Number != lastStarted
                    || round.Games.Any(g => g.Result is not (GameResult.NotPlayed or GameResult.Bye or GameResult.Absent or GameResult.NoOpponent)))
                    throw new ApiException(ApiErrorCode.ChangedSinceAction);
                await tournaments.CancelRoundAsync(tournamentId);
                break;
            }
            default:
                throw new ApiException(ApiErrorCode.NotRevertible);
        }
    }

    /// <summary>The participant that AddPlayer added (a new row) or made active again.</summary>
    private async Task<TournamentPlayer> ParticipantAsync(List<ActivityChangeDto> changes)
    {
        var change = changes.FirstOrDefault(c => c.EntityType == nameof(TournamentPlayer)
                                                 && (c.Kind == ActivityChangeKind.Added || Has(c, nameof(TournamentPlayer.IsActive))))
                     ?? throw new ApiException(ApiErrorCode.NotRevertible);
        return await db.TournamentPlayers.FindAsync(int.Parse(change.EntityKey))
               ?? throw new ApiException(ApiErrorCode.ChangedSinceAction);
    }

    // ---------- Simple rows: put the values back ----------

    private async Task RevertRowsAsync(List<ActivityChangeDto> changes)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Rows that were deleted come back first (with their own key), so rows that referred to them can again.
        foreach (var change in changes.Where(c => c.Kind == ActivityChangeKind.Deleted))
        {
            var type = EntityType(change);
            if (await db.FindAsync(type.ClrType, Key(type, change)) is not null) throw new ApiException(ApiErrorCode.ChangedSinceAction);
            var entity = Activator.CreateInstance(type.ClrType)!;
            type.FindPrimaryKey()!.Properties[0].PropertyInfo!.SetValue(entity, Key(type, change)[0]);
            SetValues(entity, change, before: true);
            db.Add(entity);
            await db.SaveChangesAsync();
        }

        // Changed rows get their old values, in reverse order (one at a time, for unique indexes like the own club).
        foreach (var change in changes.Where(c => c.Kind == ActivityChangeKind.Modified).Reverse())
        {
            var type = EntityType(change);
            var entity = await db.FindAsync(type.ClrType, Key(type, change)) ?? throw new ApiException(ApiErrorCode.ChangedSinceAction);
            EnsureUnchanged(entity, change);
            var ratingBefore = (entity as Player)?.Rating;
            SetValues(entity, change, before: true);
            if (entity is Player player && ratingBefore is { } before)
                await ratingLog.AddAsync(player, before, RatingChangeReason.Manual);
            await db.SaveChangesAsync();
        }

        // Rows that were added are removed again; a player is (soft) deleted, as in the player list.
        foreach (var change in changes.Where(c => c.Kind == ActivityChangeKind.Added).Reverse())
        {
            var type = EntityType(change);
            var entity = await db.FindAsync(type.ClrType, Key(type, change)) ?? throw new ApiException(ApiErrorCode.ChangedSinceAction);
            EnsureUnchanged(entity, change);
            switch (entity)
            {
                case Player player:
                    player.IsDeleted = true;
                    break;
                case Club club when await db.Players.AnyAsync(p => p.ClubId == club.Id):
                    throw new ApiException(ApiErrorCode.ChangedSinceAction);
                case AppSettings:
                    throw new ApiException(ApiErrorCode.NotRevertible);
                default:
                    db.Remove(entity);
                    break;
            }
            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
    }

    private Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType(ActivityChangeDto change) =>
        db.Model.GetEntityTypes().SingleOrDefault(t => t.ClrType.Name == change.EntityType)
        ?? throw new ApiException(ApiErrorCode.NotRevertible);

    private static object[] Key(Microsoft.EntityFrameworkCore.Metadata.IEntityType type, ActivityChangeDto change) =>
        [ValueText.Parse(change.EntityKey, type.FindPrimaryKey()!.Properties[0].ClrType)!];

    /// <summary>The row must still have the values the action gave it.</summary>
    private void EnsureUnchanged(object entity, ActivityChangeDto change)
    {
        var entry = db.Entry(entity);
        foreach (var property in change.Properties)
        {
            if (property.IsMasked || entry.Metadata.FindProperty(property.Name) is null) continue;
            if (ValueText.Format(entry.Property(property.Name).CurrentValue) != property.After)
                throw new ApiException(ApiErrorCode.ChangedSinceAction);
        }
    }

    /// <summary>Puts the values before (or after) the action on the row.</summary>
    private void SetValues(object entity, ActivityChangeDto change, bool before)
    {
        var entry = db.Entry(entity);
        foreach (var property in change.Properties)
        {
            if (property.IsMasked) throw new ApiException(ApiErrorCode.NotRevertible);
            if (entry.Metadata.FindProperty(property.Name) is not { } metadata || metadata.IsPrimaryKey()) continue;
            entry.Property(property.Name).CurrentValue = ValueText.Parse(before ? property.Before : property.After, metadata.ClrType);
        }
    }

    /// <summary>The one changed (not added) row of the type, with the property when given. Newly paired games of the
    /// next round are added rows, so they are not mistaken for the game whose result was entered.</summary>
    private static ActivityChangeDto? Modified(List<ActivityChangeDto> changes, string entityType, string? property) =>
        changes.SingleOrDefault(c => c.Kind == ActivityChangeKind.Modified && c.EntityType == entityType
                                     && (property is null || Has(c, property)));

    private static bool Has(ActivityChangeDto change, string property) => change.Properties.Any(p => p.Name == property);

    private static ActivityPropertyChangeDto Property(ActivityChangeDto change, string property) =>
        change.Properties.Single(p => p.Name == property);

    [GeneratedRegex(@"^/api/tournaments/(\d+)(/players/(\d+))?")]
    private static partial Regex TournamentPath();
}
