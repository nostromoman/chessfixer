namespace ChessFixer.Core.Pairing;

/// <summary>
/// Maximum weight matching in a general graph (Edmonds' blossom algorithm with dual variables, O(n³)).
/// A C# port of Joris van Rantwijk's well-known reference implementation (mwmatching.py, public domain), which is
/// also the basis of NetworkX's <c>max_weight_matching</c>. With integer weights it only uses integer arithmetic.
/// </summary>
public static class MaxWeightMatching
{
    /// <summary>An undirected edge between vertices <paramref name="U"/> and <paramref name="V"/> (0-based).</summary>
    public readonly record struct Edge(int U, int V, long Weight);

    /// <summary>
    /// Finds a matching with the largest total weight. With <paramref name="maxCardinality"/>, only matchings with the
    /// largest possible number of edges are considered (and of those the heaviest).
    /// </summary>
    /// <returns>For every vertex its partner, or -1 when it is not matched.</returns>
    public static int[] Find(int vertexCount, IReadOnlyList<Edge> edges, bool maxCardinality = false)
    {
        var mate = Enumerable.Repeat(-1, vertexCount).ToArray();
        if (edges.Count == 0 || vertexCount == 0) return mate;
        foreach (var e in edges)
        {
            if (e.U == e.V || e.U < 0 || e.V < 0 || e.U >= vertexCount || e.V >= vertexCount)
                throw new ArgumentException("Invalid edge.", nameof(edges));
        }
        return new Solver(vertexCount, edges, maxCardinality).Run();
    }

    private sealed class Solver
    {
        private readonly int _n;
        private readonly int _edgeCount;
        private readonly int[] _edgeU, _edgeV;
        private readonly long[] _weight;
        private readonly bool _maxCardinality;

        // endpoint[p]: the vertex at endpoint p; edge k has endpoints 2k (U) and 2k+1 (V).
        private readonly int[] _endpoint;
        // neighbend[v]: the remote endpoints of the edges of v.
        private readonly List<int>[] _neighbend;

        // mate[v]: the remote endpoint of v's matched edge, or -1.
        private readonly int[] _mate;
        // Per top-level blossom / vertex: 0 = free, 1 = S, 2 = T (bit 4 marks a breadcrumb in scanBlossom).
        private readonly int[] _label;
        private readonly int[] _labelEnd;
        private readonly int[] _inBlossom;
        private readonly int[] _blossomParent;
        private readonly List<int>?[] _blossomChilds;
        private readonly int[] _blossomBase;
        private readonly List<int>?[] _blossomEndps;
        private readonly int[] _bestEdge;
        private readonly List<int>?[] _blossomBestEdges;
        private readonly Stack<int> _unusedBlossoms = new();
        private readonly long[] _dualVar;
        private readonly bool[] _allowEdge;
        private readonly List<int> _queue = [];

        public Solver(int vertexCount, IReadOnlyList<Edge> edges, bool maxCardinality)
        {
            _n = vertexCount;
            _edgeCount = edges.Count;
            _maxCardinality = maxCardinality;
            _edgeU = new int[_edgeCount];
            _edgeV = new int[_edgeCount];
            _weight = new long[_edgeCount];
            for (var k = 0; k < _edgeCount; k++)
                (_edgeU[k], _edgeV[k], _weight[k]) = (edges[k].U, edges[k].V, edges[k].Weight);

            var maxWeight = Math.Max(0, _weight.Max());

            _endpoint = new int[2 * _edgeCount];
            for (var p = 0; p < 2 * _edgeCount; p++) _endpoint[p] = p % 2 == 0 ? _edgeU[p / 2] : _edgeV[p / 2];

            _neighbend = new List<int>[_n];
            for (var v = 0; v < _n; v++) _neighbend[v] = [];
            for (var k = 0; k < _edgeCount; k++)
            {
                _neighbend[_edgeU[k]].Add(2 * k + 1);
                _neighbend[_edgeV[k]].Add(2 * k);
            }

            _mate = Enumerable.Repeat(-1, _n).ToArray();
            _label = new int[2 * _n];
            _labelEnd = Enumerable.Repeat(-1, 2 * _n).ToArray();
            _inBlossom = Enumerable.Range(0, _n).ToArray();
            _blossomParent = Enumerable.Repeat(-1, 2 * _n).ToArray();
            _blossomChilds = new List<int>?[2 * _n];
            _blossomBase = Enumerable.Range(0, _n).Concat(Enumerable.Repeat(-1, _n)).ToArray();
            _blossomEndps = new List<int>?[2 * _n];
            _bestEdge = Enumerable.Repeat(-1, 2 * _n).ToArray();
            _blossomBestEdges = new List<int>?[2 * _n];
            // Popped from the end in the reference implementation: push in increasing order.
            for (var b = _n; b < 2 * _n; b++) _unusedBlossoms.Push(b);
            _dualVar = Enumerable.Repeat(maxWeight, _n).Concat(Enumerable.Repeat(0L, _n)).ToArray();
            _allowEdge = new bool[_edgeCount];
        }

