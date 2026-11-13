using ChessFixer.Shared.Pgn;

namespace ChessFixer.Core.Tests.Pgn;

public class PgnTextTests
{
    private const string Example = """
        [Event "?"]
        [Site "?"]
        [Date "2026.11.05"]
        [Round "?"]
        [White "Roel"]
        [Black "Frank"]
        [Result "*"]
        [Link "https://www.chess.com/a/JhRwnGei27ttE/analysis?move=24"]

        1. e4 Nc6 2. Nf3 e5 3. Bc4 Bc5 4. c3 d6 5. d4 exd4 6. cxd4 Na5 $4 7. dxc5 Nxc4 8.
        Qa4+ $1 c6 $2 9. Qxc4 $1 Qa5+ 10. b4 Be6 $2 11. Qxe6+ fxe6 12. bxa5 $1 Nf6 13. Ng5 *
        """;

    [Fact]
    public void Example_HasMovesAndHeaders()
    {
        Assert.True(PgnText.HasMoves(Example));
        Assert.Equal("Roel", PgnText.Header(Example, "White"));
        Assert.Equal("Frank", PgnText.Header(Example, "Black"));
        Assert.Equal("2026.11.05", PgnText.Header(Example, "Date"));
        Assert.Null(PgnText.Header(Example, "Event")); // "?" means unknown
        Assert.Null(PgnText.Header(Example, "Annotator"));
    }

    [Fact]
    public void MoveText_DropsHeadersAnnotationsCommentsAndVariations()
    {
        Assert.Equal(
            "1. e4 Nc6 2. Nf3 e5 3. Bc4 Bc5 4. c3 d6 5. d4 exd4 6. cxd4 Na5 7. dxc5 Nxc4 8. Qa4+ c6 9. Qxc4 Qa5+ " +
            "10. b4 Be6 11. Qxe6+ fxe6 12. bxa5 Nf6 13. Ng5 *",
            PgnText.MoveText(Example));

        Assert.Equal("1. e4 e5 2. Nf3 1-0",
            PgnText.MoveText("1. e4 {best by test} e5 (1... c5 2. Nf3 (2. c3)) 2. Nf3 ; a comment\n1-0"));
    }

    [Theory]
    [InlineData("1. e4 e5")]
    [InlineData("1.d4 d5 2.c4")]
    [InlineData("[White \"A\"]\n\n1. Nf3")]
    public void HasMoves_True(string pgn) => Assert.True(PgnText.HasMoves(pgn));

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("[White \"A\"]\n[Black \"B\"]")]
    [InlineData("{1. e4 in a comment}")]
    public void HasMoves_False(string pgn) => Assert.False(PgnText.HasMoves(pgn));

    [Fact]
    public void Normalize_TrimsAndUsesNewlines()
    {
        Assert.Null(PgnText.Normalize("  \r\n "));
        Assert.Equal("[White \"A\"]\n\n1. e4", PgnText.Normalize("\r\n[White \"A\"]\r\n\r\n1. e4  \r\n"));
    }
}
