namespace ChessFixer.Core.Pairing;

internal enum PieceColor
{
    White,
    Black
}

/// <summary>Which color a player would like next, and how strongly (0 = none, 1 = mild, 2 = strong, 3 = absolute).</summary>
internal readonly record struct ColorPreference(PieceColor? Color, int Strength)
{
    public static ColorPreference None => new(null, 0);
}

internal static class ColorAllocator
{
    public static ColorPreference PreferenceOf(IReadOnlyList<PieceColor> colors)
    {
        if (colors.Count == 0) return ColorPreference.None;

        var difference = colors.Count(c => c == PieceColor.White) - colors.Count(c => c == PieceColor.Black);
        var last = colors[^1];
        var lastTwoSame = colors.Count >= 2 && colors[^2] == last;

        if (difference >= 2 || (lastTwoSame && last == PieceColor.White)) return new(PieceColor.Black, 3);
        if (difference <= -2 || (lastTwoSame && last == PieceColor.Black)) return new(PieceColor.White, 3);
        if (difference == 1) return new(PieceColor.Black, 2);
        if (difference == -1) return new(PieceColor.White, 2);
        return new(Opposite(last), 1);
    }

    /// <summary>How bad it is to pair these two players, color-wise. 0 when both can get their preference.</summary>
    public static int ConflictPenalty(ColorPreference a, ColorPreference b) =>
        a.Color is not null && a.Color == b.Color ? Math.Min(a.Strength, b.Strength) : 0;

    /// <summary>
    /// Decides who gets White. When both want the same color, the stronger preference wins;
    /// on a tie the higher-ranked player gets their preference.
    /// </summary>
    public static (int WhiteId, int BlackId) Assign(
        int higherId, ColorPreference higher, int lowerId, ColorPreference lower, bool higherWhiteByDefault)
    {
        PieceColor higherColor;
        if (higher.Color is null && lower.Color is null)
            higherColor = higherWhiteByDefault ? PieceColor.White : PieceColor.Black;
        else if (higher.Color is null)
            higherColor = Opposite(lower.Color!.Value);
        else if (lower.Color is null || higher.Color != lower.Color)
            higherColor = higher.Color.Value;
        else
            higherColor = lower.Strength > higher.Strength ? Opposite(lower.Color.Value) : higher.Color.Value;

        return higherColor == PieceColor.White ? (higherId, lowerId) : (lowerId, higherId);
    }

    private static PieceColor Opposite(PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;
}
