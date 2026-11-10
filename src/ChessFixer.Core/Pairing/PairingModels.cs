using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Pairing;

/// <summary>A player that can be paired. Id is the tournament participant id (stable, in registration order).</summary>
public record PairingPlayer(int Id, int? Rating);

/// <summary>A game from an earlier round. BlackId is null for a bye.</summary>
public record PlayedGame(int Round, int WhiteId, int? BlackId, GameResult Result);

/// <summary>Everything needed to pair the next round.</summary>
/// <param name="Players">The active players (removed players are left out).</param>
/// <param name="History">All games of earlier rounds, including games of removed players.</param>
/// <param name="Scoring">Points per result, used for score groups.</param>
public record PairingInput(
    IReadOnlyList<PairingPlayer> Players,
    IReadOnlyList<PlayedGame> History,
    ScoringRules Scoring);

/// <summary>One board of the new round. BlackId is null when White gets a bye.</summary>
public record BoardPairing(int Board, int WhiteId, int? BlackId)
{
    public bool IsBye => BlackId is null;
}

public enum PairingError
{
    NotEnoughPlayers,
    TooManyPlayers,
    NoValidPairing
}

public sealed class PairingException(PairingError error) : Exception($"Pairing failed: {error}")
{
    public PairingError Error { get; } = error;
}
