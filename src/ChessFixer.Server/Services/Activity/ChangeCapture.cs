using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ChessFixer.Server.Data;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ChessFixer.Server.Services.Activity;

/// <summary>
/// Records every row that a request adds, changes or deletes (with the values before and after), so the activity log
/// can show what an action did and the administrator can revert it. The changes are collected per request in
/// <see cref="HttpContext.Items"/> and written by the <see cref="Auth.ActivityLogMiddleware"/> together with the entry.
/// Changes outside a request (the report worker) are not recorded.
/// </summary>
public class ChangeCapture(IHttpContextAccessor http) : SaveChangesInterceptor
{
    private const string ItemsKey = "ChessFixer.ActivityChanges";

    /// <summary>Never stored, only that they changed: secrets and photos.</summary>
    private static readonly HashSet<(string Entity, string Property)> Masked =
    [
        (nameof(AppUser), nameof(AppUser.PasswordHash)),
        (nameof(AppUser), nameof(AppUser.SecurityStamp)),
        (nameof(AppSettings), nameof(AppSettings.ClaudeApiKey)),
        (nameof(PlayerPhoto), nameof(PlayerPhoto.Data))
    ];

    /// <summary>Bookkeeping that is not interesting in the log.</summary>
    private static readonly HashSet<(string Entity, string Property)> Ignored =
    [
        (nameof(AppUser), nameof(AppUser.LastSeenAt))
    ];

    /// <summary>The log itself is not recorded.</summary>
    private static readonly HashSet<Type> NotRecorded = [typeof(ActivityLogEntry), typeof(ActivityChange), typeof(GameAnalysis)];

    private sealed record Pending(EntityEntry Entry, ActivityChangeKind Kind, Dictionary<string, object?> Before);

    private readonly ConditionalWeakTable<DbContext, List<Pending>> _pending = new();

    /// <summary>The changes recorded in this request so far.</summary>
    public static List<RecordedChange> Of(HttpContext context) =>
        context.Items.TryGetValue(ItemsKey, out var list) && list is List<RecordedChange> changes ? changes : [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Complete(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Complete(eventData.Context);
        return result;
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) _pending.Remove(context);
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context) _pending.Remove(context);
    }

    /// <summary>Before saving: which rows change, and their values before (after saving those are gone).</summary>
    private void Collect(DbContext? context)
    {
        if (context is null || http.HttpContext is null) return;
        context.ChangeTracker.DetectChanges();

        var pending = new List<Pending>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (NotRecorded.Contains(entry.Metadata.ClrType)) continue;
            var kind = entry.State switch
            {
                EntityState.Added => ActivityChangeKind.Added,
                EntityState.Modified => ActivityChangeKind.Modified,
                EntityState.Deleted => ActivityChangeKind.Deleted,
                _ => (ActivityChangeKind?)null
            };
            if (kind is null) continue;

            var before = new Dictionary<string, object?>();
            if (kind != ActivityChangeKind.Added)
                foreach (var property in entry.Properties)
                    before[property.Metadata.Name] = property.OriginalValue;
            pending.Add(new Pending(entry, kind.Value, before));
        }
        _pending.AddOrUpdate(context, pending);
    }

    /// <summary>After saving: the generated keys and the values after; added to the changes of the request.</summary>
    private void Complete(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        if (http.HttpContext is not { } httpContext) return;

        var changes = Of(httpContext);
        foreach (var (entry, kind, before) in pending)
        {
            var type = entry.Metadata;
            var entity = type.ClrType.Name;
            var properties = new List<ActivityPropertyChangeDto>();
            foreach (var property in type.GetProperties())
            {
                var name = property.Name;
                if (property.IsPrimaryKey() || property.ValueGenerated == ValueGenerated.OnAddOrUpdate
                    || Ignored.Contains((entity, name))) continue;

                var old = kind == ActivityChangeKind.Added ? null : before.GetValueOrDefault(name);
                var @new = kind == ActivityChangeKind.Deleted ? null : entry.Property(name).CurrentValue;
                if (kind == ActivityChangeKind.Modified && ValueText.Equal(old, @new)) continue;
                // An added row lists what was filled in; a deleted row everything it had (needed to restore it).
                if (kind == ActivityChangeKind.Added && @new is null or "") continue;
                if (kind == ActivityChangeKind.Deleted && old is null) continue;

                properties.Add(Masked.Contains((entity, name))
                    ? new ActivityPropertyChangeDto(name, null, null, IsMasked: true)
                    : new ActivityPropertyChangeDto(name, ValueText.Format(old), ValueText.Format(@new)));
            }
            if (kind == ActivityChangeKind.Modified && properties.Count == 0) continue;

            var key = type.FindPrimaryKey()!.Properties
                .Select(p => kind == ActivityChangeKind.Deleted ? before.GetValueOrDefault(p.Name) : entry.Property(p.Name).CurrentValue);
            changes.Add(new RecordedChange(entity, string.Join(",", key.Select(ValueText.Format)), LabelOf(entry.Entity), kind, properties));
        }
        httpContext.Items[ItemsKey] = changes;
    }

    /// <summary>A readable name of the row, from what is loaded.</summary>
    private static string? LabelOf(object entity) => entity switch
    {
        Player p => p.Name is { Length: > 0 } name ? name : $"{p.FirstName} {p.LastName}".Trim(),
        Tournament t => t.Title,
        Club c => c.Name,
        WritingStyle w => w.Name,
        AppUser u => u.DisplayName,
        TournamentPlayer tp => PlayerName(tp),
        Round r => r.Number.ToString(CultureInfo.InvariantCulture),
        Game g => PlayerName(g.White) is { } white
            ? g.Black is null ? white : $"{white} – {PlayerName(g.Black)}"
            : null,
        RatingLogEntry r => r.Player is { } player ? LabelOf(player) : null,
        PlayerPhoto => null,
        TournamentReport => null,
        _ => null
    };

    private static string? PlayerName(TournamentPlayer? participant) =>
        participant?.Player is { } player ? LabelOf(player) : null;
}

/// <summary>A change recorded in a request, before it is stored with the log entry.</summary>
public record RecordedChange(
    string EntityType, string EntityKey, string? Label, ActivityChangeKind Kind, List<ActivityPropertyChangeDto> Properties)
{
    public ActivityChange ToEntity(int sequence) => new()
    {
        Sequence = sequence,
        EntityType = EntityType,
        EntityKey = EntityKey.Length <= 50 ? EntityKey : EntityKey[..50],
        Label = Label is { Length: > 200 } ? Label[..200] : Label,
        Kind = Kind,
        Properties = JsonSerializer.Serialize(Properties)
    };
}

/// <summary>Values as invariant text, for the log and back again for a revert.</summary>
public static class ValueText
{
    public static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        byte[] bytes => $"{bytes.Length} bytes",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        System.Collections.IEnumerable list => JsonSerializer.Serialize(list),
        _ => value.ToString()
    };

    public static bool Equal(object? a, object? b) => Format(a) == Format(b);

    /// <summary>Text from <see cref="Format"/> back to a value of the given type.</summary>
    public static object? Parse(string? text, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (text is null) return null;
        if (target == typeof(string)) return text;
        if (target == typeof(bool)) return text == "true";
        if (target == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target == typeof(DateOnly)) return DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (target.IsEnum) return Enum.Parse(target, text);
        if (target == typeof(List<string>)) return JsonSerializer.Deserialize<List<string>>(text);
        return Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
    }
}
