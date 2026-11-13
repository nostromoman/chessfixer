using System.Text;

namespace ChessFixer.Core.Chess;

/// <summary>A move from one square to another (0 = a1, 7 = h1, 63 = h8); <paramref name="Promotion"/> is the
/// piece a pawn becomes ('Q', 'R', 'B' or 'N'), or '\0'.</summary>
public readonly record struct Move(int From, int To, char Promotion = '\0')
{
    /// <summary>The move in UCI notation, as a chess engine reads it: "e2e4", "e7e8q".</summary>
    public string ToUci() =>
        Position.SquareName(From) + Position.SquareName(To) + (Promotion == '\0' ? "" : char.ToLowerInvariant(Promotion));
}

/// <summary>
/// A chess position with legal move generation and SAN (standard algebraic notation) reading and writing.
/// Immutable: <see cref="Play"/> returns a new position. Pieces are FEN letters: uppercase White, lowercase Black.
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly (int File, int Rank)[] KnightSteps = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int File, int Rank)[] KingSteps = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    private static readonly (int File, int Rank)[] RookDirections = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int File, int Rank)[] BishopDirections = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    private readonly char[] _board;

    private Position(char[] board, bool whiteToMove, string castling, int enPassant, int halfmoveClock, int fullmoveNumber)
    {
        _board = board;
        WhiteToMove = whiteToMove;
        Castling = castling;
        EnPassant = enPassant;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
    }

    public static Position Start { get; } = FromFen(StartFen);

    public bool WhiteToMove { get; }

    /// <summary>The castling rights as in FEN: a subset of "KQkq", or "-".</summary>
    public string Castling { get; }

    /// <summary>The square behind a pawn that just moved two squares; -1 when there is none.</summary>
    public int EnPassant { get; }

    public int HalfmoveClock { get; }
    public int FullmoveNumber { get; }

    /// <summary>The piece on the square ('\0' when empty).</summary>
    public char this[int square] => _board[square];

    public static string SquareName(int square) => $"{(char)('a' + square % 8)}{square / 8 + 1}";

    private static int Square(int file, int rank) => rank * 8 + file;
    private static bool OnBoard(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8;
    private static bool IsWhite(char piece) => char.IsUpper(piece);
    private bool IsOwn(char piece) => piece != '\0' && IsWhite(piece) == WhiteToMove;
    private bool IsEnemy(char piece) => piece != '\0' && IsWhite(piece) != WhiteToMove;

    // ----- FEN -----

    public static Position FromFen(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new FormatException($"Invalid FEN: {fen}");

        var board = new char[64];
        var ranks = parts[0].Split('/');
        if (ranks.Length != 8) throw new FormatException($"Invalid FEN: {fen}");
        for (var r = 0; r < 8; r++)
        {
            var file = 0;
            foreach (var c in ranks[r])
            {
                if (char.IsDigit(c)) file += c - '0';
                else if ("PNBRQKpnbrqk".Contains(c) && file < 8) board[Square(file++, 7 - r)] = c;
                else throw new FormatException($"Invalid FEN: {fen}");
            }
            if (file != 8) throw new FormatException($"Invalid FEN: {fen}");
        }

        var whiteToMove = parts[1] switch
        {
            "w" => true,
            "b" => false,
            _ => throw new FormatException($"Invalid FEN: {fen}")
        };
        var castling = parts.Length > 2 ? parts[2] : "-";
        var enPassant = parts.Length > 3 && parts[3] != "-" ? ParseSquare(parts[3]) : -1;
        var halfmove = parts.Length > 4 && int.TryParse(parts[4], out var h) ? h : 0;
        var fullmove = parts.Length > 5 && int.TryParse(parts[5], out var f) ? f : 1;
        return new Position(board, whiteToMove, castling == "" ? "-" : castling, enPassant, halfmove, fullmove);
    }

    public string ToFen()
    {
        var fen = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var piece = _board[Square(file, rank)];
                if (piece == '\0') { empty++; continue; }
                if (empty > 0) { fen.Append(empty); empty = 0; }
                fen.Append(piece);
            }
            if (empty > 0) fen.Append(empty);
            if (rank > 0) fen.Append('/');
        }
        fen.Append(WhiteToMove ? " w " : " b ").Append(Castling)
            .Append(' ').Append(EnPassant < 0 ? "-" : SquareName(EnPassant))
            .Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
        return fen.ToString();
    }

    private static int ParseSquare(string name) =>
        name.Length == 2 && name[0] is >= 'a' and <= 'h' && name[1] is >= '1' and <= '8'
            ? Square(name[0] - 'a', name[1] - '1')
            : throw new FormatException($"Invalid square: {name}");

    // ----- Attacks and check -----

    /// <summary>Is the square attacked by a piece of the given color?</summary>
    public bool IsAttacked(int square, bool byWhite)
    {
        int file = square % 8, rank = square / 8;

        // Pawns attack diagonally forward, so look diagonally backward from the square.
        var pawnRank = rank + (byWhite ? -1 : 1);
        foreach (var df in new[] { -1, 1 })
            if (OnBoard(file + df, pawnRank) && _board[Square(file + df, pawnRank)] == (byWhite ? 'P' : 'p'))
                return true;

        foreach (var (df, dr) in KnightSteps)
            if (OnBoard(file + df, rank + dr) && _board[Square(file + df, rank + dr)] == (byWhite ? 'N' : 'n'))
                return true;
        foreach (var (df, dr) in KingSteps)
            if (OnBoard(file + df, rank + dr) && _board[Square(file + df, rank + dr)] == (byWhite ? 'K' : 'k'))
                return true;

        return SlidingAttack(file, rank, RookDirections, byWhite ? "RQ" : "rq")
               || SlidingAttack(file, rank, BishopDirections, byWhite ? "BQ" : "bq");
    }

    private bool SlidingAttack(int file, int rank, (int File, int Rank)[] directions, string attackers)
    {
        foreach (var (df, dr) in directions)
        {
            int f = file + df, r = rank + dr;
            while (OnBoard(f, r))
            {
                var piece = _board[Square(f, r)];
                if (piece != '\0')
                {
                    if (attackers.Contains(piece)) return true;
                    break;
                }
                f += df;
                r += dr;
            }
        }
        return false;
    }

    private int KingSquare(bool white) => Array.IndexOf(_board, white ? 'K' : 'k');

    /// <summary>The side to move is in check.</summary>
    public bool InCheck
    {
        get
        {
            var king = KingSquare(WhiteToMove);
            return king >= 0 && IsAttacked(king, !WhiteToMove);
        }
    }

    public bool IsCheckmate => InCheck && LegalMoves().Count == 0;
    public bool IsStalemate => !InCheck && LegalMoves().Count == 0;

    // ----- Move generation -----

    public IReadOnlyList<Move> LegalMoves()
    {
        var legal = new List<Move>();
        foreach (var move in PseudoLegalMoves())
        {
            var after = Play(move);
            var king = after.KingSquare(WhiteToMove);
            if (king < 0 || !after.IsAttacked(king, !WhiteToMove)) legal.Add(move);
        }
        return legal;
    }

    private IEnumerable<Move> PseudoLegalMoves()
    {
        for (var from = 0; from < 64; from++)
        {
            var piece = _board[from];
            if (!IsOwn(piece)) continue;
            int file = from % 8, rank = from / 8;

            switch (char.ToUpperInvariant(piece))
            {
                case 'P':
                    foreach (var move in PawnMoves(from, file, rank)) yield return move;
                    break;
                case 'N':
                    foreach (var move in StepMoves(from, file, rank, KnightSteps)) yield return move;
                    break;
                case 'B':
                    foreach (var move in SlidingMoves(from, file, rank, BishopDirections)) yield return move;
                    break;
                case 'R':
                    foreach (var move in SlidingMoves(from, file, rank, RookDirections)) yield return move;
                    break;
                case 'Q':
                    foreach (var move in SlidingMoves(from, file, rank, RookDirections)) yield return move;
                    foreach (var move in SlidingMoves(from, file, rank, BishopDirections)) yield return move;
                    break;
                case 'K':
                    foreach (var move in StepMoves(from, file, rank, KingSteps)) yield return move;
                    foreach (var move in CastlingMoves(from)) yield return move;
                    break;
            }
        }
    }

    private IEnumerable<Move> PawnMoves(int from, int file, int rank)
    {
        var dir = WhiteToMove ? 1 : -1;
        var startRank = WhiteToMove ? 1 : 6;
        var lastRank = WhiteToMove ? 7 : 0;

        IEnumerable<Move> To(int to) =>
            to / 8 == lastRank
                ? "QRBN".Select(p => new Move(from, to, p))
                : [new Move(from, to)];

        var oneStep = Square(file, rank + dir);
        if (_board[oneStep] == '\0')
        {
            foreach (var move in To(oneStep)) yield return move;
            var twoSteps = Square(file, rank + 2 * dir);
            if (rank == startRank && _board[twoSteps] == '\0') yield return new Move(from, twoSteps);
        }

        foreach (var df in new[] { -1, 1 })
        {
            if (!OnBoard(file + df, rank + dir)) continue;
            var to = Square(file + df, rank + dir);
            if (IsEnemy(_board[to]))
                foreach (var move in To(to)) yield return move;
            else if (to == EnPassant)
                yield return new Move(from, to);
        }
    }

    private IEnumerable<Move> StepMoves(int from, int file, int rank, (int File, int Rank)[] steps)
    {
        foreach (var (df, dr) in steps)
        {
            if (!OnBoard(file + df, rank + dr)) continue;
            var to = Square(file + df, rank + dr);
            if (!IsOwn(_board[to])) yield return new Move(from, to);
        }
    }

    private IEnumerable<Move> SlidingMoves(int from, int file, int rank, (int File, int Rank)[] directions)
    {
        foreach (var (df, dr) in directions)
        {
            int f = file + df, r = rank + dr;
            while (OnBoard(f, r))
            {
                var to = Square(f, r);
                if (IsOwn(_board[to])) break;
                yield return new Move(from, to);
                if (_board[to] != '\0') break;
                f += df;
                r += dr;
            }
        }
    }

    private IEnumerable<Move> CastlingMoves(int from)
    {
        var (king, kingSide, queenSide, home) = WhiteToMove ? ('K', 'K', 'Q', 4) : ('k', 'k', 'q', 60);
        if (from != home || _board[home] != king) yield break;
        var enemy = !WhiteToMove;
        var rook = WhiteToMove ? 'R' : 'r';

        if (Castling.Contains(kingSide) && _board[home + 3] == rook && _board[home + 1] == '\0' && _board[home + 2] == '\0'
            && !IsAttacked(home, enemy) && !IsAttacked(home + 1, enemy))
            yield return new Move(home, home + 2);

        if (Castling.Contains(queenSide) && _board[home - 4] == rook && _board[home - 1] == '\0' && _board[home - 2] == '\0'
            && _board[home - 3] == '\0' && !IsAttacked(home, enemy) && !IsAttacked(home - 1, enemy))
            yield return new Move(home, home - 2);
    }

    // ----- Playing a move -----

    /// <summary>The position after the move. The move is not checked for legality: use a move from <see cref="LegalMoves"/>.</summary>
    public Position Play(Move move)
    {
        var board = (char[])_board.Clone();
        var piece = board[move.From];
        var captured = board[move.To];
        var isPawn = char.ToUpperInvariant(piece) == 'P';

        board[move.To] = move.Promotion == '\0' ? piece : WhiteToMove ? char.ToUpperInvariant(move.Promotion) : char.ToLowerInvariant(move.Promotion);
        board[move.From] = '\0';

        // En passant: the captured pawn is beside the target square.
        if (isPawn && move.To == EnPassant && captured == '\0')
        {
            board[move.To - (WhiteToMove ? 8 : -8)] = '\0';
            captured = WhiteToMove ? 'p' : 'P';
        }

        // Castling: the rook jumps over the king.
        if (char.ToUpperInvariant(piece) == 'K' && Math.Abs(move.To - move.From) == 2)
        {
            var (rookFrom, rookTo) = move.To > move.From ? (move.From + 3, move.From + 1) : (move.From - 4, move.From - 1);
            board[rookTo] = board[rookFrom];
            board[rookFrom] = '\0';
        }

        var castling = Castling;
        void Lose(char right) => castling = castling.Replace(right.ToString(), "");
        if (piece == 'K') { Lose('K'); Lose('Q'); }
        if (piece == 'k') { Lose('k'); Lose('q'); }
        foreach (var square in new[] { move.From, move.To })
        {
            switch (square)
            {
                case 0: Lose('Q'); break;
                case 7: Lose('K'); break;
                case 56: Lose('q'); break;
                case 63: Lose('k'); break;
            }
        }
        if (castling == "") castling = "-";

        var enPassant = isPawn && Math.Abs(move.To - move.From) == 16 ? (move.From + move.To) / 2 : -1;
        var halfmove = isPawn || captured != '\0' ? 0 : HalfmoveClock + 1;
        var fullmove = WhiteToMove ? FullmoveNumber : FullmoveNumber + 1;
        return new Position(board, !WhiteToMove, castling, enPassant, halfmove, fullmove);
    }

    // ----- SAN -----

    /// <summary>The move in standard algebraic notation, as in PGN: "Nf3", "exd5", "O-O", "e8=Q+", "Qxf7#".</summary>
    public string ToSan(Move move)
    {
        var piece = char.ToUpperInvariant(_board[move.From]);
        string san;
        if (piece == 'K' && Math.Abs(move.To - move.From) == 2)
        {
            san = move.To > move.From ? "O-O" : "O-O-O";
        }
        else
        {
            var isCapture = _board[move.To] != '\0' || (piece == 'P' && move.To == EnPassant);
            var text = new StringBuilder();
            if (piece == 'P')
            {
                if (isCapture) text.Append((char)('a' + move.From % 8));
            }
            else
            {
                text.Append(piece);
                var others = LegalMoves()
                    .Where(m => m.To == move.To && m.From != move.From && char.ToUpperInvariant(_board[m.From]) == piece)
                    .ToList();
                if (others.Count > 0)
                {
                    if (others.All(m => m.From % 8 != move.From % 8)) text.Append((char)('a' + move.From % 8));
                    else if (others.All(m => m.From / 8 != move.From / 8)) text.Append((char)('1' + move.From / 8));
                    else text.Append(SquareName(move.From));
                }
            }
            if (isCapture) text.Append('x');
            text.Append(SquareName(move.To));
            if (move.Promotion != '\0') text.Append('=').Append(char.ToUpperInvariant(move.Promotion));
            san = text.ToString();
        }

        var after = Play(move);
        if (after.InCheck) san += after.LegalMoves().Count == 0 ? "#" : "+";
        return san;
    }

    /// <summary>
    /// Reads a move in algebraic notation: SAN ("Nf3", "exd5", "e8=Q", "O-O"), also without "x" or "=", with
    /// annotations ("Nf3!?"), as long algebraic ("e2-e4", "g1f3") and with Dutch piece letters
    /// (K, D = queen, T = rook, L = bishop, P = knight), as on a Dutch scoresheet.
    /// </summary>
    /// <exception cref="FormatException">Not a legal move in this position, or ambiguous.</exception>
    public Move ParseSan(string text)
    {
        var san = text.Trim().TrimEnd('+', '#', '!', '?');
        if (san is "O-O" or "0-0" or "O-O-O" or "0-0-0")
        {
            var home = WhiteToMove ? 4 : 60;
            var to = san.Length == 3 ? home + 2 : home - 2;
            return Single(LegalMoves().Where(m => m.From == home && m.To == to && char.ToUpperInvariant(_board[home]) == 'K'), text);
        }

        san = san.Replace("x", "").Replace(":", "").Replace("-", "").Replace("=", "").Replace("e.p.", "");
        if (san.Length < 2) throw new FormatException($"Invalid move: {text}");

        var piece = 'P';
        var pieceGiven = true;
        var first = san[0];
        if ("KQRBN".Contains(first)) { piece = first; san = san[1..]; }
        else if ("DTLP".Contains(first)) { piece = first switch { 'D' => 'Q', 'T' => 'R', 'L' => 'B', _ => 'N' }; san = san[1..]; }
        else pieceGiven = false;

        var promotion = '\0';
        if (san.Length > 2 && "QRBNDTLPqrbn".Contains(san[^1]) && piece == 'P')
        {
            promotion = char.ToUpperInvariant(san[^1]) switch { 'D' => 'Q', 'T' => 'R', 'L' => 'B', 'P' => 'N', var p => p };
            san = san[..^1];
        }

        if (san.Length < 2) throw new FormatException($"Invalid move: {text}");
        int to2;
        try
        {
            to2 = ParseSquare(san[^2..]);
        }
        catch (FormatException)
        {
            throw new FormatException($"Invalid move: {text}");
        }

        var hint = san[..^2]; // disambiguation: a file, a rank or both
        int? fromFile = null, fromRank = null;
        foreach (var c in hint)
        {
            if (c is >= 'a' and <= 'h') fromFile = c - 'a';
            else if (c is >= '1' and <= '8') fromRank = c - '1';
            else throw new FormatException($"Invalid move: {text}");
        }

        var candidates = LegalMoves().Where(m =>
            m.To == to2
            // Long algebraic without piece letter ("g1f3"): the from square says which piece it is.
            && (char.ToUpperInvariant(_board[m.From]) == piece || (!pieceGiven && fromFile is not null && fromRank is not null))
            && (fromFile is null || m.From % 8 == fromFile)
            && (fromRank is null || m.From / 8 == fromRank)
            && (m.Promotion == '\0' || m.Promotion == (promotion == '\0' ? 'Q' : promotion))
            && (promotion == '\0' || m.Promotion == promotion));
        return Single(candidates, text);
    }

    private static Move Single(IEnumerable<Move> candidates, string text)
    {
        var list = candidates.ToList();
        return list.Count switch
        {
            1 => list[0],
            0 => throw new FormatException($"Illegal move: {text}"),
            _ => throw new FormatException($"Ambiguous move: {text}")
        };
    }

    /// <summary>Reads a move in UCI notation ("e2e4", "e7e8q"); null when it is not legal here.</summary>
    public Move? ParseUci(string uci)
    {
        if (uci.Length is not (4 or 5)) return null;
        int from, to;
        try
        {
            from = ParseSquare(uci[..2]);
            to = ParseSquare(uci[2..4]);
        }
        catch (FormatException)
        {
            return null;
        }
        var promotion = uci.Length == 5 ? char.ToUpperInvariant(uci[4]) : '\0';
        foreach (var move in LegalMoves())
            if (move.From == from && move.To == to && move.Promotion == promotion)
                return move;
        return null;
    }
}
