using ChessFixer.Core.Rating;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services.Ratings;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
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

    /// <summary>The rules and values of the rating calculation.</summary>
    [HttpGet("rules")]
    public RatingRulesDto GetRules()
    {
        var table = EloCalculator.ExpectedScoreTable;
        var ranges = table
            .Select((max, i) => new ExpectedScoreRangeDto(i == 0 ? 0 : table[i - 1] + 1, max, (50 + i) / 100m))
            .Append(new ExpectedScoreRangeDto(table[^1] + 1, null, 1.00m))
            .ToList();
        return new RatingRulesDto(
            EloCalculator.DefaultRating, EloCalculator.DefaultK, EloCalculator.JuniorK, EloCalculator.JuniorMaxRating,
            EloCalculator.JuniorAge, EloCalculator.MasterK, EloCalculator.MasterRating,
            EloCalculator.MaxDifference, EloCalculator.NoDifferenceCapFrom, ranges);
    }

    /// <summary>Works out the rating change of a game step by step (the calculator in "meer info").</summary>
    [HttpGet("calculate")]
    public ActionResult<RatingCalculationDto> Calculate(
        int whiteRating, int blackRating, GameResult result = GameResult.WhiteWins,
        bool whiteJunior = false, bool blackJunior = false, bool whiteWasMaster = false, bool blackWasMaster = false)
    {
        if (!EloCalculator.IsRated(result) || whiteRating is < 0 or > 4000 || blackRating is < 0 or > 4000)
            throw new ApiException(ApiErrorCode.InvalidRating);

        var white = new RatedPlayer(whiteRating, whiteWasMaster, whiteJunior);
        var black = new RatedPlayer(blackRating, blackWasMaster, blackJunior);
        var whiteScore = result switch { GameResult.WhiteWins => 1m, GameResult.Draw => 0.5m, _ => 0m };
        return new RatingCalculationDto(
            ToDto(EloCalculator.Explain(white, black, whiteScore)),
            ToDto(EloCalculator.Explain(black, white, 1 - whiteScore)));
    }

    private static RatingCalculationStepDto ToDto(RatingCalculation c) =>
        new(c.Rating, c.OpponentRating, c.Difference, c.UsedDifference, c.ExpectedScore, c.Score,
            c.K, c.KReason, c.ExactChange, c.Change);

    /// <summary>Every rating change, newest first, a page at a time.</summary>
    [HttpGet("log")]
    public Task<RatingLogPageDto> GetLog(int skip = 0, int take = 50, int? playerId = null) =>
        ratings.GetPageAsync(skip, take, playerId);
}
