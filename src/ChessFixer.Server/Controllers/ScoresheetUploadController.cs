using ChessFixer.Server.Services.Analysis;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChessFixer.Server.Controllers;

/// <summary>
/// The phone page behind the QR code of the scoresheet wizard: no login, the random token in the link is the key.
/// It can only show which game the photo is for and send one photo, within 20 minutes.
/// </summary>
[ApiController]
[Route("api/scoresheet-upload/{token}")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicy)]
public class ScoresheetUploadController(ScoresheetService scoresheets) : ControllerBase
{
    public const string RateLimitPolicy = "scoresheet-upload";

    /// <summary>The page was opened on the phone (shown in the wizard): which game the photo is for.</summary>
    [HttpPost("open")]
    public Task<ScoresheetUploadInfoDto> Open(string token) => scoresheets.OpenLinkAsync(token);

    /// <summary>The photo (body: the image, max 5 MB). The link can no longer be used afterwards.</summary>
    [HttpPost]
    [RequestSizeLimit(ScoresheetService.MaxImageBytes + 1024)]
    public async Task<IActionResult> Upload(string token)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        await scoresheets.UploadViaLinkAsync(token, buffer.ToArray(), Request.ContentType);
        return NoContent();
    }
}
