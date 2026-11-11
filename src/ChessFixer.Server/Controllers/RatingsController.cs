using ChessFixer.Server.Services.Ratings;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

/// <summary>The rating list of the own players and the rating log.</summary>
[ApiController]
[Route("api/ratings")]
public class RatingsController(RatingLogService ratings) : ControllerBase
{
    /// <summary>The own players (no guests) with their rating and trend, highest rating first.</summary>
    [HttpGet]
    public Task<List<RatingListEntryDto>> GetList() => ratings.GetRatingListAsync();

    /// <summary>Every rating change, newest first, a page at a time.</summary>
    [HttpGet("log")]
    public Task<RatingLogPageDto> GetLog(int skip = 0, int take = 50, int? playerId = null) =>
        ratings.GetPageAsync(skip, take, playerId);
}
