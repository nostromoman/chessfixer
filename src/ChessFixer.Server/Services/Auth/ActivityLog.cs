using ChessFixer.Server.Data;
using ChessFixer.Server.Services.Activity;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ChessFixer.Server.Services.Auth;

/// <summary>
/// Writes the activity log (see <see cref="ActivityLogEntry"/>). Uses its own database context, so an entry is never
/// saved together with the half-done changes of a request that failed. Writing the log never fails a request.
/// </summary>
public class ActivityLog(IServiceScopeFactory scopes, TimeProvider time, ILogger<ActivityLog> logger)
{
    /// <summary>Entries older than this are deleted when the server starts.</summary>
    public const int RetentionDays = 365;

    /// <param name="typedUserName">For a failed login: the user name that was typed.</param>
    public Task LogAsync(HttpContext http, ActivityKind kind, AppUser? user, string? typedUserName = null) =>
        SaveAsync(http, new ActivityLogEntry
        {
            UserId = user?.Id,
            UserName = Truncate(user?.UserName ?? typedUserName ?? "", 100)!,
            DisplayName = user?.DisplayName,
            Kind = kind
        });

    /// <summary>Set by a revert, so its entry refers to the entry it reverted.</summary>
    public const string RevertOfItemsKey = "ChessFixer.RevertOf";

    /// <summary>A change through the API, with the status code of the answer and (when it succeeded) what it changed.</summary>
    public Task LogActionAsync(HttpContext http, int statusCode)
    {
        var action = http.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var changes = statusCode < 400 ? ChangeCapture.Of(http) : [];
        return SaveAsync(http, new ActivityLogEntry
        {
            UserId = UserSession.GetUserId(http.User),
            UserName = Truncate(http.User.Identity?.Name ?? "", 100)!,
            Kind = ActivityKind.Action,
            Action = action is null ? null : $"{action.ControllerName}.{action.ActionName}",
            Method = http.Request.Method,
            Path = Truncate(http.Request.Path.Value, 300),
            StatusCode = statusCode,
            RevertOfId = statusCode < 400 && http.Items.TryGetValue(RevertOfItemsKey, out var revertOf) ? revertOf as long? : null,
            Changes = changes.Select((change, i) => change.ToEntity(i)).ToList()
        });
    }

    private async Task SaveAsync(HttpContext http, ActivityLogEntry entry)
    {
        try
        {
            entry.At = time.GetUtcNow().UtcDateTime;
            entry.IpAddress = http.Connection.RemoteIpAddress?.ToString();
            entry.UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 300);

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChessFixerDbContext>();
            // The cookie only has the login name; the log also shows the display name.
            if (entry.DisplayName is null && entry.UserId is { } userId)
                entry.DisplayName = (await db.Users.FindAsync(userId))?.DisplayName;
            db.ActivityLog.Add(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not write the activity log ({Kind} by {UserName})", entry.Kind, entry.UserName);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Logs every change (POST, PUT, PATCH, DELETE) a logged-in user makes through the API, also when it is refused (403)
/// or fails. Runs before authorization, so refused attempts are logged too. Logging in and out and changing the
/// password are logged by the AuthController itself.
/// </summary>
public class ActivityLogMiddleware(RequestDelegate next, ActivityLog log)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!ShouldLog(http))
        {
            await next(http);
            return;
        }

        try
        {
            await next(http);
        }
        catch
        {
            await log.LogActionAsync(http, StatusCodes.Status500InternalServerError);
            throw;
        }
        await log.LogActionAsync(http, http.Response.StatusCode);
    }

    private static bool ShouldLog(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true
        && !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)
        && !HttpMethods.IsOptions(http.Request.Method)
        && http.Request.Path.StartsWithSegments("/api")
        && !http.Request.Path.StartsWithSegments("/api/auth");
}
