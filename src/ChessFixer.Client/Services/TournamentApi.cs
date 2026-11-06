using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;

namespace ChessFixer.Client.Services;

/// <summary>Typed wrapper around the ChessFixer Web API.</summary>
public class TournamentApi(HttpClient http)
{
    public async Task<List<TournamentSummaryDto>> GetTournamentsAsync() =>
        await http.GetFromJsonAsync<List<TournamentSummaryDto>>("api/tournaments") ?? [];

    public async Task<TournamentDto?> GetTournamentAsync(int id)
    {
        var response = await http.GetAsync($"api/tournaments/{id}");
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TournamentDto>() : null;
    }

    public async Task<TournamentDto> CreateTournamentAsync(string title)
    {
        var response = await http.PostAsJsonAsync("api/tournaments", new CreateTournamentRequest(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TournamentDto>())!;
    }

    public async Task<List<PlayerDto>> GetPlayersAsync() =>
        await http.GetFromJsonAsync<List<PlayerDto>>("api/players") ?? [];
}
