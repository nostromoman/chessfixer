using ChessFixer.Server.Services.Analysis;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

/// <summary>Games with moves (PGN) and their Stockfish analysis. The moves are set via the tournament
/// (PUT api/tournaments/{id}/games/{gameId}/pgn).</summary>
[ApiController]
[Route("api/games")]
public class GamesController(GameAnalysisService service, ScoresheetService scoresheets) : ControllerBase
{
    // ----- Photos of the handwritten scoresheet, kept for the records -----

    [HttpGet("{gameId:int}/scoresheets")]
    public Task<List<ScoresheetPhotoDto>> GetScoresheets(int gameId) => scoresheets.GetPhotosAsync(gameId);

    [HttpGet("{gameId:int}/scoresheets/{photoId:int}/image")]
    public async Task<IActionResult> GetScoresheetImage(int gameId, int photoId)
    {
        if (await scoresheets.GetImageAsync(gameId, photoId) is not { } image) return NotFound();
        // A photo never changes (a new photo gets a new id), so the browser may keep it.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(image.Data, image.ContentType);
    }

    /// <summary>Keeps a photo of the scoresheet without reading it (body: the image, max 5 MB).</summary>
    [HttpPost("{gameId:int}/scoresheets")]
    [RequestSizeLimit(ScoresheetService.MaxImageBytes + 1024)]
    public async Task<ScoresheetPhotoDto> UploadScoresheet(int gameId)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        return await scoresheets.UploadAsync(gameId, buffer.ToArray(), Request.ContentType, User);
    }

    [HttpDelete("{gameId:int}/scoresheets/{photoId:int}")]
    public async Task<IActionResult> DeleteScoresheet(int gameId, int photoId)
    {
        await scoresheets.DeleteAsync(gameId, photoId);
        return NoContent();
    }

    // ----- Games with moves and their analysis -----

    /// <summary>All games with moves, newest first.</summary>
    [HttpGet]
    public Task<List<AnalysedGameSummaryDto>> GetAll() => service.GetAllAsync();

    /// <summary>All games of a player against an opponent, newest first, with the analysis where there are moves.</summary>
    [HttpGet("~/api/players/{playerId:int}/games")]
    public Task<List<PlayerAnalysedGameDto>> GetPlayerGames(int playerId) => service.GetPlayerGamesAsync(playerId);

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
