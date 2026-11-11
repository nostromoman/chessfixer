using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Controllers;

/// <summary>
/// Logging in and out with a cookie. Only tournament leaders and the system administrator log in.
/// Logins (also failed ones), logouts and password changes are written to the activity log.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(ChessFixerDbContext db, IPasswordHasher<AppUser> hasher, ActivityLog activity) : ControllerBase
{
    public const string LoginRateLimitPolicy = "login";

    private static readonly AppUser DummyUser = new() { UserName = "", DisplayName = "" };
    private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(DummyUser, Guid.NewGuid().ToString());

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    public async Task<CurrentUserDto> Login(LoginRequest request)
    {
        var userName = request.UserName?.Trim() ?? "";
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserName == userName);

        // Hash also when the user does not exist, so the response time does not reveal which login names exist.
        var result = hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, request.Password ?? "");
        if (user is null || result == PasswordVerificationResult.Failed)
        {
            await activity.LogAsync(HttpContext, ActivityKind.LoginFailed, user, userName);
            throw new ApiException(ApiErrorCode.InvalidCredentials);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            await db.SaveChangesAsync();
        }

        await SignInAsync(user);
        await activity.LogAsync(HttpContext, ActivityKind.Login, user);
        return ToDto(user);
    }

    /// <summary>Also allowed without a (valid) session, so the client can always clear its cookie.</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (UserSession.GetUserId(User) is { } userId && await db.Users.FindAsync(userId) is { } user)
            await activity.LogAsync(HttpContext, ActivityKind.Logout, user);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>The logged-in user; 401 when not logged in.</summary>
    [HttpGet("me")]
    public async Task<CurrentUserDto> Me() => ToDto(await GetCurrentUserAsync());

    /// <summary>Changes the password of the logged-in user. Other sessions of the user are logged out.</summary>
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
            throw new ApiException(ApiErrorCode.CurrentPasswordIncorrect);

        user.PasswordHash = hasher.HashPassword(user, UserRules.ValidatePassword(request.NewPassword));
        user.SecurityStamp = AppUser.NewSecurityStamp();
        await db.SaveChangesAsync();

        await SignInAsync(user); // a new cookie with the new security stamp keeps this session logged in
        await activity.LogAsync(HttpContext, ActivityKind.PasswordChanged, user);
        return NoContent();
    }

    private async Task<AppUser> GetCurrentUserAsync() =>
        await db.Users.FindAsync(UserSession.GetUserId(User) ?? 0) ?? throw new ApiException(ApiErrorCode.UserNotFound);

    private Task SignInAsync(AppUser user) =>
        HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserSession.CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = true });

    private static CurrentUserDto ToDto(AppUser user) => new(user.Id, user.UserName, user.DisplayName, user.Role);
}
