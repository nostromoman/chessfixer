using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Client.Services;

/// <summary>The API refused a request. <see cref="Code"/> is null when the server gave no error code.</summary>
public sealed class ApiClientException(ApiErrorCode? code, string message) : Exception(message)
{
    public ApiErrorCode? Code { get; } = code;
}

/// <summary>Typed wrapper around the ChessFixer Web API.</summary>
public class TournamentApi(HttpClient http)
{
    // ----- Tournaments -----

    public Task<List<TournamentSummaryDto>> GetTournamentsAsync() =>
        SendAsync<List<TournamentSummaryDto>>(HttpMethod.Get, "api/tournaments");

    public Task<TournamentDto> GetTournamentAsync(int id) =>
        SendAsync<TournamentDto>(HttpMethod.Get, $"api/tournaments/{id}");

    public Task<TournamentDto> CreateTournamentAsync(string title) =>
        SendAsync<TournamentDto>(HttpMethod.Post, "api/tournaments", new CreateTournamentRequest(title));

    public Task<TournamentDto> UpdateTournamentAsync(int id, UpdateTournamentRequest request) =>
        SendAsync<TournamentDto>(HttpMethod.Put, $"api/tournaments/{id}", request);

    public Task<TournamentDto> StartNextRoundAsync(int id) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/rounds/start");

    public Task<TournamentDto> CloseAsync(int id) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/close");

    // ----- Players in a tournament -----

    public Task<List<TournamentPlayerDto>> GetTournamentPlayersAsync(int id) =>
        SendAsync<List<TournamentPlayerDto>>(HttpMethod.Get, $"api/tournaments/{id}/players");

    public Task<TournamentDto> AddPlayerAsync(int id, int playerId) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/players", new AddTournamentPlayerRequest(playerId));

    public Task<TournamentDto> RemovePlayerAsync(int id, int playerId) =>
        SendAsync<TournamentDto>(HttpMethod.Delete, $"api/tournaments/{id}/players/{playerId}");

    // ----- Rounds, results, standings -----

    public Task<List<RoundDto>> GetRoundsAsync(int id) =>
        SendAsync<List<RoundDto>>(HttpMethod.Get, $"api/tournaments/{id}/rounds");

    public Task<TournamentDto> SetResultAsync(int id, int gameId, GameResult result) =>
        SendAsync<TournamentDto>(HttpMethod.Put, $"api/tournaments/{id}/games/{gameId}/result", new SetResultRequest(result));

    public Task<List<StandingDto>> GetStandingsAsync(int id) =>
        SendAsync<List<StandingDto>>(HttpMethod.Get, $"api/tournaments/{id}/standings");

    // ----- Central player list -----

    public Task<List<PlayerDto>> GetPlayersAsync() =>
        SendAsync<List<PlayerDto>>(HttpMethod.Get, "api/players");

    public Task<PlayerDto> CreatePlayerAsync(CreatePlayerRequest request) =>
        SendAsync<PlayerDto>(HttpMethod.Post, "api/players", request);

    public Task<PlayerDto> UpdatePlayerAsync(int id, UpdatePlayerRequest request) =>
        SendAsync<PlayerDto>(HttpMethod.Put, $"api/players/{id}", request);

    public Task DeletePlayerAsync(int id) =>
        SendCoreAsync(HttpMethod.Delete, $"api/players/{id}");

    public Task<PlayerDto> SetPlayerPhotoAsync(int id, byte[] image, string contentType)
    {
        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new(contentType);
        return SendAsync<PlayerDto>(HttpMethod.Put, $"api/players/{id}/photo", content);
    }

    public Task<PlayerDto> DeletePlayerPhotoAsync(int id) =>
        SendAsync<PlayerDto>(HttpMethod.Delete, $"api/players/{id}/photo");

    // ----- Plumbing -----

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, body);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>Sends the request and throws <see cref="ApiClientException"/> when it fails.
    /// The body is sent as JSON, unless it is already <see cref="HttpContent"/>.</summary>
    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, body.GetType())
        };

        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiError>(); }
            catch { /* no ApiError body */ }
            response.Dispose();
            throw new ApiClientException(error?.Code, $"{method} {url} failed with {(int)response.StatusCode}");
        }

        return response;
    }
}
