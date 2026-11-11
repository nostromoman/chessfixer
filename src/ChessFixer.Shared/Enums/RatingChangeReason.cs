using System.Text.Json.Serialization;

namespace ChessFixer.Shared.Enums;

/// <summary>Why a rating changed (rating log).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RatingChangeReason>))]
public enum RatingChangeReason
{
    /// <summary>A new player got their starting rating.</summary>
    NewPlayer,
    /// <summary>The rating was set by hand on the player.</summary>
    Manual,
    /// <summary>The result of a rated game.</summary>
    Game,
    /// <summary>The change of a game was taken back because its result was corrected or cleared.</summary>
    GameReverted,
    /// <summary>The change of a game was taken back because its round was cancelled.</summary>
    RoundCancelled
}
