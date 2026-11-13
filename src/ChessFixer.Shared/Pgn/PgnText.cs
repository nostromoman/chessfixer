using System.Text;
using System.Text.RegularExpressions;

namespace ChessFixer.Shared.Pgn;

/// <summary>
/// The PGN (Portable Game Notation) of a played game, as a player noted it down or exported it from an app.
/// It is stored as text; only checked lightly (it must contain moves), not validated move by move.
/// </summary>
public static partial class PgnText
{
    /// <summary>Plenty for any game with comments; keeps the text out of the way of a pasted wall of text.</summary>
    public const int MaxLength = 20_000;

    /// <summary>The text as it is stored: trimmed, with \n line endings; null when there is nothing.</summary>
    public static string? Normalize(string? pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return null;
        return pgn.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    /// <summary>The text contains at least one numbered move ("1. e4" or "1...e5").</summary>
    public static bool HasMoves(string? pgn) => pgn is not null && FirstMove().IsMatch(MoveText(pgn));

    /// <summary>A header tag such as White, Black or Date; null when it is missing or "?".</summary>
    public static string? Header(string pgn, string name)
    {
        foreach (Match match in HeaderLine().Matches(pgn))
        {
            if (!string.Equals(match.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase)) continue;
            var value = match.Groups[2].Value.Replace("\\\"", "\"").Trim();
            return value is "" or "?" or "????.??.??" ? null : value;
        }
        return null;
    }

    /// <summary>
    /// Only the moves (with move numbers and the result): without header tags, comments, variations and
    /// annotation glyphs ($4), on one line. This is what an analysis board needs to replay the game.
    /// </summary>
    public static string MoveText(string pgn)
    {
        var text = new StringBuilder();
        var depth = 0;     // inside a variation ( ... ), possibly nested
        var inComment = false;
        foreach (var rawLine in pgn.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (!inComment && depth == 0 && line.StartsWith('[')) continue; // header tag
            if (!inComment && line.StartsWith('%')) continue;               // escape line

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inComment)
                {
                    if (c == '}') inComment = false;
                    continue;
                }
                if (c == '{') { inComment = true; continue; }
                if (c == ';') break; // comment to the end of the line
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
                if (depth == 0) text.Append(c);
            }
            text.Append(' ');
        }

        var moves = Nag().Replace(text.ToString(), " ");
        return Whitespace().Replace(moves, " ").Trim();
    }

    [GeneratedRegex(@"^\s*\[\s*(\w+)\s+""((?:[^""\\]|\\.)*)""\s*\]", RegexOptions.Multiline)]
    private static partial Regex HeaderLine();

    [GeneratedRegex(@"(^|\s)1\s*\.+\s*[a-hKQRBNO]")]
    private static partial Regex FirstMove();

    [GeneratedRegex(@"\$\d+")]
    private static partial Regex Nag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
