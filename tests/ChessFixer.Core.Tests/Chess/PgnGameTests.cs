using ChessFixer.Core.Chess;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Chess;

public class PgnGameTests
{
    private const string Example = """
        [White "Roel"]
        [Black "Frank"]
        [Result "*"]

        1. e4 Nc6 2. Nf3 e5 3. Bc4 Bc5 4. c3 d6 5. d4 exd4 6. cxd4 Na5 $4 7. dxc5 Nxc4 8.
        Qa4+ $1 c6 $2 9. Qxc4 $1 Qa5+ 10. b4 Be6 $2 11. Qxe6+ fxe6 12. bxa5 $1 Nf6 13. Ng5 *
        """;

    [Fact]
    public void Example_AllMovesReplayed()
    {
        var game = PgnGame.Parse(Example);
        Assert.Null(game.Error);
        Assert.Equal(25, game.Plies.Count);
        Assert.Equal("e2e4", game.Plies[0].Uci);
        Assert.Equal("Qa4+", game.Plies[14].San);
        Assert.Equal("Ng5", game.Plies[^1].San);
        Assert.Equal(26, game.Fens.Count);
        Assert.Equal(Position.StartFen, game.Fens[0]);
    }

    [Fact]
    public void IllegalMove_StopsThereWithError()
    {
        var game = PgnGame.Parse("1. e4 e5 2. Nf3 Nc6 3. Bb5 Ke7 4. Qe2 Qe8 5. Bxe8 1-0");
        Assert.Equal(8, game.Plies.Count);
        Assert.Equal(new PgnMoveError(5, true, "Bxe8"), game.Error);

        game = PgnGame.Parse("1. e4 e5 2. Nf3 Nf3");
        Assert.Equal(new PgnMoveError(2, false, "Nf3"), game.Error);
    }

    [Fact]
    public void MoveNumbersGluedToMoves_AndDutchPieces()
    {
        var game = PgnGame.Parse("1.e4 e5 2.Pf3 Pc6 3.Lb5 a6 4.O-O");
        Assert.Null(game.Error);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "O-O"], game.Plies.Select(p => p.San));
    }

    [Fact]
    public void FenTag_IsStartPosition()
    {
        var game = PgnGame.Parse("[SetUp \"1\"]\n[FEN \"6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1\"]\n\n1. Ra8# 1-0");
        Assert.Null(game.Error);
        Assert.Equal("Ra8#", Assert.Single(game.Plies).San);
    }

    [Fact]
    public void Judge_ClassifiesByLossOfWinChance()
    {
        var equal = new Evaluation(0, null);
        Assert.Equal(MoveQuality.Best, MoveJudgement.Judge(equal, new Evaluation(-20, null), true, playedBest: true).Quality);
        Assert.Equal(MoveQuality.Good, MoveJudgement.Judge(equal, new Evaluation(-20, null), true, false).Quality);
        Assert.Equal(MoveQuality.Inaccuracy, MoveJudgement.Judge(equal, new Evaluation(-70, null), true, false).Quality);
        Assert.Equal(MoveQuality.Mistake, MoveJudgement.Judge(equal, new Evaluation(-130, null), true, false).Quality);
        Assert.Equal(MoveQuality.Blunder, MoveJudgement.Judge(equal, new Evaluation(-300, null), true, false).Quality);

        // Black's move is judged from Black's side: White's score going up is bad for Black.
        Assert.Equal(MoveQuality.Blunder, MoveJudgement.Judge(equal, new Evaluation(300, null), false, false).Quality);
        Assert.Equal(MoveQuality.Good, MoveJudgement.Judge(equal, new Evaluation(-300, null), false, false).Quality);

        // Missing a mate in one for a won position is not a blunder when still winning big.
        var judgement = MoveJudgement.Judge(new Evaluation(null, 1), new Evaluation(900, null), true, false);
        Assert.True(judgement.WinChanceLoss < 5);

        // Checkmate on the board: the side to move lost.
        Assert.Equal(0, new Evaluation(null, 0).WhiteWinChance(whiteToMove: true));
        Assert.Equal(100, new Evaluation(null, 0).WhiteWinChance(whiteToMove: false));
        Assert.Equal(100, MoveJudgement.Judge(equal, new Evaluation(null, 0), true, true).Accuracy, 0);
    }
}
