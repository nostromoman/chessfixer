using ChessFixer.Core.Chess;

namespace ChessFixer.Core.Tests.Chess;

public class PositionTests
{
    private static long Perft(Position position, int depth)
    {
        if (depth == 0) return 1;
        var moves = position.LegalMoves();
        if (depth == 1) return moves.Count;
        return moves.Sum(m => Perft(position.Play(m), depth - 1));
    }

    /// <summary>Known move counts (chessprogramming.org "Perft Results"): they only match when castling, en passant,
    /// promotion, pins and checks are all generated correctly.</summary>
    [Theory]
    [InlineData(Position.StartFen, 3, 8902)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2039)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97862)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43238)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9467)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62379)]
    public void Perft_MatchesKnownCounts(string fen, int depth, long expected) =>
        Assert.Equal(expected, Perft(Position.FromFen(fen), depth));

    [Fact]
    public void Fen_RoundTrips()
    {
        Assert.Equal(Position.StartFen, Position.Start.ToFen());
        var after = Position.Start.Play(Position.Start.ParseSan("e4"));
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", after.ToFen());
    }

    [Theory]
    [InlineData("e4", "e2e4", "e4")]
    [InlineData("Nf3", "g1f3", "Nf3")]
    [InlineData("Pf3", "g1f3", "Nf3")]     // Dutch: paard
    [InlineData("g1f3", "g1f3", "Nf3")]    // long algebraic
    [InlineData("e2-e4", "e2e4", "e4")]
    [InlineData("Nf3!?", "g1f3", "Nf3")]
    public void ParseSan_FromStart(string text, string uci, string san)
    {
        var move = Position.Start.ParseSan(text);
        Assert.Equal(uci, move.ToUci());
        Assert.Equal(san, Position.Start.ToSan(move));
    }

    [Fact]
    public void San_DisambiguationCastlingPromotionAndMate()
    {
        // Two knights can go to d2: the file is added.
        var knights = Position.FromFen("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1");
        Assert.Equal("Nbd2", knights.ToSan(knights.ParseSan("Nbd2")));
        Assert.Throws<FormatException>(() => knights.ParseSan("Nd2"));

        var castle = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Assert.Equal("O-O", castle.ToSan(castle.ParseSan("0-0")));
        Assert.Equal("O-O-O", castle.ToSan(castle.ParseSan("O-O-O")));

        var promote = Position.FromFen("8/P6k/8/8/8/8/8/K7 w - - 0 1");
        Assert.Equal("a8=Q", promote.ToSan(promote.ParseSan("a8=Q")));
        Assert.Equal("a8=N", promote.ToSan(promote.ParseSan("a8N")));
        Assert.Equal("a8=Q", promote.ToSan(promote.ParseSan("a8D"))); // Dutch: dame

        var mate = Position.FromFen("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
        var move = mate.ParseSan("Ra8");
        Assert.Equal("Ra8#", mate.ToSan(move));
        Assert.True(mate.Play(move).IsCheckmate);
    }

    [Fact]
    public void EnPassant()
    {
        var position = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        var move = position.ParseSan("exd6");
        Assert.Equal("exd6", position.ToSan(move));
        Assert.Equal('\0', position.Play(move)[35]); // d5 pawn is gone
    }

    [Fact]
    public void IllegalMove_Throws()
    {
        Assert.Throws<FormatException>(() => Position.Start.ParseSan("e5"));
        Assert.Throws<FormatException>(() => Position.Start.ParseSan("Ke2"));
        Assert.Throws<FormatException>(() => Position.Start.ParseSan("hello"));
    }
}
