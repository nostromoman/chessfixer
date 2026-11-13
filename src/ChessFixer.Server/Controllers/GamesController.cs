using ChessFixer.Server.Services.Analysis;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

/// <summary>Games with moves (PGN) and their Stockfish analysis. The moves are set via the tournament
/// (PUT api/tournaments/{id}/games/{gameId}/pgn).</summary>
[ApiController]
[Route("api/games")]
public class GamesController(GameAnalysisService service) : ControllerBase
{
    /// <summary>All games with moves, newest first.</summary>
    [HttpGet]
    public Task<List<AnalysedGameSummaryDto>> GetAll() => service.GetAllAsync();

    /// <summary>The moves of a game with the analysis per move.</summary>
    [HttpGet("{gameId:int}")]
    public Task<GameAnalysisDto> Get(int gameId) => service.GetAsync(gameId);

    /// <summary>Analyses the game again (e.g. after a failed analysis).</summary>
    [HttpPost("{gameId:int}/analyse")]
    public async Task<GameAnalysisDto> Analyse(int gameId)
    {
        await service.ResetAsync(gameId);
        return await service.GetAsync(gameId);
    }
}
