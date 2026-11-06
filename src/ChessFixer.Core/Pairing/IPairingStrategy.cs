using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Pairing;

public interface IPairingStrategy
{
    /// <summary>Pairs the next round. Throws <see cref="PairingException"/> when that is not possible.</summary>
    IReadOnlyList<BoardPairing> PairNextRound(PairingInput input);
}

public static class PairingStrategies
{
    public static IPairingStrategy For(PairingSystem system) => system switch
    {
        PairingSystem.Swiss => new SwissPairing(),
        PairingSystem.RoundRobin => new RoundRobinPairing(),
        _ => throw new ArgumentOutOfRangeException(nameof(system), system, null)
    };
}
