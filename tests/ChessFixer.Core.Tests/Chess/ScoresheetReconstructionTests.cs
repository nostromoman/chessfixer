using ChessFixer.Core.Chess;

namespace ChessFixer.Core.Tests.Chess;

public class ScoresheetReconstructionTests
{
    private static ReadMove W(int n, string text, bool certain = true, params string[] alternatives) => new(n, true, text, alternatives, certain);
    private static ReadMove B(int n, string text, bool certain = true, params string[] alternatives) => new(n, false, text, alternatives, certain);

    private static Task<string?> NoRepair(RepairQuestion _) => Task.FromResult<string?>(null);

    [Fact]
    public async Task CleanSheet_DutchNotation()
    {
        var game = await ScoresheetReconstruction.ReconstructAsync(
            [W(1, "e4"), B(1, "e5"), W(2, "Pf3"), B(2, "Pc6"), W(3, "Lb5"), B(3, "a6")], NoRepair);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5", "a6"], game.Moves);
        Assert.Empty(game.Uncertain);
        Assert.Null(game.Stop);
    }

    [Fact]
    public async Task IllegalReading_TheLegalAlternativeIsTaken_AndMarked()
    {
        // "Nf5" is not possible after 1. e4 e5; the alternative Nf3 is.
        var game = await ScoresheetReconstruction.ReconstructAsync(
            [W(1, "e4"), B(1, "e5"), W(2, "Nf5", false, "Nf3"), B(2, "Nc6")], NoRepair);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6"], game.Moves);
        Assert.Equal(new UncertainMove(2, true, "Nf3", "Nf5"), Assert.Single(game.Uncertain));
    }

    [Fact]
    public async Task UncertainButLegal_IsKeptAndMarked()
    {
        var game = await ScoresheetReconstruction.ReconstructAsync([W(1, "d4", certain: false)], NoRepair);
        Assert.Equal(["d4"], game.Moves);
        Assert.Single(game.Uncertain);
    }

    [Fact]
    public async Task NoLegalReading_RepairIsAsked_WithTheLegalMoves()
    {
        RepairQuestion? asked = null;
        var game = await ScoresheetReconstruction.ReconstructAsync(
            [W(1, "e4"), B(1, "Pg6"), W(2, "d4")],
            q => { asked = q; return Task.FromResult<string?>("Nf6"); });

        Assert.NotNull(asked);
        Assert.Equal(1, asked!.Number);
        Assert.False(asked.White);
        Assert.Equal(["e4"], asked.PlayedSoFar);
        Assert.Contains("Nf6", asked.LegalMoves);
        Assert.Equal(20, asked.LegalMoves.Count);
        Assert.Equal(["e4", "Nf6", "d4"], game.Moves);
        Assert.Null(game.Stop);
    }

    [Fact]
    public async Task RepairFails_StopsThere_WithTheRestUnread()
    {
        var game = await ScoresheetReconstruction.ReconstructAsync(
            [W(1, "e4"), B(1, "Kxx"), W(2, "d4")], NoRepair);
        Assert.Equal(["e4"], game.Moves);
        var stop = game.Stop!;
        Assert.Equal((1, false, "Kxx"), (stop.Number, stop.White, stop.ReadAs));
        Assert.Equal(["1... Kxx", "2. d4"], stop.Unread);
    }

    [Fact]
    public async Task RepairAnswerThatIsNotLegal_IsIgnored()
    {
        var game = await ScoresheetReconstruction.ReconstructAsync([W(1, "e5")], _ => Task.FromResult<string?>("e5"));
        Assert.Empty(game.Moves);
        Assert.NotNull(game.Stop);
    }

    [Fact]
    public async Task SkippedHalfMove_Stops()
    {
        var game = await ScoresheetReconstruction.ReconstructAsync([W(1, "e4"), W(2, "d4")], NoRepair);
        Assert.Equal(["e4"], game.Moves);
        Assert.Equal(2, game.Stop!.Number);
    }

    [Fact]
    public async Task AtMostThreeRepairs()
    {
        var asked = 0;
        // Every black move is unreadable; the repair always answers a legal pawn move.
        var read = new List<ReadMove>();
        string[] white = ["a3", "b3", "c3", "d3", "e3"];
        string[] black = ["a6", "b6", "c6", "d6", "e6"];
        for (var n = 1; n <= 5; n++)
        {
            read.Add(W(n, white[n - 1]));
            read.Add(B(n, "??"));
        }
        var game = await ScoresheetReconstruction.ReconstructAsync(read, q =>
        {
            asked++;
            return Task.FromResult<string?>(black[q.Number - 1]);
        });

        Assert.Equal(ScoresheetReconstruction.MaxRepairs, asked);
        Assert.Equal(7, game.Moves.Count); // 3 full moves + White's 4th
        Assert.Equal(4, game.Stop!.Number);
    }
}
