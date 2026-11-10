using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Controllers;

/// <summary>The tournament leader accounts. Only the system administrator manages them.</summary>
[ApiController]
[Route("api/leaders")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class LeadersController(ChessFixerDbContext db, IPasswordHasher<AppUser> hasher) : ControllerBase
{
    [HttpGet]
    public async Task<List<LeaderDto>> GetAll() =>
        await db.Users
            .Where(u => u.Role == UserRole.TournamentLeader)
            .OrderBy(u => u.DisplayName)
            .Select(u => new LeaderDto(u.Id, u.UserName, u.DisplayName))
            .ToListAsync();

    [HttpPost]
    public async Task<LeaderDto> Create(CreateLeaderRequest request)
    {
        var leader = new AppUser
        {
            UserName = await ValidateUniqueUserNameAsync(request.UserName, exceptId: null),
            DisplayName = UserRules.ValidateDisplayName(request.DisplayName),
            Role = UserRole.TournamentLeader
        };
        leader.PasswordHash = hasher.HashPassword(leader, UserRules.ValidatePassword(request.Password));
        db.Users.Add(leader);
        await db.SaveChangesAsync();
        return ToDto(leader);
    }

    /// <summary>A new password logs the leader out everywhere.</summary>
    [HttpPut("{id:int}")]
    public async Task<LeaderDto> Update(int id, UpdateLeaderRequest request)
    {
        var leader = await FindAsync(id);
        leader.UserName = await ValidateUniqueUserNameAsync(request.UserName, exceptId: id);
        leader.DisplayName = UserRules.ValidateDisplayName(request.DisplayName);
        if (!string.IsNullOrEmpty(request.Password))
        {
            leader.PasswordHash = hasher.HashPassword(leader, UserRules.ValidatePassword(request.Password));
            leader.SecurityStamp = AppUser.NewSecurityStamp();
        }
        await db.SaveChangesAsync();
        return ToDto(leader);
    }

    /// <summary>Deletes the account; the leader is logged out right away. Tournaments are not linked to leaders.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        db.Users.Remove(await FindAsync(id));
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Only tournament leaders: the administrator account cannot be changed through this API.</summary>
    private async Task<AppUser> FindAsync(int id) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Role == UserRole.TournamentLeader)
        ?? throw new ApiException(ApiErrorCode.UserNotFound);

    private async Task<string> ValidateUniqueUserNameAsync(string userName, int? exceptId)
    {
        var name = UserRules.ValidateUserName(userName);
        // The column has NOCASE collation, so this comparison ignores case.
        if (await db.Users.AnyAsync(u => u.UserName == name && u.Id != exceptId))
            throw new ApiException(ApiErrorCode.UserNameTaken);
        return name;
    }

    private static LeaderDto ToDto(AppUser u) => new(u.Id, u.UserName, u.DisplayName);
}
