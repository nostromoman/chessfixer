using ChessFixer.Server.Services;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Server.Services.Reports;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

/// <summary>Application settings.</summary>
[ApiController]
[Route("api/settings")]
public class SettingsController(AiSettingsService ai, WritingStyleService writingStyles, TournamentDefaultsService tournamentDefaults)
    : ControllerBase
{
    /// <summary>The values a new tournament starts with.</summary>
    [HttpGet("tournament-defaults")]
    public Task<TournamentDefaultsDto> GetTournamentDefaults() => tournamentDefaults.GetAsync();

    [HttpPut("tournament-defaults")]
    public Task<TournamentDefaultsDto> UpdateTournamentDefaults(TournamentDefaultsDto request) => tournamentDefaults.UpdateAsync(request);

    /// <summary>The AI that writes the tournament reports. The password is never returned.</summary>
    [HttpGet("ai")]
    public Task<AiSettingsDto> GetAi() => ai.GetAsync();

    [HttpPut("ai")]
    public Task<AiSettingsDto> UpdateAi(UpdateAiSettingsRequest request) => ai.UpdateAsync(request);

    /// <summary>Tests the given settings without saving them; returns the available models.</summary>
    [HttpPost("ai/test")]
    public Task<AiConnectionTestResult> TestAi(UpdateAiSettingsRequest request) => ai.TestAsync(request);

    /// <summary>The writing styles a leader chooses from when asking for a report.</summary>
    [HttpGet("writing-styles")]
    public Task<List<WritingStyleDto>> GetWritingStyles() => writingStyles.GetAllAsync();

    [HttpPost("writing-styles")]
    public Task<WritingStyleDto> CreateWritingStyle(SaveWritingStyleRequest request) => writingStyles.CreateAsync(request);

    [HttpPut("writing-styles/{id:int}")]
    public Task<WritingStyleDto> UpdateWritingStyle(int id, SaveWritingStyleRequest request) => writingStyles.UpdateAsync(id, request);

    [HttpDelete("writing-styles/{id:int}")]
    public async Task<IActionResult> DeleteWritingStyle(int id)
    {
        await writingStyles.DeleteAsync(id);
        return NoContent();
    }
}
