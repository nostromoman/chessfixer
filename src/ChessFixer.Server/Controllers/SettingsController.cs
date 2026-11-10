using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

/// <summary>Application settings.</summary>
[ApiController]
[Route("api/settings")]
public class SettingsController(AiSettingsService ai) : ControllerBase
{
    /// <summary>The AI that writes the tournament reports. The password is never returned.</summary>
    [HttpGet("ai")]
    public Task<AiSettingsDto> GetAi() => ai.GetAsync();

    [HttpPut("ai")]
    public Task<AiSettingsDto> UpdateAi(UpdateAiSettingsRequest request) => ai.UpdateAsync(request);

    /// <summary>Tests the given settings without saving them; returns the available models.</summary>
    [HttpPost("ai/test")]
    public Task<AiConnectionTestResult> TestAi(UpdateAiSettingsRequest request) => ai.TestAsync(request);
}
