using System.Security.Claims;
using ChessFixer.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Auth;

/// <summary>The login cookie: which claims it carries and how it is checked on every request.</summary>
public static class UserSession
{
    private const string SecurityStampClaim = "chessfixer:stamp";

    public static ClaimsPrincipal CreatePrincipal(AppUser user) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(SecurityStampClaim, user.SecurityStamp)
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static int? GetUserId(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>
    /// Rejects the cookie when the account was deleted or its security stamp changed (new password, role change),
    /// so such changes take effect right away instead of when the cookie expires.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var userId = context.Principal is { } principal ? GetUserId(principal) : null;
        var stamp = context.Principal?.FindFirstValue(SecurityStampClaim);

        var db = context.HttpContext.RequestServices.GetRequiredService<ChessFixerDbContext>();
        var valid = userId is not null && stamp is not null
            && await db.Users.AnyAsync(u => u.Id == userId && u.SecurityStamp == stamp);

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