        private long Slack(int k) => _dualVar[_edgeU[k]] + _dualVar[_edgeV[k]] - 2 * _weight[k];

        private IEnumerable<int> BlossomLeaves(int b)
        {
            if (b < _n)
            {
                yield return b;
                yield break;
            }
            foreach (var t in _blossomChilds[b]!)
            {
                if (t < _n) yield return t;
                else
                    foreach (var v in BlossomLeaves(t)) yield return v;
            }
        }

        /// <summary>Labels vertex w (and its top-level blossom) S (1) or T (2), reached through endpoint p.</summary>
        private void AssignLabel(int w, int t, int p)
        {
            var b = _inBlossom[w];
            _label[w] = _label[b] = t;
            _labelEnd[w] = _labelEnd[b] = p;
            _bestEdge[w] = _bestEdge[b] = -1;
            if (t == 1)
                _queue.AddRange(BlossomLeaves(b));
            else if (t == 2)
            {
                var @base = _blossomBase[b];
                AssignLabel(_endpoint[_mate[@base]], 1, _mate[@base] ^ 1);
            }
        }

        /// <summary>Traces back from v and w to find a new blossom (returns its base) or an augmenting path (-1).</summary>
        private int ScanBlossom(int v, int w)
        {
            var path = new List<int>();
            var @base = -1;
            while (v != -1 || w != -1)
            {
                var b = _inBlossom[v];
                if ((_label[b] & 4) != 0)
                {
                    @base = _blossomBase[b];
                    break;
                }
                path.Add(b);
                _label[b] = 5;
                if (_labelEnd[b] == -1)
                    v = -1;
                else
                {
                    v = _endpoint[_labelEnd[b]];
                    b = _inBlossom[v];
                    v = _endpoint[_labelEnd[b]];
                }
                if (w != -1) (v, w) = (w, v);
            }
            foreach (var b in path) _label[b] = 1;
            return @base;
        }

