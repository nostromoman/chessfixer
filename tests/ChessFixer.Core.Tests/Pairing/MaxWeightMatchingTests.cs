using ChessFixer.Core.Pairing;

namespace ChessFixer.Core.Tests.Pairing;

public class MaxWeightMatchingTests
{
    private static MaxWeightMatching.Edge E(int u, int v, long w) => new(u, v, w);

    [Fact]
    public void Empty() => Assert.Equal([-1, -1], MaxWeightMatching.Find(2, []));

    [Fact]
    public void SingleEdge() => Assert.Equal([1, 0], MaxWeightMatching.Find(2, [E(0, 1, 1)]));

    [Fact]
    public void PrefersHeavierEdge() =>
        Assert.Equal([-1, 2, 1], MaxWeightMatching.Find(3, [E(0, 1, 10), E(1, 2, 11)]));

    [Fact]
    public void TwoLightEdgesBeatOneHeavy() =>
        Assert.Equal([1, 0, 3, 2], MaxWeightMatching.Find(4, [E(0, 1, 5), E(1, 2, 11), E(2, 3, 5)]));

    [Fact]
    public void MaxCardinality_TakesMoreEdgesEvenIfLighter() =>
        Assert.Equal([1, 0, 3, 2], MaxWeightMatching.Find(4, [E(0, 1, 2), E(1, 2, 11), E(2, 3, 2)], maxCardinality: true));

    [Fact]
    public void Blossom_Augmenting() =>
        // A blossom (0,1,2) with a stem that needs augmenting through it.
        Assert.Equal([1, 0, 3, 2], MaxWeightMatching.Find(4, [E(0, 1, 8), E(0, 2, 9), E(1, 2, 10), E(2, 3, 7)]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomGraphs_MatchBruteForce(bool maxCardinality)
    {
        var random = new Random(12345);
        for (var test = 0; test < 3000; test++)
        {
            var n = random.Next(1, 11);
            var edges = new List<MaxWeightMatching.Edge>();
            var density = random.NextDouble();
            var maxWeight = random.Next(1, 4) switch { 1 => 3, 2 => 20, _ => 1_000_000_000L };
            for (var u = 0; u < n; u++)
            for (var v = u + 1; v < n; v++)
            {
                if (random.NextDouble() < density)
                    edges.Add(E(u, v, random.NextInt64(maxCardinality ? -maxWeight : 1, maxWeight + 1)));
            }

            var mate = MaxWeightMatching.Find(n, edges, maxCardinality);

            // A valid matching over existing edges.
            for (var v = 0; v < n; v++)
            {
                if (mate[v] < 0) continue;
                Assert.Equal(v, mate[mate[v]]);
                Assert.Contains(edges, e => (e.U == v && e.V == mate[v]) || (e.V == v && e.U == mate[v]));
            }

            var (bestCount, bestWeight) = BruteForce(n, edges, maxCardinality);
            var count = mate.Count(m => m >= 0) / 2;
            if (maxCardinality) Assert.Equal(bestCount, count);
            Assert.Equal(bestWeight, Weight(mate, edges));
        }
    }

    private static long Weight(int[] mate, IReadOnlyList<MaxWeightMatching.Edge> edges) =>
        edges.Where(e => mate[e.U] == e.V).Sum(e => e.Weight);

    /// <summary>The best (count, weight) over all matchings: count first when maxCardinality.</summary>
    private static (int Count, long Weight) BruteForce(int n, List<MaxWeightMatching.Edge> edges, bool maxCardinality)
    {
        var weight = new long?[n, n];
        foreach (var e in edges) weight[e.U, e.V] = weight[e.V, e.U] = e.Weight;

        var best = (Count: 0, Weight: 0L);
        void Search(ulong done, int count, long total)
        {
            var v = 0;
            while (v < n && (done & (1UL << v)) != 0) v++;
            if (v == n)
            {
                var better = maxCardinality
                    ? count > best.Count || (count == best.Count && total > best.Weight)
                    : total > best.Weight;
                if (better) best = (count, total);
                return;
            }
            Search(done | (1UL << v), count, total); // v stays unmatched
            for (var w = v + 1; w < n; w++)
            {
                if ((done & (1UL << w)) == 0 && weight[v, w] is { } ew)
                    Search(done | (1UL << v) | (1UL << w), count + 1, total + ew);
            }
        }
        Search(0, 0, 0);
        return best;
    }
}
