using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

/// <summary>A game with moves (PGN) or photos of its scoresheet, for the list of games.</summary>
/// <param name="Date">When the game was played: the start of its round, or when a catch-up game was entered.</param>
/// <param name="Moves">Number of moves (full moves) that could be read.</param>
public record AnalysedGameSummaryDto(
    int GameId,
    int TournamentId,
    string TournamentTitle,
    int Round,
    bool IsCatchUp,
    DateTime? Date,
    GamePlayerDto White,
    GamePlayerDto Black,
    GameResult Result,
    int Moves,
    AnalysisSummaryDto Analysis,
    int Scoresheets = 0);

/// <summary>A game of one player (the Partijen tab of the player drawer), with the analysis when moves were added.</summary>
/// <param name="IsWhite">The player had White.</param>
/// <param name="CanHavePgn">Played over the board (won, lost or drawn): moves can be added.</param>
/// <param name="Analysis">Status None when there are no moves or no analysis yet.</param>
public record PlayerAnalysedGameDto(
    int GameId,
    int TournamentId,
    string TournamentTitle,
    int Round,
    bool IsCatchUp,
    DateTime? Date,
    bool IsWhite,
    GamePlayerDto White,
    GamePlayerDto Black,
    GameResult Result,
    bool HasPgn,
    bool CanHavePgn,
    AnalysisSummaryDto Analysis,
    int Scoresheets = 0);

/// <param name="Status">None when there is no analysis of the current moves.</param>
/// <param name="WhiteAccuracy">0–100; only when the analysis is done.</param>
public record AnalysisSummaryDto(
    AnalysisStatus Status,
    int PositionsDone = 0,
    int PositionsTotal = 0,
    double? WhiteAccuracy = null,
    double? BlackAccuracy = null,
    MoveQualityCountDto? White = null,
    MoveQualityCountDto? Black = null);

public record MoveQualityCountDto(int Inaccuracies, int Mistakes, int Blunders);

/// <summary>A game with its moves and the engine analysis, for the analysis page.</summary>
/// <param name="Pgn">Null when the game has only photos of its scoresheet, no moves (yet).</param>
/// <param name="Scoresheets">The photos of the handwritten scoresheet, oldest first.</param>
/// <param name="StartFen">The position before the first move.</param>
/// <param name="StartEvaluation">The evaluation of the start position, with the best first move.</param>
/// <param name="MoveError">The move where reading the PGN stopped (a typo or an illegal move); null when all moves were read.</param>
public record GameAnalysisDto(
    int GameId,
    int TournamentId,
    string TournamentTitle,
    int Round,
    bool IsCatchUp,
    DateTime? Date,
    GamePlayerDto White,
    GamePlayerDto Black,
    GameResult Result,
    string? Pgn,
    string StartFen,
    IReadOnlyList<PlyDto> Plies,
    PgnMoveErrorDto? MoveError,
    AnalysisSummaryDto Analysis,
    EvaluationDto? StartEvaluation,
    string? Engine,
    int? Depth,
    DateTime? AnalysedAt,
    string? AnalysisError,
    IReadOnlyList<ScoresheetPhotoDto>? Scoresheets = null);

/// <summary>One half-move with the position after it and, once analysed, how good it was.</summary>
/// <param name="Fen">The position after the move.</param>
/// <param name="Evaluation">The evaluation after the move.</param>
/// <param name="BestSan">The engine's best move in the position before this move (what could have been played).</param>
/// <param name="BestEvaluation">The evaluation before the move, i.e. with the best move.</param>
/// <param name="BestLine">The engine's best line from the position before this move, in SAN.</param>
/// <param name="NextLine">The engine's best line from the position after this move, in SAN (what comes next).</param>
public record PlyDto(
    int MoveNumber,
    bool White,
    string San,
    string Uci,
    string Fen,
    EvaluationDto? Evaluation = null,
    MoveQuality? Quality = null,
    double? WinChanceLoss = null,
    string? BestSan = null,
    string? BestUci = null,
    EvaluationDto? BestEvaluation = null,
    IReadOnlyList<string>? BestLine = null,
    IReadOnlyList<string>? NextLine = null);

/// <summary>An engine evaluation from White's point of view.</summary>
/// <param name="Centipawns">+100 is about a pawn up for White.</param>
/// <param name="Mate">Mate in this many moves: positive White mates, negative Black mates, 0 checkmate on the board.</param>
/// <param name="WhiteWinChance">White's chance to win, 0–100.</param>
public record EvaluationDto(int? Centipawns, int? Mate, double WhiteWinChance);

public record PgnMoveErrorDto(int MoveNumber, bool White, string Text);

/// <summary>The moves read from a photo of the handwritten scoresheet, as PGN to check and save.</summary>
/// <param name="Pgn">Tags from the app (event, players, result) and the moves that could be replayed; doubtful
/// moves carry a comment with what was read, unread moves are in a comment at the end.</param>
/// <param name="Moves">Number of half-moves in the PGN.</param>
/// <param name="Uncertain">Moves that could only be read with doubt: check them on the sheet.</param>
/// <param name="Stop">Where reading stopped (no legal move fitted what was written); null when all moves were read.</param>
/// <param name="Remarks">Notes of the reader (Dutch), e.g. about illegible rows; may be empty.</param>
public record ScoresheetResultDto(
    string Pgn,
    int Moves,
    IReadOnlyList<ScoresheetUncertainMoveDto> Uncertain,
    ScoresheetStopDto? Stop,
    string Remarks,
    string Model,
    int ScoresheetId);

/// <summary>A photo of the handwritten scoresheet of a game, kept for the records (the image: GET
/// api/games/{GameId}/scoresheets/{Id}/image).</summary>
/// <param name="UploadedBy">Display name of the user who uploaded it.</param>
/// <param name="Bytes">Size of the image.</param>
public record ScoresheetPhotoDto(int Id, int GameId, DateTime UploadedAt, string? UploadedBy, int Bytes);

public record ScoresheetUncertainMoveDto(int MoveNumber, bool White, string San, string ReadAs);

/// <param name="Unread">Number of half-moves on the sheet from this move on that are not in the PGN.</param>
public record ScoresheetStopDto(int MoveNumber, bool White, string ReadAs, int Unread);