        /// <summary>Makes a new blossom with the given base, through S-vertices connected by edge k.</summary>
        private void AddBlossom(int @base, int k)
        {
            int v = _edgeU[k], w = _edgeV[k];
            var bb = _inBlossom[@base];
            var bv = _inBlossom[v];
            var bw = _inBlossom[w];
            var b = _unusedBlossoms.Pop();
            _blossomBase[b] = @base;
            _blossomParent[b] = -1;
            _blossomParent[bb] = b;
            var path = new List<int>();
            var endps = new List<int>();
            _blossomChilds[b] = path;
            _blossomEndps[b] = endps;
            while (bv != bb)
            {
                _blossomParent[bv] = b;
                path.Add(bv);
                endps.Add(_labelEnd[bv]);
                v = _endpoint[_labelEnd[bv]];
                bv = _inBlossom[v];
            }
            path.Add(bb);
            path.Reverse();
            endps.Reverse();
            endps.Add(2 * k);
            while (bw != bb)
            {
                _blossomParent[bw] = b;
                path.Add(bw);
                endps.Add(_labelEnd[bw] ^ 1);
                w = _endpoint[_labelEnd[bw]];
                bw = _inBlossom[w];
            }
            _label[b] = 1;
            _labelEnd[b] = _labelEnd[bb];
            _dualVar[b] = 0;
            foreach (var leaf in BlossomLeaves(b).ToList())
            {
                if (_label[_inBlossom[leaf]] == 2) _queue.Add(leaf);
                _inBlossom[leaf] = b;
            }

            var bestEdgeTo = Enumerable.Repeat(-1, 2 * _n).ToArray();
            foreach (var child in path)
            {
                List<List<int>> neighbourLists;
                if (_blossomBestEdges[child] is null)
                    neighbourLists = BlossomLeaves(child).Select(leaf => _neighbend[leaf].Select(p => p / 2).ToList()).ToList();
                else
                    neighbourLists = [_blossomBestEdges[child]!];
                foreach (var list in neighbourLists)
                foreach (var edge in list)
                {
                    int i = _edgeU[edge], j = _edgeV[edge];
                    if (_inBlossom[j] == b) (i, j) = (j, i);
                    var bj = _inBlossom[j];
                    if (bj != b && _label[bj] == 1 && (bestEdgeTo[bj] == -1 || Slack(edge) < Slack(bestEdgeTo[bj])))
                        bestEdgeTo[bj] = edge;
                }
                _blossomBestEdges[child] = null;
                _bestEdge[child] = -1;
            }
            _blossomBestEdges[b] = bestEdgeTo.Where(edge => edge != -1).ToList();
            _bestEdge[b] = -1;
            foreach (var edge in _blossomBestEdges[b]!)
            {
                if (_bestEdge[b] == -1 || Slack(edge) < Slack(_bestEdge[b])) _bestEdge[b] = edge;
            }
        }

        /// <summary>Expands the given top-level blossom.</summary>
        private void ExpandBlossom(int b, bool endStage)
        {
            var childs = _blossomChilds[b]!;
            foreach (var s in childs)
            {
                _blossomParent[s] = -1;
                if (s < _n)
                    _inBlossom[s] = s;
                else if (endStage && _dualVar[s] == 0)
                    ExpandBlossom(s, endStage);
                else
                    foreach (var leaf in BlossomLeaves(s)) _inBlossom[leaf] = s;
            }

            if (!endStage && _label[b] == 2)
            {
                var endps = _blossomEndps[b]!;
                var entryChild = _inBlossom[_endpoint[_labelEnd[b] ^ 1]];
                var j = childs.IndexOf(entryChild);
                int jStep, endpTrick;
                if ((j & 1) != 0)
                {
                    j -= childs.Count;
                    jStep = 1;
                    endpTrick = 0;
                }
                else
                {
                    jStep = -1;
                    endpTrick = 1;
                }
                var p = _labelEnd[b];
                while (j != 0)
                {
                    _label[_endpoint[p ^ 1]] = 0;
                    _label[_endpoint[At(endps, j - endpTrick) ^ endpTrick ^ 1]] = 0;
                    AssignLabel(_endpoint[p ^ 1], 2, p);
                    _allowEdge[At(endps, j - endpTrick) / 2] = true;
                    j += jStep;
                    p = At(endps, j - endpTrick) ^ endpTrick;
                    _allowEdge[p / 2] = true;
                    j += jStep;
                }
                var bv = At(childs, j);
                _label[_endpoint[p ^ 1]] = _label[bv] = 2;
                _labelEnd[_endpoint[p ^ 1]] = _labelEnd[bv] = p;
                _bestEdge[bv] = -1;
                j += jStep;
                while (At(childs, j) != entryChild)
                {
                    bv = At(childs, j);
                    if (_label[bv] == 1)
                    {
                        j += jStep;
                        continue;
                    }
                    var labelled = -1;
                    foreach (var leaf in BlossomLeaves(bv))
                    {
                        if (_label[leaf] != 0)
                        {
                            labelled = leaf;
                            break;
                        }
                    }
                    if (labelled != -1)
                    {
                        _label[labelled] = 0;
                        _label[_endpoint[_mate[_blossomBase[bv]]]] = 0;
                        AssignLabel(labelled, 2, _labelEnd[labelled]);
                    }
                    j += jStep;
                }
            }

            _label[b] = _labelEnd[b] = -1;
            _blossomChilds[b] = _blossomEndps[b] = null;
            _blossomBase[b] = -1;
            _blossomBestEdges[b] = null;
            _bestEdge[b] = -1;
            _unusedBlossoms.Push(b);
        }

