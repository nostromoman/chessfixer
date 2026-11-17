using ChessFixer.Server.Services;
using ChessFixer.Server.Services.Analysis;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChessFixer.Server.Controllers;

/// <summary>
/// The phone page behind the QR code of the 'Analyse aanmaken' wizard: no login, the random token in the link is the
/// key. The phone does the rest for that one game: it sends a photo of the scoresheet, has Claude read it, checks the
/// moves and saves them. The token works for a few hours after it was opened, until the moves are saved.
/// </summary>
[ApiController]
[Route("api/scoresheet-upload/{token}")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicy)]
public class ScoresheetUploadController(ScoresheetService scoresheets, TournamentService tournaments) : ControllerBase
{
    public const string RateLimitPolicy = "scoresheet-upload";

    /// <summary>The page was opened on the phone (the computer closes its wizard): the game, and where the phone got to.</summary>
    [HttpPost("open")]
    public Task<ScoresheetUploadInfoDto> Open(string token) => scoresheets.OpenLinkAsync(token);

    /// <summary>A photo (body: the image, max 5 MB); the phone then has it read.</summary>
    [HttpPost]
    [RequestSizeLimit(ScoresheetService.MaxImageBytes + 1024)]
    public async Task<ScoresheetPhotoDto> Upload(string token)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        return await scoresheets.UploadViaLinkAsync(token, buffer.ToArray(), Request.ContentType);
    }

    /// <summary>The image of the last photo the phone sent.</summary>
    [HttpGet("image")]
    [NoActivityLog]
    public async Task<IActionResult> Image(string token)
    {
        if (await scoresheets.GetImageViaLinkAsync(token) is not { } image) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return File(image.Data, image.ContentType);
    }

    /// <summary>Claude reads the last photo the phone sent (takes a minute or two).</summary>
    [HttpPost("read")]
    public async Task<ScoresheetWizardDto> Read(string token)
    {
        var wizard = await scoresheets.ReadViaLinkAsync(token);
        if (wizard.ReadStatus == ScoresheetReadStatus.Failed)
            HttpContext.Items[ActivityLog.ProblemItemsKey] = $"Lezen mislukt: {wizard.ReadError}";
        return wizard;
    }

    /// <summary>The reading of the last photo (polled while Claude reads); 204 when there is none.</summary>
    [HttpGet("wizard")]
    [NoActivityLog]
    public async Task<ActionResult<ScoresheetWizardDto>> Wizard(string token) =>
        await scoresheets.GetWizardViaLinkAsync(token) is { } wizard ? wizard : NoContent();

    /// <summary>Saves the moves being checked (while the user works, so not in the activity log).</summary>
    [HttpPut("review")]
    [NoActivityLog]
    public async Task<IActionResult> SaveReview(string token, ScoresheetReviewDto review)
    {
        await scoresheets.SaveReviewViaLinkAsync(token, review);
        return NoContent();
    }

    /// <summary>Saves the checked moves as the game's PGN (the analysis starts); the link can no longer be used.</summary>
    [HttpPost("moves")]
    public async Task<IActionResult> SaveMoves(string token, ScoresheetPgnRequest request)
    {
        var (tournamentId, gameId, pgn) = await scoresheets.BuildPgnViaLinkAsync(token, request);
        await tournaments.SetPgnAsync(tournamentId, gameId, pgn);
        await scoresheets.FinishLinkAsync(token);
        return NoContent();
    }
}
