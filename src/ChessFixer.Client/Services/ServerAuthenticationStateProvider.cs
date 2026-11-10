using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Components.Authorization;

namespace ChessFixer.Client.Services;

/// <summary>
/// Who is logged in, according to the server. The login itself is an HttpOnly cookie that the browser sends
/// with every request; this class only asks the server whose cookie it is (<c>api/auth/me</c>).
/// </summary>
public sealed class ServerAuthenticationStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private Task<AuthenticationState>? _state;

    /// <summary>The logged-in user, once known; null when not logged in.</summary>
    public CurrentUserDto? CurrentUser { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    /// <returns>Null when logged in; otherwise the reason (e.g. <see cref="ApiErrorCode.InvalidCredentials"/>).</returns>
    public async Task<ApiErrorCode?> LoginAsync(string userName, string password)
    {
        using var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(userName, password));
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return ApiErrorCode.TooManyLoginAttempts;
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiError>(); }
            catch { /* no ApiError body */ }
            return error?.Code ?? ApiErrorCode.InvalidCredentials;
        }

        SetUser((await response.Content.ReadFromJsonAsync<CurrentUserDto>())!);
        return null;
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var _ = await http.PostAsync("api/auth/logout", null);
        }
        finally
        {
            SetUser(null);
        }
    }

    /// <summary>The server refused a request because the session ended (expired, password changed, account deleted).</summary>
    public void OnSessionEnded()
    {
        if (CurrentUser is not null) SetUser(null);
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            using var response = await http.GetAsync("api/auth/me");
            CurrentUser = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CurrentUserDto>() : null;
        }
        catch (HttpRequestException)
        {
            CurrentUser = null; // server unreachable: show the login page
        }
        return ToState(CurrentUser);
    }

    private void SetUser(CurrentUserDto? user)
    {
        CurrentUser = user;
        _state = Task.FromResult(ToState(user));
        NotifyAuthenticationStateChanged(_state);
    }

    private static AuthenticationState ToState(CurrentUserDto? user) =>
        user is null
            ? Anonymous
            : new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            ], authenticationType: "ChessFixer")));
}