        /// <summary>Python-style indexing: a negative index counts from the end.</summary>
        private static int At(List<int> list, int index) => list[index < 0 ? index + list.Count : index];

        /// <summary>Swaps matched and unmatched edges over an alternating path through blossom b between vertex v and its base.</summary>
        private void AugmentBlossom(int b, int v)
        {
            var t = v;
            while (_blossomParent[t] != b) t = _blossomParent[t];
            if (t >= _n) AugmentBlossom(t, v);

            var childs = _blossomChilds[b]!;
            var endps = _blossomEndps[b]!;
            var i = childs.IndexOf(t);
            var j = i;
            int jStep, endpTrick;
            if ((i & 1) != 0)
            {
                j -= childs.Count;
                jStep = 1;
                endpTrick = 0;
            }
            else
            {
                jStep = -1;
                endpTrick = 1;
            }
            while (j != 0)
            {
                j += jStep;
                t = At(childs, j);
                var p = At(endps, j - endpTrick) ^ endpTrick;
                if (t >= _n) AugmentBlossom(t, _endpoint[p]);
                j += jStep;
                t = At(childs, j);
                if (t >= _n) AugmentBlossom(t, _endpoint[p ^ 1]);
                _mate[_endpoint[p]] = p ^ 1;
                _mate[_endpoint[p ^ 1]] = p;
            }
            _blossomChilds[b] = [.. childs.Skip(i), .. childs.Take(i)];
            _blossomEndps[b] = [.. endps.Skip(i), .. endps.Take(i)];
            _blossomBase[b] = _blossomBase[_blossomChilds[b]![0]];
        }

        /// <summary>Swaps matched and unmatched edges over the augmenting path through edge k.</summary>
        private void AugmentMatching(int k)
        {
            foreach (var (start, startP) in new[] { (_edgeU[k], 2 * k + 1), (_edgeV[k], 2 * k) })
            {
                var s = start;
                var p = startP;
                while (true)
                {
                    var bs = _inBlossom[s];
                    if (bs >= _n) AugmentBlossom(bs, s);
                    _mate[s] = p;
                    if (_labelEnd[bs] == -1) break;
                    var t = _endpoint[_labelEnd[bs]];
                    var bt = _inBlossom[t];
                    s = _endpoint[_labelEnd[bt]];
                    var j = _endpoint[_labelEnd[bt] ^ 1];
                    if (bt >= _n) AugmentBlossom(bt, j);
                    _mate[j] = _labelEnd[bt];
                    p = _labelEnd[bt] ^ 1;
                }
            }
        }

