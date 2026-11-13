namespace ChessFixer.Core.Pairing;

/// <summary>A pairing system: <see cref="SwissPairing"/> or <see cref="CompetitionPairing"/>.</summary>
public interface IPairingStrategy
{
    /// <summary>Pairs the next round. Throws <see cref="PairingException"/> when that is not possible.</summary>
    IReadOnlyList<BoardPairing> PairNextRound(PairingInput input);
}
