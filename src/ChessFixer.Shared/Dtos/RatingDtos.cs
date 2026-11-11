using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <summary>One own player (not a guest) on the rating page.</summary>
/// <param name="RatedGames">Number of rated games.</param>
/// <param name="Trend">The ratings after the latest changes, oldest first, ending with the current rating.</param>
/// <param name="Change30Days">Rating change over the last 30 days.</param>
public record RatingListEntryDto(
    int PlayerId, string Name, string FirstName, string LastName, int? PhotoVersion,
    int Rating, int RatedGames, IReadOnlyList<int> Trend, int Change30Days)
{
    public string? PhotoUrl => PhotoVersion is { } version ? $"api/players/{PlayerId}/photo?v={version}" : null;
}

/// <summary>One change in the rating log.</summary>
/// <param name="Tournament">For changes of a game: the tournament (title as it was then) and round.</param>
/// <param name="ChangedBy">Display name of the user who made the change; null when unknown.</param>
public record RatingLogEntryDto(
    int Id,
    DateTime At,
    GamePlayerDto Player,
    int RatingBefore,
    int RatingAfter,
    RatingChangeReason Reason,
    int? TournamentId,
    string? TournamentTitle,
    int? Round,
    GamePlayerDto? Opponent,
    int? OpponentRating,
    GameResult? Result,
    bool? IsWhite,
    string? ChangedBy)
{
    public int Change => RatingAfter - RatingBefore;
}

/// <summary>A page of the rating log, newest first.</summary>
public record RatingLogPageDto(int Total, IReadOnlyList<RatingLogEntryDto> Entries);

/// <summary>The rules and values the rating calculation uses (rating page, "meer info").</summary>
/// <param name="ExpectedScoreTable">FIDE table: the expected score of the higher rated player per range of rating
/// difference, from 0.50 up.</param>
public record RatingRulesDto(
    int DefaultRating,
    int DefaultK,
    int JuniorK,
    int JuniorMaxRating,
    int JuniorAge,
    int MasterK,
    int MasterRating,
    int MaxDifference,
    int NoDifferenceCapFrom,
    IReadOnlyList<ExpectedScoreRangeDto> ExpectedScoreTable);

/// <param name="ExpectedScore">Of the higher rated player; the lower rated player gets 1 minus this.</param>
public record ExpectedScoreRangeDto(int MinDifference, int? MaxDifference, decimal ExpectedScore);

/// <summary>The rating change of one game, worked out step by step for both players.</summary>
public record RatingCalculationDto(RatingCalculationStepDto White, RatingCalculationStepDto Black);

/// <param name="UsedDifference">The rating difference after the cap of 400.</param>
/// <param name="ExactChange">K × (score − expected score), before rounding.</param>
public record RatingCalculationStepDto(
    int Rating, int OpponentRating, int Difference, int UsedDifference, decimal ExpectedScore, decimal Score,
    int K, KFactorReason KReason, decimal ExactChange, int Change)
{
    public int NewRating => Rating + Change;
}