        public int[] Run()
        {
            for (var stage = 0; stage < _n; stage++)
            {
                Array.Fill(_label, 0);
                Array.Fill(_bestEdge, -1);
                for (var b = _n; b < 2 * _n; b++) _blossomBestEdges[b] = null;
                Array.Fill(_allowEdge, false);
                _queue.Clear();

                for (var v = 0; v < _n; v++)
                {
                    if (_mate[v] == -1 && _label[_inBlossom[v]] == 0) AssignLabel(v, 1, -1);
                }

                var augmented = false;
                while (true)
                {
                    while (_queue.Count > 0 && !augmented)
                    {
                        var v = _queue[^1];
                        _queue.RemoveAt(_queue.Count - 1);

                        foreach (var p in _neighbend[v])
                        {
                            var k = p / 2;
                            var w = _endpoint[p];
                            if (_inBlossom[v] == _inBlossom[w]) continue;
                            long kSlack = 0;
                            if (!_allowEdge[k])
                            {
                                kSlack = Slack(k);
                                if (kSlack <= 0) _allowEdge[k] = true;
                            }
                            if (_allowEdge[k])
                            {
                                if (_label[_inBlossom[w]] == 0)
                                    AssignLabel(w, 2, p ^ 1);
                                else if (_label[_inBlossom[w]] == 1)
                                {
                                    var @base = ScanBlossom(v, w);
                                    if (@base >= 0)
                                        AddBlossom(@base, k);
                                    else
                                    {
                                        AugmentMatching(k);
                                        augmented = true;
                                        break;
                                    }
                                }
                                else if (_label[w] == 0)
                                {
                                    _label[w] = 2;
                                    _labelEnd[w] = p ^ 1;
                                }
                            }
                            else if (_label[_inBlossom[w]] == 1)
                            {
                                var b = _inBlossom[v];
                                if (_bestEdge[b] == -1 || kSlack < Slack(_bestEdge[b])) _bestEdge[b] = k;
                            }
                            else if (_label[w] == 0)
                            {
                                if (_bestEdge[w] == -1 || kSlack < Slack(_bestEdge[w])) _bestEdge[w] = k;
                            }
                        }
                    }

                    if (augmented) break;

                    // No augmenting path with the current duals: change them.
                    var deltaType = -1;
                    long delta = 0;
                    int deltaEdge = -1, deltaBlossom = -1;

                    if (!_maxCardinality)
                    {
                        deltaType = 1;
                        delta = _dualVar.Take(_n).Min();
                    }
                    for (var v = 0; v < _n; v++)
                    {
                        if (_label[_inBlossom[v]] == 0 && _bestEdge[v] != -1)
                        {
                            var d = Slack(_bestEdge[v]);
                            if (deltaType == -1 || d < delta)
                            {
                                delta = d;
                                deltaType = 2;
                                deltaEdge = _bestEdge[v];
                            }
                        }
                    }
                    for (var b = 0; b < 2 * _n; b++)
                    {
                        if (_blossomParent[b] == -1 && _label[b] == 1 && _bestEdge[b] != -1)
                        {
                            var d = Slack(_bestEdge[b]) / 2;
                            if (deltaType == -1 || d < delta)
                            {
                                delta = d;
                                deltaType = 3;
                                deltaEdge = _bestEdge[b];
                            }
                        }
                    }
                    for (var b = _n; b < 2 * _n; b++)
                    {
                        if (_blossomBase[b] >= 0 && _blossomParent[b] == -1 && _label[b] == 2
                            && (deltaType == -1 || _dualVar[b] < delta))
                        {
                            delta = _dualVar[b];
                            deltaType = 4;
                            deltaBlossom = b;
                        }
                    }
                    if (deltaType == -1)
                    {
                        // Max cardinality and no further improvement possible: one last update to optimum.
                        deltaType = 1;
                        delta = Math.Max(0, _dualVar.Take(_n).Min());
                    }

                    for (var v = 0; v < _n; v++)
                    {
                        if (_label[_inBlossom[v]] == 1) _dualVar[v] -= delta;
                        else if (_label[_inBlossom[v]] == 2) _dualVar[v] += delta;
                    }
                    for (var b = _n; b < 2 * _n; b++)
                    {
                        if (_blossomBase[b] >= 0 && _blossomParent[b] == -1)
                        {
                            if (_label[b] == 1) _dualVar[b] += delta;
                            else if (_label[b] == 2) _dualVar[b] -= delta;
                        }
                    }

                    if (deltaType == 1) break;
                    if (deltaType == 2)
                    {
                        _allowEdge[deltaEdge] = true;
                        int i = _edgeU[deltaEdge], j = _edgeV[deltaEdge];
                        if (_label[_inBlossom[i]] == 0) (i, j) = (j, i);
                        _queue.Add(i);
                    }
                    else if (deltaType == 3)
                    {
                        _allowEdge[deltaEdge] = true;
                        _queue.Add(_edgeU[deltaEdge]);
                    }
                    else
                        ExpandBlossom(deltaBlossom, false);
                }

                if (!augmented) break;

                // End of stage: expand S-blossoms with zero dual.
                for (var b = _n; b < 2 * _n; b++)
                {
                    if (_blossomParent[b] == -1 && _blossomBase[b] >= 0 && _label[b] == 1 && _dualVar[b] == 0)
                        ExpandBlossom(b, true);
                }
            }

            var result = new int[_n];
            for (var v = 0; v < _n; v++) result[v] = _mate[v] >= 0 ? _endpoint[_mate[v]] : -1;
            return result;
        }
    }
}
