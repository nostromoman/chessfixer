using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using ChessFixer.Core.Chess;
using ChessFixer.Server.Data;

namespace ChessFixer.Server.Services.Ai;

/// <summary>What Claude read from the photo of a scoresheet.</summary>
/// <param name="White">Name of White as written on the sheet ("" when not readable).</param>
/// <param name="Remarks">Notes of the reader for the user (Dutch), e.g. about illegible rows.</param>
public sealed record ScoresheetReading(string White, string Black, string Date, string Result, IReadOnlyList<ReadMove> Moves, string Remarks);

/// <summary>Reads the moves from a photo of a handwritten scoresheet.</summary>
public interface IScoresheetReader
{
    /// <param name="context">What the app knows about the game, e.g. "White: Roel - Black: Frank, result 1-0".</param>
    Task<ScoresheetReading> ReadAsync(AppSettings settings, byte[] image, string mediaType, string context,
        CancellationToken cancellationToken);

    /// <summary>Which of the legal moves is written at this move; null when none fits.</summary>
    Task<string?> RepairAsync(AppSettings settings, byte[] image, string mediaType, RepairQuestion question,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads handwritten chess scoresheets from a photo with Claude (vision), and answers the follow-up question
/// which legal move is written where no reading was legal. Ollama is not used for this: local models read
/// handwriting far too poorly.
/// </summary>
public class ClaudeScoresheetReader : IScoresheetReader
{
    private const int MaxTokens = 16000;

    private const string ReadSystemPrompt = """
        You read handwritten chess scoresheets (in Dutch clubs: "notatieformulier") from a photo and transcribe the moves.

        - The sheet has numbered rows; each row has White's move and then Black's move. Long games continue in a
          second column or block (e.g. moves 31-60): read every block, in move order.
        - Players often write Dutch notation: K = king, D = queen (dame), T = rook (toren), L = bishop (loper),
          P = knight (paard); a pawn move has no letter. English letters (K Q R B N) or figurines also occur.
          Write every move in standard English SAN: Pf3 -> Nf3, Dxd4 -> Qxd4, Lb5+ -> Bb5+, 0-0 -> O-O, e8=D -> e8=Q.
        - Captures may be written with "x", ":" or not at all, checks with "+" or not. Transcribe what is written;
          do not add or drop captures or checks by guessing.
        - A move that is crossed out and rewritten: take the move that was meant to stand.
        - When you are not sure what is written, give your best reading in "text", other plausible readings in
          "alternatives" (most likely first) and set "certain" to false. Use the game so far to judge which reading
          is plausible, but transcribe: never replace a clearly written move by a better move.
        - Stop at the last move that is written. Never invent or complete moves; skip empty rows.
        - "number" is the move number of the row, "color" is "white" or "black".
        - Copy the player names, the date and the result when they are on the sheet; otherwise an empty string.
        - "remarks": anything the user should check, in Dutch (e.g. "Zet 18-20 zijn slecht leesbaar."), or "".
        """;

    private const string RepairSystemPrompt = """
        You help transcribe a handwritten chess scoresheet from a photo. The moves before the question have been
        checked on a board. For one move, the reading is not a legal move in the position. Look at the sheet again
        and decide which of the legal moves is written there (in Dutch notation K D T L P = K Q R B N).
        Answer with exactly one move from the list of legal moves, written as in the list, or an empty string when
        none of them matches what is written. Do not choose a move only because it is good.
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, JsonElement> ReadSchema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            white = new { type = "string" },
            black = new { type = "string" },
            date = new { type = "string" },
            result = new { type = "string" },
            moves = new
            {
                type = "array",
                items = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new
                    {
                        number = new { type = "integer" },
                        color = new { type = "string", @enum = new[] { "white", "black" } },
                        text = new { type = "string" },
                        alternatives = new { type = "array", items = new { type = "string" } },
                        certain = new { type = "boolean" }
                    },
                    ["required"] = new[] { "number", "color", "text", "alternatives", "certain" }
                }
            },
            remarks = new { type = "string" }
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "white", "black", "date", "result", "moves", "remarks" })
    };

    private static readonly Dictionary<string, JsonElement> RepairSchema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["properties"] = JsonSerializer.SerializeToElement(new { move = new { type = "string" } }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "move" })
    };

    private sealed record ReadAnswer(string White, string Black, string Date, string Result, List<ReadAnswerMove> Moves, string Remarks);
    private sealed record ReadAnswerMove(int Number, string Color, string Text, List<string>? Alternatives, bool Certain);
    private sealed record RepairAnswer(string Move);

    public async Task<ScoresheetReading> ReadAsync(AppSettings settings, byte[] image, string mediaType, string context,
        CancellationToken cancellationToken)
    {
        var text = await AskAsync(settings, ReadSystemPrompt, image, mediaType,
            $"This is the scoresheet of the game {context}. Transcribe all moves.", ReadSchema, cancellationToken);
        var answer = JsonSerializer.Deserialize<ReadAnswer>(text, Json)
                     ?? throw new InvalidOperationException("Claude gaf een leeg antwoord.");

        var moves = answer.Moves
            .Select(m => new ReadMove(m.Number, m.Color != "black", m.Text, m.Alternatives ?? [], m.Certain))
            .ToList();
        return new ScoresheetReading(answer.White, answer.Black, answer.Date, answer.Result, moves, answer.Remarks);
    }

    public async Task<string?> RepairAsync(AppSettings settings, byte[] image, string mediaType, RepairQuestion question,
        CancellationToken cancellationToken)
    {
        var played = string.Join(" ", question.PlayedSoFar.Select((san, i) => i % 2 == 0 ? $"{i / 2 + 1}. {san}" : san));
        var alternatives = question.Alternatives.Count == 0 ? "" : $" (other readings: {string.Join(", ", question.Alternatives)})";
        var prompt = $"""
            Moves so far (checked): {(played.Length == 0 ? "none" : played)}
            Move {question.Number} of {(question.White ? "White" : "Black")} was read as "{question.ReadAs}"{alternatives}, but that is not legal here.
            Legal moves: {string.Join(", ", question.LegalMoves)}
            Which of these legal moves is written on the sheet?
            """;
        var text = await AskAsync(settings, RepairSystemPrompt, image, mediaType, prompt, RepairSchema, cancellationToken);
        var move = JsonSerializer.Deserialize<RepairAnswer>(text, Json)?.Move?.Trim();
        return string.IsNullOrEmpty(move) ? null : move;
    }

    private static async Task<string> AskAsync(AppSettings settings, string system, byte[] image, string mediaType,
        string question, Dictionary<string, JsonElement> schema, CancellationToken cancellationToken)
    {
        var model = settings.ClaudeModel.Trim();
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = MaxTokens,
            System = system,
            // Reading handwriting is worth thinking about; medium is Claude Opus 5.5's default.
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.High,
                Format = new BetaJsonOutputFormat { Schema = schema }
            },
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaImageBlockParam
                        {
                            Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(image), MediaType = mediaType }
                        },
                        new BetaTextBlockParam { Text = question }
                    }
                }
            ]
        };
        if (ClaudeTextGenerator.ServerSideFallbackModels.Contains(model))
            parameters = parameters with { Betas = [ClaudeTextGenerator.ServerSideFallbackBeta], Fallbacks = new Default() };

        var response = await ClaudeTextGenerator.CallAsync(settings,
            client => client.Beta.Messages.Create(parameters, cancellationToken));

        if (response.StopReason == "refusal")
            throw new InvalidOperationException("Claude weigerde deze foto te lezen.");
        if (response.StopReason == "max_tokens")
            throw new InvalidOperationException("Het antwoord van Claude was te lang.");

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(b => b.Text)).Trim();
        return text.Length > 0 ? text : throw new InvalidOperationException("Claude gaf een leeg antwoord.");
    }
}
