using System.Text.RegularExpressions;
using ChessFixer.Server.Data;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Controllers;

/// <summary>The activity log: who logged in and what they did. Only for the system administrator.</summary>
[ApiController]
[Route("api/activity")]
[Authorize(Roles = nameof(UserRole.Admin))]
public partial class ActivityController(ChessFixerDbContext db) : ControllerBase
{
    public const int MaxPageSize = 200;

    /// <summary>The log, newest first.</summary>
    /// <param name="userId">Only the entries of this user.</param>
    /// <param name="problemsOnly">Only failed logins and actions that were refused or failed.</param>
    [HttpGet]
    public async Task<ActivityLogPageDto> GetLog(int skip = 0, int take = 50, int? userId = null, bool problemsOnly = false)
    {
        var query = db.ActivityLog.AsQueryable();
        if (userId is { } id) query = query.Where(e => e.UserId == id);
        if (problemsOnly) query = query.Where(e => e.Kind == ActivityKind.LoginFailed || e.StatusCode >= 400);

        var total = await query.CountAsync();
        var entries = await query
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxPageSize))
            .ToListAsync();

        // Show the tournament an action was about by its title (as long as it exists).
        var tournamentIds = entries.Select(e => TournamentIdOf(e.Path)).OfType<int>().Distinct().ToList();
        var titles = await db.Tournaments.Where(t => tournamentIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title);

        return new ActivityLogPageDto(total, entries.Select(e =>
        {
            int? tournamentId = TournamentIdOf(e.Path) is { } tid && titles.ContainsKey(tid) ? tid : null;
            return new ActivityLogEntryDto(e.Id, e.At, e.UserId, e.UserName, e.DisplayName, e.Kind, e.Action, e.Method,
                e.Path, e.StatusCode, e.IpAddress, e.UserAgent, tournamentId, tournamentId is { } t ? titles[t] : null);
        }).ToList());
    }

    /// <summary>All accounts with a summary of their activity; tournament leaders first, then by name.</summary>
    [HttpGet("users")]
    public async Task<List<ActivityUserDto>> GetUsers()
    {
        var users = await db.Users
            .OrderBy(u => u.Role == UserRole.Admin).ThenBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id, u.UserName, u.DisplayName, u.Role, u.LastSeenAt,
                LastLoginAt = db.ActivityLog.Where(e => e.UserId == u.Id && e.Kind == ActivityKind.Login).Max(e => (DateTime?)e.At),
                Logins = db.ActivityLog.Count(e => e.UserId == u.Id && e.Kind == ActivityKind.Login),
                FailedLogins = db.ActivityLog.Count(e => e.UserId == u.Id && e.Kind == ActivityKind.LoginFailed),
                Actions = db.ActivityLog.Count(e => e.UserId == u.Id && e.Kind == ActivityKind.Action),
                FailedActions = db.ActivityLog.Count(e => e.UserId == u.Id && e.Kind == ActivityKind.Action && e.StatusCode >= 400)
            })
            .ToListAsync();

        return users.Select(u => new ActivityUserDto(u.Id, u.UserName, u.DisplayName, u.Role,
            AsUtc(u.LastLoginAt), AsUtc(u.LastSeenAt), u.Logins, u.FailedLogins, u.Actions, u.FailedActions)).ToList();
    }

    private static int? TournamentIdOf(string? path) =>
        path is not null && TournamentPath().Match(path) is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;

    /// <summary>An aggregate (MAX) can bypass the UTC conversion of the column, so mark the value as UTC again.</summary>
    private static DateTime? AsUtc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    [GeneratedRegex(@"^/api/tournaments/(\d+)(/|$)")]
    private static partial Regex TournamentPath();
}
