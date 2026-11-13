using System.Text.RegularExpressions;
using ChessFixer.Shared.Pgn;

namespace ChessFixer.Core.Chess;

/// <summary>One half-move of a game: the move as SAN and UCI, and the position after it.</summary>
public sealed record PgnPly(string San, string Uci, string FenAfter);

/// <summary>
/// The moves of a PGN replayed on the board. When a move cannot be played (a typo, an illegal move), the game
/// stops there: <see cref="Plies"/> holds the moves before it and <see cref="Error"/> says which move it was.
/// </summary>
/// <param name="StartFen">The start position (the FEN tag, or the normal start position).</param>
public sealed partial record PgnGame(string StartFen, IReadOnlyList<PgnPly> Plies, PgnMoveError? Error)
{
    /// <summary>The position before each ply, and after the last one (Plies.Count + 1 positions).</summary>
    public IReadOnlyList<string> Fens => [StartFen, .. Plies.Select(p => p.FenAfter)];

    public static PgnGame Parse(string pgn)
    {
        var startFen = PgnText.Header(pgn, "FEN") ?? Position.StartFen;
        Position position;
        try
        {
            position = Position.FromFen(startFen);
        }
        catch (FormatException)
        {
            return new PgnGame(Position.StartFen, [], new PgnMoveError(1, true, "FEN"));
        }

        var plies = new List<PgnPly>();
        foreach (var raw in PgnText.MoveText(pgn).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsResult(raw)) continue;
            var token = MoveNumber().Replace(raw, "");
            if (token.Length == 0 || AnnotationOnly().IsMatch(token)) continue;

            try
            {
                var move = position.ParseSan(token);
                var san = position.ToSan(move);
                position = position.Play(move);
                plies.Add(new PgnPly(san, move.ToUci(), position.ToFen()));
            }
            catch (FormatException)
            {
                return new PgnGame(startFen, plies, new PgnMoveError(position.FullmoveNumber, position.WhiteToMove, token));
            }
        }
        return new PgnGame(startFen, plies, null);
    }

    private static bool IsResult(string token) => token is "1-0" or "0-1" or "1/2-1/2" or "½-½" or "*";

    [GeneratedRegex(@"^\d+\.*")]
    private static partial Regex MoveNumber();

    [GeneratedRegex(@"^[!?]+$")]
    private static partial Regex AnnotationOnly();
}

/// <summary>The move that could not be played: move <paramref name="MoveNumber"/> of White or Black.</summary>
public sealed record PgnMoveError(int MoveNumber, bool White, string Text);
