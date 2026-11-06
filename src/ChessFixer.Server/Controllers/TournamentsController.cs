using ChessFixer.Server.Services;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

[ApiController]
[Route("api/tournaments")]
public class TournamentsController(TournamentService service) : ControllerBase
{
    [HttpGet]
    public Task<List<TournamentSummaryDto>> GetAll() => service.GetAllAsync();

    [HttpGet("{id:int}")]
    public Task<TournamentDto> Get(int id) => service.GetAsync(id);

    [HttpPost]
    public async Task<ActionResult<TournamentDto>> Create(CreateTournamentRequest request)
    {
        var tournament = await service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = tournament.Id }, tournament);
    }

    /// <summary>Changes the title and settings. Settings (not the title) are locked once round 1 has started.</summary>
    [HttpPut("{id:int}")]
    public Task<TournamentDto> Update(int id, UpdateTournamentRequest request) => service.UpdateAsync(id, request);

    /// <summary>Starts the next round (which is already paired while waiting).</summary>
    [HttpPost("{id:int}/rounds/start")]
    public Task<TournamentDto> StartNextRound(int id) => service.StartNextRoundAsync(id);

    [HttpPost("{id:int}/close")]
    public Task<TournamentDto> Close(int id) => service.CloseAsync(id);

    // ----- Players in this tournament -----

    [HttpGet("{id:int}/players")]
    public Task<List<TournamentPlayerDto>> GetPlayers(int id) => service.GetPlayersAsync(id);

    [HttpPost("{id:int}/players")]
    public Task<TournamentDto> AddPlayer(int id, AddTournamentPlayerRequest request) =>
        service.AddPlayerAsync(id, request.PlayerId);

    [HttpDelete("{id:int}/players/{playerId:int}")]
    public Task<TournamentDto> RemovePlayer(int id, int playerId) => service.RemovePlayerAsync(id, playerId);

    // ----- Rounds, results, standings -----

    /// <summary>All rounds; the last one is the pending round when IsStarted is false.</summary>
    [HttpGet("{id:int}/rounds")]
    public Task<List<RoundDto>> GetRounds(int id) => service.GetRoundsAsync(id);

    [HttpPut("{id:int}/games/{gameId:int}/result")]
    public Task<TournamentDto> SetResult(int id, int gameId, SetResultRequest request) =>
        service.SetResultAsync(id, gameId, request.Result);

    [HttpGet("{id:int}/standings")]
    public Task<List<StandingDto>> GetStandings(int id) => service.GetStandingsAsync(id);
}
