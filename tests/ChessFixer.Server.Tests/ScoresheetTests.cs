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

    private Task<HttpResponseMessage> PostPhotoAsync(int gameId, string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(Photo);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return Client.PostAsync($"api/games/{gameId}/scoresheets", content);
    }

    private async Task<ScoresheetPhotoDto> UploadAsync(int gameId) => await ReadAsync<ScoresheetPhotoDto>(await PostPhotoAsync(gameId));

    private Task<HttpResponseMessage> PostReadAsync(int gameId, int photoId) =>
        Client.PostAsync($"api/games/{gameId}/scoresheets/{photoId}/read", null);

    private async Task<ScoresheetWizardDto> ReadPhotoAsync(int gameId, int photoId) =>
        await ReadAsync<ScoresheetWizardDto>(await PostReadAsync(gameId, photoId));

    private async Task<ScoresheetWizardDto?> GetWizardAsync(int gameId)
    {
        var response = await Client.GetAsync($"api/games/{gameId}/scoresheet-wizard");
        return response.StatusCode == HttpStatusCode.NoContent ? null : await ReadAsync<ScoresheetWizardDto>(response);
    }

    private static ReadMove W(int n, string text, MoveConfidence confidence = MoveConfidence.High, params string[] alternatives) =>
        new(n, true, text, alternatives, confidence);
    private static ReadMove B(int n, string text, MoveConfidence confidence = MoveConfidence.High, params string[] alternatives) =>
        new(n, false, text, alternatives, confidence);

    [Fact]
    public async Task Photo_IsRead_IntoMovesToCheck_WithTheConfidence_AndNothingSaved()
    {
        await UseClaudeAsync();
        var (id, game) = await PlayedGameAsync();
        // Dutch notation; 2. Pf5 is impossible (the alternative Pf3 is taken); 2... "Pg6" fits nothing, the repair says Nc6.
        _reader.Moves = [W(1, "e4"), B(1, "e5"), W(2, "Pf5", MoveConfidence.High, "Pf3"), B(2, "Pg6"), W(3, "Lb5", MoveConfidence.Medium)];
        _reader.Repairs[(2, false)] = "Nc6";
        var photo = await UploadAsync(game.Id);

        var wizard = await ReadPhotoAsync(game.Id, photo.Id);

        Assert.Equal((photo.Id, ScoresheetReadStatus.Done, "claude-opus-5-5", "Zet 3 is slordig."),
            (wizard.PhotoId, wizard.ReadStatus, wizard.Model, wizard.Remarks));
        // Only what Claude was sure of, and is what was read, is checked in advance.
        Assert.Equal(
        [
            ("e4", "e4", MoveConfidence.High, true),
            ("e5", "e5", MoveConfidence.High, true),
            ("Nf3", "Pf5", MoveConfidence.Medium, false), // another reading was taken
            ("Nc6", "Pg6", MoveConfidence.Low, false), // repaired
            ("Bb5", "Lb5", MoveConfidence.Medium, false)
        ], wizard.Review!.Moves.Select(m => (m.Text, m.ReadAs, m.Confidence, m.Checked)));
        Assert.Equal(["Pf3"], wizard.Review.Moves[2].Alternatives);

        var question = Assert.Single(_reader.Asked);
        Assert.Equal(["e4", "e5", "Nf3"], question.PlayedSoFar);
        Assert.Contains($"White: {game.White.Name} - Black: {game.Black!.Name}, result 1-0", _reader.Context);
        Assert.Equal("image/jpeg", _reader.MediaType);

        // The wizard is kept, to continue later; nothing is saved as moves yet.
        var kept = await GetWizardAsync(game.Id);
        Assert.Equal((photo.Id, ScoresheetReadStatus.Done, 5), (kept!.PhotoId, kept.ReadStatus, kept.Review!.Moves.Count));
        Assert.False(Assert.Single((await GetRoundsAsync(id))[0].Games).HasPgn);
    }

    [Fact]
    public async Task UnreadableMove_TheRestIsListedAsRead()
    {
        await UseClaudeAsync();
        var (_, game) = await PlayedGameAsync();
        _reader.Moves = [W(1, "d4"), B(1, "d5"), W(2, "Kxx"), B(2, "Nf6", MoveConfidence.Low)];

        var wizard = await ReadPhotoAsync(game.Id, (await UploadAsync(game.Id)).Id);

        Assert.Equal(["d4", "d5", "Kxx", "Nf6"], wizard.Review!.Moves.Select(m => m.Text));
        Assert.Equal(["d4", "d5", "Kxx", "Nf6"], wizard.Review.Moves.Select(m => m.ReadAs));
        Assert.Equal(MoveConfidence.Low, wizard.Review.Moves[3].Confidence);
    }

    [Fact]
    public async Task NoMovesFound_TheWizardShowsTheFailure()
    {
        await UseClaudeAsync();
        var (_, game) = await PlayedGameAsync();
        _reader.Moves = [];
        var photo = await UploadAsync(game.Id);

        var wizard = await ReadPhotoAsync(game.Id, photo.Id);

        Assert.Equal((ScoresheetReadStatus.Failed, "Er zijn geen zetten gevonden op de foto."), (wizard.ReadStatus, wizard.ReadError));
        Assert.Null(wizard.Review);
        // Kept: to try again or to type the moves.
        Assert.Equal(ScoresheetReadStatus.Failed, (await GetWizardAsync(game.Id))!.ReadStatus);

        // The administrator sees the failure in the activity log (also among the problems), with the reason.
        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var problems = await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity?problemsOnly=true"));
        var entry = Assert.Single(problems.Entries, e => e.Action == "Games.ReadScoresheet");
        Assert.Equal((200, "Lezen mislukt: Er zijn geen zetten gevonden op de foto."), (entry.StatusCode, entry.Error));
    }

    [Fact]
    public async Task RefusedAction_IsLoggedWithItsStatusAndReason()
    {
        var (_, game) = await PlayedGameAsync();
        var photo = await UploadAsync(game.Id);
        await PostReadAsync(game.Id, photo.Id); // Ollama: needs Claude

        using var admin = await CreateClientAsync(AdminUserName, AdminPassword);
        var log = await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity?problemsOnly=true"));
        var entry = Assert.Single(log.Entries, e => e.Action == "Games.ReadScoresheet");
        Assert.Equal((409, "ScoresheetNeedsClaude"), (entry.StatusCode, entry.Error));
    }

    [Fact]
    public async Task ReadingNeedsClaude_ThePhotoIsKeptAnyway()
    {
        var (_, game) = await PlayedGameAsync();
        var photo = await UploadAsync(game.Id);

        // Default AI provider is Ollama.
        await AssertErrorAsync(await PostReadAsync(game.Id, photo.Id), HttpStatusCode.Conflict, ApiErrorCode.ScoresheetNeedsClaude);

        Assert.Null(await GetWizardAsync(game.Id));
        Assert.Single(await GetAsync<List<ScoresheetPhotoDto>>($"api/games/{game.Id}/scoresheets"));
        await AssertErrorAsync(await PostReadAsync(game.Id, photo.Id + 100), HttpStatusCode.NotFound, ApiErrorCode.ScoresheetNotFound);
    }

    [Fact]
    public async Task Review_IsSavedToContinue_NotInTheActivityLog_AndStartOverClosesIt()
    {
        await UseClaudeAsync();
        var (_, game) = await PlayedGameAsync();
        _reader.Moves = [W(1, "e4"), B(1, "e5", MoveConfidence.Low)];
        var photo = await UploadAsync(game.Id);
        await ReadPhotoAsync(game.Id, photo.Id);

        var review = new ScoresheetReviewDto(
        [
            new ScoresheetReviewMoveDto("e4", "e4", [], MoveConfidence.High, true),
            new ScoresheetReviewMoveDto(" c5 ", "e5", ["c5"], MoveConfidence.Low, true),
            new ScoresheetReviewMoveDto("Pf3", "", [], MoveConfidence.Low, false)
        ], Selected: 2);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.PutAsJsonAsync($"api/games/{game.Id}/scoresheets/{photo.Id}/review", review)).StatusCode);

        var kept = (await GetWizardAsync(game.Id))!.Review!;
        Assert.Equal(["e4", "c5", "Pf3"], kept.Moves.Select(m => m.Text));
        Assert.Equal([true, true, false], kept.Moves.Select(m => m.Checked));
        Assert.Equal(2, kept.Selected);

        // Saving while the user works does not flood the activity log; reading is logged.
        using (var admin = await CreateClientAsync(AdminUserName, AdminPassword))
        {
            var log = await ReadAsync<ActivityLogPageDto>(await admin.GetAsync("api/activity?take=100"));
            var actions = log.Entries.Select(e => e.Action).ToList();
            Assert.Contains("Games.ReadScoresheet", actions);
            Assert.DoesNotContain("Games.SaveReview", actions);
        }

        // Starting over closes the wizard; the photo is kept.
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/games/{game.Id}/scoresheet-wizard")).StatusCode);
        Assert.Null(await GetWizardAsync(game.Id));
        Assert.Single(await GetAsync<List<ScoresheetPhotoDto>>($"api/games/{game.Id}/scoresheets"));
        // A late save (e.g. from another tab) does not reopen it.
        await Client.PutAsJsonAsync($"api/games/{game.Id}/scoresheets/{photo.Id}/review", review);
        Assert.Null(await GetWizardAsync(game.Id));
    }

    [Fact]
    public async Task ReadingAnotherPhoto_ReplacesTheWizard()
    {
        await UseClaudeAsync();
        var (_, game) = await PlayedGameAsync();
        _reader.Moves = [W(1, "e4")];
        var first = await UploadAsync(game.Id);
        var second = await UploadAsync(game.Id);

        await ReadPhotoAsync(game.Id, first.Id);
        await ReadPhotoAsync(game.Id, second.Id);

        Assert.Equal(second.Id, (await GetWizardAsync(game.Id))!.PhotoId);
    }

    [Fact]
    public async Task CheckedMoves_BecomePgn_AndSavingThemClosesTheWizard()
    {
        await UseClaudeAsync();
        var (id, game) = await PlayedGameAsync();
        _reader.Moves = [W(1, "e4")];
        await ReadPhotoAsync(game.Id, (await UploadAsync(game.Id)).Id);

        // Dutch letters are accepted and written in standard notation.
        var pgn = (await PostAsync<GamePgnDto>($"api/games/{game.Id}/scoresheet-pgn",
            new ScoresheetPgnRequest(["e4", "e5", "Pf3", "Pc6", "Lb5"]))).Pgn!;
        Assert.Contains($"[White \"{game.White.Name}\"]", pgn);
        Assert.Contains($"[Black \"{game.Black!.Name}\"]", pgn);
        Assert.Contains("[Result \"1-0\"]", pgn);
        Assert.Contains("1. e4 e5 2. Nf3 Nc6 3. Bb5 1-0", pgn);
        Assert.Null(PgnGame.Parse(pgn).Error);

        var response = await Client.PostAsJsonAsync($"api/games/{game.Id}/scoresheet-pgn", new ScoresheetPgnRequest(["e4", "e4"]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal((ApiErrorCode.InvalidPgn, "Zet 1... e4 is niet mogelijk."), (error!.Code, error.Detail));

        // Saving the moves finishes the wizard.
        Assert.NotNull(await GetWizardAsync(game.Id));
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{game.Id}/pgn", new SetPgnRequest(pgn))).StatusCode);
        Assert.Null(await GetWizardAsync(game.Id));
    }

    [Fact]
    public async Task Photo_IsKept_WithoutReading()
    {
        var (_, game) = await PlayedGameAsync();

        var photo = await UploadAsync(game.Id);

        Assert.Equal(LeaderDisplayName, photo.UploadedBy);
        Assert.Equal(Photo.Length, photo.Bytes);
        var image = await Client.GetAsync($"api/games/{game.Id}/scoresheets/{photo.Id}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Photo, await image.Content.ReadAsByteArrayAsync());

        // A game with only a photo (no moves yet) is in the list of games and has an analysis page.
        var listed = Assert.Single(await GetAsync<List<AnalysedGameSummaryDto>>("api/games"));
        Assert.Equal((game.Id, 0, 1), (listed.GameId, listed.Moves, listed.Scoresheets));
        var page = await GetAsync<GameAnalysisDto>($"api/games/{game.Id}");
        Assert.Null(page.Pgn);
        Assert.Empty(page.Plies);
        Assert.Equal(AnalysisStatus.None, page.Analysis.Status);
        Assert.Equal(photo.Id, Assert.Single(page.Scoresheets!).Id);
        Assert.Equal(1, Assert.Single(await GetAsync<List<PlayerAnalysedGameDto>>($"api/players/{game.White.PlayerId}/games")).Scoresheets);
        // Keeping a photo does not start a wizard.
        Assert.Null(await GetWizardAsync(game.Id));
    }

    [Fact]
    public async Task Photos_CanBeDeleted()
    {
        var (_, game) = await PlayedGameAsync();
        var first = await UploadAsync(game.Id);
        var second = await UploadAsync(game.Id);

        var photos = await GetAsync<List<ScoresheetPhotoDto>>($"api/games/{game.Id}/scoresheets");
        Assert.Equal([first.Id, second.Id], photos.Select(p => p.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/games/{game.Id}/scoresheets/{first.Id}")).StatusCode);
        await AssertErrorAsync(await Client.DeleteAsync($"api/games/{game.Id}/scoresheets/{first.Id}"),
            HttpStatusCode.NotFound, ApiErrorCode.ScoresheetNotFound);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"api/games/{game.Id}/scoresheets/{first.Id}/image")).StatusCode);

        // The last photo removed and no moves: the game is no longer in the list.
        await Client.DeleteAsync($"api/games/{game.Id}/scoresheets/{second.Id}");
        Assert.Empty(await GetAsync<List<AnalysedGameSummaryDto>>("api/games"));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"api/games/{game.Id}")).StatusCode);
    }

    [Fact]
    public async Task QrCode_ThePhoneSendsOnePhoto_WithoutLogin()
    {
        var (_, game) = await PlayedGameAsync();
        var link = await PostAsync<ScoresheetLinkDto>($"api/games/{game.Id}/scoresheet-links", null);
        Assert.Equal(ScoresheetLinkStatus.Waiting, link.Status);
        Assert.True(link.ExpiresAt > DateTime.UtcNow.AddMinutes(19) && link.ExpiresAt <= DateTime.UtcNow.AddMinutes(20));
        Assert.True(link.Token.Length >= 32);

        using var phone = await CreateClientAsync(); // not logged in
        var info = await ReadAsync<ScoresheetUploadInfoDto>(await phone.PostAsync($"api/scoresheet-upload/{link.Token}/open", null));
        Assert.Equal((game.White.Name, game.Black!.Name), (info.White, info.Black));
        Assert.Equal(ScoresheetLinkStatus.Opened,
            (await GetAsync<ScoresheetLinkDto>($"api/games/{game.Id}/scoresheet-links/{link.Id}")).Status);

        var content = new ByteArrayContent(Photo);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        Assert.Equal(HttpStatusCode.NoContent, (await phone.PostAsync($"api/scoresheet-upload/{link.Token}", content)).StatusCode);

        var sent = await GetAsync<ScoresheetLinkDto>($"api/games/{game.Id}/scoresheet-links/{link.Id}");
        Assert.Equal(ScoresheetLinkStatus.Uploaded, sent.Status);
        var photo = Assert.Single(await GetAsync<List<ScoresheetPhotoDto>>($"api/games/{game.Id}/scoresheets"));
        Assert.Equal((sent.PhotoId, $"{LeaderDisplayName} (telefoon)"), (photo.Id, photo.UploadedBy));

        // Used: the QR code no longer works.
        await AssertErrorAsync(await phone.PostAsync($"api/scoresheet-upload/{link.Token}/open", null),
            HttpStatusCode.NotFound, ApiErrorCode.UploadLinkInvalid);
        var again = new ByteArrayContent(Photo);
        again.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        await AssertErrorAsync(await phone.PostAsync($"api/scoresheet-upload/{link.Token}", again),
            HttpStatusCode.NotFound, ApiErrorCode.UploadLinkInvalid);
        Assert.Single(await GetAsync<List<ScoresheetPhotoDto>>($"api/games/{game.Id}/scoresheets"));
    }

    [Fact]
    public async Task QrCode_Cancelled_OrReplaced_OrUnknown_DoesNotWork()
    {
        var (_, game) = await PlayedGameAsync();
        var first = await PostAsync<ScoresheetLinkDto>($"api/games/{game.Id}/scoresheet-links", null);
        var second = await PostAsync<ScoresheetLinkDto>($"api/games/{game.Id}/scoresheet-links", null);
        Assert.Equal(ScoresheetLinkStatus.Cancelled,
            (await GetAsync<ScoresheetLinkDto>($"api/games/{game.Id}/scoresheet-links/{first.Id}")).Status);

        var cancelled = await ReadAsync<ScoresheetLinkDto>(await Client.DeleteAsync($"api/games/{game.Id}/scoresheet-links/{second.Id}"));
        Assert.Equal(ScoresheetLinkStatus.Cancelled, cancelled.Status);

        using var phone = await CreateClientAsync();
        foreach (var token in new[] { first.Token, second.Token, "onbekend" })
        {
            await AssertErrorAsync(await phone.PostAsync($"api/scoresheet-upload/{token}/open", null),
                HttpStatusCode.NotFound, ApiErrorCode.UploadLinkInvalid);
        }
        // The rest of the API still needs a login.
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync($"api/games/{game.Id}/scoresheet-links/{first.Id}")).StatusCode);
    }

    [Fact]
    public async Task NoPhotoForAForfeit_OrAnInvalidImage()
    {
        var (_, forfeit) = await PlayedGameAsync(GameResult.WhiteWinsByForfeit);
        await AssertErrorAsync(await PostPhotoAsync(forfeit.Id), HttpStatusCode.Conflict, ApiErrorCode.PgnNotAllowed);
        await AssertErrorAsync(await PostPhotoAsync(forfeit.Id, "text/plain"), HttpStatusCode.BadRequest, ApiErrorCode.InvalidScoresheetImage);
        Assert.Empty(await GetAsync<List<ScoresheetPhotoDto>>($"api/games/{forfeit.Id}/scoresheets"));
    }
}
