using System.Globalization;
using System.Text;
using System.Text.Json;
using ChessFixer.Core.Chess;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Analysis;

/// <summary>
/// Turns a photo of a handwritten scoresheet into PGN: Claude reads the moves, the board checks them (the first
/// legal reading wins; when none is legal Claude is asked which legal move is written), and the result is
/// returned as PGN for the user to check. Nothing is saved: the user saves the PGN in the dialog.
/// </summary>
public class ScoresheetService(
    ChessFixerDbContext db,
    AiSettingsService aiSettings,
    IScoresheetReader reader,
    ILogger<ScoresheetService> logger)
{
    /// <summary>The client scales photos down to about 2000 px; this leaves room for a PNG.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private static readonly string[] MediaTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    public async Task<ScoresheetResultDto> ReadAsync(int tournamentId, int gameId, byte[] image, string? contentType,
        CancellationToken cancellationToken)
    {
        var mediaType = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (mediaType is null || !MediaTypes.Contains(mediaType) || image.Length == 0 || image.Length > MaxImageBytes)
            throw new ApiException(ApiErrorCode.InvalidScoresheetImage);

        var game = await db.Games
                       .Include(g => g.Round).ThenInclude(r => r.Tournament)
                       .Include(g => g.White).ThenInclude(p => p.Player)
                       .Include(g => g.Black!).ThenInclude(p => p.Player)
                       .SingleOrDefaultAsync(g => g.Id == gameId && g.Round.TournamentId == tournamentId, cancellationToken)
                   ?? throw new ApiException(ApiErrorCode.GameNotFound);
        if (game.Black is null || game.Result is not (GameResult.WhiteWins or GameResult.Draw or GameResult.BlackWins))
            throw new ApiException(ApiErrorCode.PgnNotAllowed);

        if (await aiSettings.GetGeneratorAsync() is not { Generator.Provider: AiProvider.Claude } ai)
            throw new ApiException(ApiErrorCode.ScoresheetNeedsClaude);
        var settings = ai.Settings;

        var result = ResultTag(game.Result);
        ScoresheetReading reading;
        ReconstructedGame reconstructed;
        try
        {
            reading = await reader.ReadAsync(settings, image, mediaType,
                $"White: {game.White.Player.Name} - Black: {game.Black.Player.Name}, result {result}", cancellationToken);
            if (reading.Moves.Count == 0)
                throw new ApiException(ApiErrorCode.ScoresheetFailed, "Er zijn geen zetten gevonden op de foto.");

            var ordered = reading.Moves.OrderBy(m => m.Number).ThenBy(m => m.White ? 0 : 1).ToList();
            reconstructed = await ScoresheetReconstruction.ReconstructAsync(ordered,
                question => reader.RepairAsync(settings, image, mediaType, question, cancellationToken));
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            logger.LogWarning(e, "Reading the scoresheet of game {GameId} failed.", gameId);
            throw new ApiException(ApiErrorCode.ScoresheetFailed,
                e is JsonException ? "Het antwoord van Claude was onleesbaar." : e.Message);
        }

        return new ScoresheetResultDto(
            BuildPgn(game, reconstructed, result),
            reconstructed.Moves.Count,
            reconstructed.Uncertain.Select(u => new ScoresheetUncertainMoveDto(u.Number, u.White, u.San, u.ReadAs)).ToList(),
            reconstructed.Stop is { } stop ? new ScoresheetStopDto(stop.Number, stop.White, stop.ReadAs, stop.Unread.Count) : null,
            reading.Remarks.Trim(),
            settings.ClaudeModel.Trim());
    }

    private static string ResultTag(GameResult result) => result switch
    {
        GameResult.WhiteWins => "1-0",
        GameResult.BlackWins => "0-1",
        GameResult.Draw => "1/2-1/2",
        _ => "*"
    };

    /// <summary>
    /// The tags come from the app (not from the sheet: names there are often abbreviated); the moves are the ones
    /// that could be replayed. A doubtful move gets a comment with what was read, so the user can check it; the
    /// moves that could not be read are kept in a comment at the end.
    /// </summary>
    public static string BuildPgn(Game game, ReconstructedGame reconstructed, string result)
    {
        var pgn = new StringBuilder();
        void Tag(string name, string value) =>
            pgn.Append('[').Append(name).Append(" \"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"]\n");

        var date = game.IsCatchUp ? game.PlayedAt : game.Round.StartedAt;
        Tag("Event", game.Round.Tournament.Title);
        Tag("Date", date?.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) ?? "????.??.??");
        Tag("Round", game.IsCatchUp ? "-" : game.Round.Number.ToString(CultureInfo.InvariantCulture));
        Tag("White", game.White.Player.Name);
        Tag("Black", game.Black!.Player.Name);
        Tag("Result", result);
        Tag("Annotator", "Gelezen van een foto van het notatieformulier");
        pgn.Append('\n');

        var doubtful = reconstructed.Uncertain.ToDictionary(u => (u.Number, u.White), u => u.ReadAs);
        var tokens = new List<string>();
        var afterComment = false;
        for (var i = 0; i < reconstructed.Moves.Count; i++)
        {
            var (number, white) = (i / 2 + 1, i % 2 == 0);
            if (white) tokens.Add($"{number}.");
            else if (afterComment) tokens.Add($"{number}...");
            tokens.Add(reconstructed.Moves[i]);
            afterComment = doubtful.TryGetValue((number, white), out var readAs);
            if (afterComment) tokens.Add($"{{onzeker, gelezen als: {Comment(readAs!)}}}");
        }
        if (reconstructed.Stop is { } stop)
            tokens.Add($"{{niet gelezen vanaf zet {stop.Number}{(stop.White ? "" : " van zwart")}: {Comment(string.Join(" ", stop.Unread))}}}");
        tokens.Add(result);

        // Lines of at most 80 characters, as is usual in PGN.
        var line = new StringBuilder();
        foreach (var token in tokens)
        {
            if (line.Length > 0 && line.Length + 1 + token.Length > 80)
            {
                pgn.Append(line).Append('\n');
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(token);
        }
        return pgn.Append(line).Append('\n').ToString();
    }

    private static string Comment(string text) => text.Replace('{', '(').Replace('}', ')');
}
