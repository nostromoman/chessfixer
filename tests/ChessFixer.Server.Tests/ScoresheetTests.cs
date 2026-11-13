using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChessFixer.Core.Chess;
using ChessFixer.Server.Data;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessFixer.Server.Tests;

public class ScoresheetTests : ApiTestBase
{
    /// <summary>Stands in for Claude: returns the prepared reading, and answers repair questions from a list.</summary>
    private sealed class FakeReader : IScoresheetReader
    {
        public List<ReadMove> Moves { get; set; } = [];
        public Dictionary<(int, bool), string> Repairs { get; } = [];
        public List<RepairQuestion> Asked { get; } = [];
        public string? Context { get; private set; }
        public string? MediaType { get; private set; }

        public Task<ScoresheetReading> ReadAsync(AppSettings settings, byte[] image, string mediaType, string context,
            CancellationToken cancellationToken)
        {
            (Context, MediaType) = (context, mediaType);
            return Task.FromResult(new ScoresheetReading("R. G.", "F.", "5-11", "1-0", Moves, "Zet 3 is slordig."));
        }

        public Task<string?> RepairAsync(AppSettings settings, byte[] image, string mediaType, RepairQuestion question,
            CancellationToken cancellationToken)
        {
            Asked.Add(question);
            return Task.FromResult(Repairs.GetValueOrDefault((question.Number, question.White)));
        }
    }

    private readonly FakeReader _reader = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IScoresheetReader>();
        services.AddSingleton<IScoresheetReader>(_reader);
    }

    private static readonly byte[] Photo = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private async Task UseClaudeAsync() =>
        await PutAsync<AiSettingsDto>("api/settings/ai", new UpdateAiSettingsRequest(
            AiProvider.Claude, new UpdateOllamaSettings("", ""), new UpdateClaudeSettings("claude-opus-5-5", "sk-test")));

    private async Task<(int TournamentId, GameDto Game)> PlayedGameAsync(GameResult result = GameResult.WhiteWins)
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await StartRoundAsync(id);
        var game = Assert.Single((await GetRoundsAsync(id))[0].Games);
        await SetResultAsync(id, game.Id, result);
        return (id, game);
    }

    private Task<HttpResponseMessage> PostPhotoAsync(int id, int gameId, string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(Photo);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return Client.PostAsync($"api/tournaments/{id}/games/{gameId}/scoresheet", content);
    }

    private static ReadMove W(int n, string text, bool certain = true, params string[] alternatives) => new(n, true, text, alternatives, certain);
    private static ReadMove B(int n, string text, bool certain = true, params string[] alternatives) => new(n, false, text, alternatives, certain);

    [Fact]
    public async Task Photo_IsReadIntoPgn_WithDoubtsMarked_AndNothingSaved()
    {
        await UseClaudeAsync();
        var (id, game) = await PlayedGameAsync();
        // Dutch notation; 2. Pf5 is impossible (the alternative Pf3 is taken); 2... "Pg6" fits nothing, the repair says Nc6.
        _reader.Moves = [W(1, "e4"), B(1, "e5"), W(2, "Pf5", false, "Pf3"), B(2, "Pg6"), W(3, "Lb5")];
        _reader.Repairs[(2, false)] = "Nc6";

        var result = await ReadAsync<ScoresheetResultDto>(await PostPhotoAsync(id, game.Id));

        Assert.Equal(5, result.Moves);
        Assert.Null(result.Stop);
        Assert.Equal("Zet 3 is slordig.", result.Remarks);
        Assert.Equal("claude-opus-5-5", result.Model);
        Assert.Equal([(2, true, "Nf3", "Pf5"), (2, false, "Nc6", "Pg6")],
            result.Uncertain.Select(u => (u.MoveNumber, u.White, u.San, u.ReadAs)));

        var question = Assert.Single(_reader.Asked);
        Assert.Equal(["e4", "e5", "Nf3"], question.PlayedSoFar);
        Assert.Contains($"White: {game.White.Name} - Black: {game.Black!.Name}, result 1-0", _reader.Context);
        Assert.Equal("image/jpeg", _reader.MediaType);

        // The tags come from the app, the moves are in SAN with comments on the doubtful ones.
        Assert.Contains($"[White \"{game.White.Name}\"]", result.Pgn);
        Assert.Contains($"[Black \"{game.Black.Name}\"]", result.Pgn);
        Assert.Contains("[Result \"1-0\"]", result.Pgn);
        Assert.Contains("1. e4 e5 2. Nf3 {onzeker, gelezen als: Pf5} 2... Nc6 {onzeker, gelezen als: Pg6} 3. Bb5 1-0",
            result.Pgn.Replace('\n', ' ')); // lines are wrapped at 80 characters
        Assert.All(result.Pgn.Split('\n').Where(line => !line.StartsWith('[')), line => Assert.True(line.Length <= 80));
        Assert.Null(PgnGame.Parse(result.Pgn).Error);

        // Reading does not save anything: the user checks and saves in the dialog.
        Assert.False(Assert.Single((await GetRoundsAsync(id))[0].Games).HasPgn);
    }

    [Fact]
    public async Task UnreadableMove_StopsThere_RestInAComment()
    {
        await UseClaudeAsync();
        var (id, game) = await PlayedGameAsync();
        _reader.Moves = [W(1, "d4"), B(1, "d5"), W(2, "Kxx"), B(2, "Nf6")];

        var result = await ReadAsync<ScoresheetResultDto>(await PostPhotoAsync(id, game.Id));

        Assert.Equal(2, result.Moves);
        Assert.Equal((2, true, "Kxx", 2), (result.Stop!.MoveNumber, result.Stop.White, result.Stop.ReadAs, result.Stop.Unread));
        Assert.Contains("1. d4 d5 {niet gelezen vanaf zet 2: 2. Kxx 2... Nf6} 1-0", result.Pgn.Replace('\n', ' '));
        Assert.Null(PgnGame.Parse(result.Pgn).Error);
    }

    [Fact]
    public async Task NeedsClaude_ValidImage_PlayedGame()
    {
        var (id, game) = await PlayedGameAsync();
        _reader.Moves = [W(1, "e4")];

        // Default AI provider is Ollama.
        await AssertErrorAsync(await PostPhotoAsync(id, game.Id), HttpStatusCode.Conflict, ApiErrorCode.ScoresheetNeedsClaude);

        await UseClaudeAsync();
        await AssertErrorAsync(await PostPhotoAsync(id, game.Id, "text/plain"), HttpStatusCode.BadRequest, ApiErrorCode.InvalidScoresheetImage);

        var (forfeitId, forfeit) = await PlayedGameAsync(GameResult.WhiteWinsByForfeit);
        await AssertErrorAsync(await PostPhotoAsync(forfeitId, forfeit.Id), HttpStatusCode.Conflict, ApiErrorCode.PgnNotAllowed);
    }

    [Fact]
    public async Task NoMovesFound_IsAnErrorWithDetail()
    {
        await UseClaudeAsync();
        var (id, game) = await PlayedGameAsync();
        _reader.Moves = [];

        var response = await PostPhotoAsync(id, game.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ApiErrorCode.ScoresheetFailed, error!.Code);
        Assert.Equal("Er zijn geen zetten gevonden op de foto.", error.Detail);
    }
}
