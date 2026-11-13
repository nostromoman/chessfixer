using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Client.Services;

/// <summary>The API refused a request. <see cref="Code"/> is null when the server gave no error code.</summary>
public sealed class ApiClientException(ApiErrorCode? code, string message) : Exception(message)
{
    public ApiErrorCode? Code { get; } = code;
}

/// <summary>Typed wrapper around the ChessFixer Web API.</summary>
public class TournamentApi(HttpClient http, ServerAuthenticationStateProvider auth)
{
    // ----- Home page -----

    public Task<DashboardDto> GetDashboardAsync() =>
        SendAsync<DashboardDto>(HttpMethod.Get, "api/dashboard");

    // ----- Tournaments -----

    public Task<List<TournamentSummaryDto>> GetTournamentsAsync() =>
        SendAsync<List<TournamentSummaryDto>>(HttpMethod.Get, "api/tournaments");

    public Task<TournamentDto> GetTournamentAsync(int id) =>
        SendAsync<TournamentDto>(HttpMethod.Get, $"api/tournaments/{id}");

    public Task<TournamentDto> CreateTournamentAsync(CreateTournamentRequest request) =>
        SendAsync<TournamentDto>(HttpMethod.Post, "api/tournaments", request);

    public Task<TournamentDto> UpdateTournamentAsync(int id, UpdateTournamentRequest request) =>
        SendAsync<TournamentDto>(HttpMethod.Put, $"api/tournaments/{id}", request);

    /// <param name="skipRepeatMeetings">Skip the games in which players meet for the third time (or more): both
    /// players are absent for this round.</param>
    public Task<TournamentDto> StartNextRoundAsync(int id, bool skipRepeatMeetings = false) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/rounds/start", new StartRoundRequest(skipRepeatMeetings));

    /// <summary>Cancels the round in progress (its pairing and results are removed).</summary>
    public Task<TournamentDto> CancelRoundAsync(int id) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/rounds/cancel");

    public Task<TournamentDto> CloseAsync(int id) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/close");

    /// <summary>Has the AI (re)write the report of a closed tournament, in the background.</summary>
    public Task<TournamentDto> GenerateReportAsync(int id, GenerateReportRequest request) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/report", request);

    /// <summary>The prompt that was sent to the AI for the current report.</summary>
    /// <summary>Has the AI (re)write the report of a finished round, in the background.</summary>
    public Task<TournamentDto> GenerateRoundReportAsync(int id, int round, GenerateReportRequest request) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/rounds/{round}/report", request);

    /// <summary>The prompt that was sent to the AI for the current report of a round.</summary>
    public Task<ReportPromptDto> GetRoundReportPromptAsync(int id, int round) =>
        SendAsync<ReportPromptDto>(HttpMethod.Get, $"api/tournaments/{id}/rounds/{round}/report/prompt");

    public Task<ReportPromptDto> GetReportPromptAsync(int id) =>
        SendAsync<ReportPromptDto>(HttpMethod.Get, $"api/tournaments/{id}/report/prompt");

    // ----- Players in a tournament -----

    public Task<List<TournamentPlayerDto>> GetTournamentPlayersAsync(int id) =>
        SendAsync<List<TournamentPlayerDto>>(HttpMethod.Get, $"api/tournaments/{id}/players");

    public Task<TournamentDto> AddPlayerAsync(int id, int playerId) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/players", new AddTournamentPlayerRequest(playerId));

    public Task<TournamentDto> RemovePlayerAsync(int id, int playerId) =>
        SendAsync<TournamentDto>(HttpMethod.Delete, $"api/tournaments/{id}/players/{playerId}");

    public Task<TournamentDto> SetPresenceAsync(int id, int playerId, bool isPresent) =>
        SendAsync<TournamentDto>(HttpMethod.Put, $"api/tournaments/{id}/players/{playerId}/presence", new SetPresenceRequest(isPresent));

    // ----- Rounds, results, standings -----

    public Task<List<RoundDto>> GetRoundsAsync(int id) =>
        SendAsync<List<RoundDto>>(HttpMethod.Get, $"api/tournaments/{id}/rounds");

    public Task<TournamentDto> SetResultAsync(int id, int gameId, GameResult result) =>
        SendAsync<TournamentDto>(HttpMethod.Put, $"api/tournaments/{id}/games/{gameId}/result", new SetResultRequest(result));

    public Task<GamePgnDto> GetPgnAsync(int id, int gameId) =>
        SendAsync<GamePgnDto>(HttpMethod.Get, $"api/tournaments/{id}/games/{gameId}/pgn");

    /// <summary>Adds or replaces the moves of a played game; an empty text removes them.</summary>
    public Task<TournamentDto> SetPgnAsync(int id, int gameId, string? pgn) =>
        SendAsync<TournamentDto>(HttpMethod.Put, $"api/tournaments/{id}/games/{gameId}/pgn", new SetPgnRequest(pgn));

    // ----- Games with moves and their analysis -----

    public Task<List<AnalysedGameSummaryDto>> GetAnalysedGamesAsync() =>
        SendAsync<List<AnalysedGameSummaryDto>>(HttpMethod.Get, "api/games");

    public Task<List<PlayerAnalysedGameDto>> GetPlayerGamesAsync(int playerId) =>
        SendAsync<List<PlayerAnalysedGameDto>>(HttpMethod.Get, $"api/players/{playerId}/games");

    public Task<GameAnalysisDto> GetGameAnalysisAsync(int gameId) =>
        SendAsync<GameAnalysisDto>(HttpMethod.Get, $"api/games/{gameId}");

    public Task<GameAnalysisDto> AnalyseGameAsync(int gameId) =>
        SendAsync<GameAnalysisDto>(HttpMethod.Post, $"api/games/{gameId}/analyse");

    public Task<List<StandingDto>> GetStandingsAsync(int id) =>
        SendAsync<List<StandingDto>>(HttpMethod.Get, $"api/tournaments/{id}/standings");

    /// <summary>Competition: everyone against everyone, with the games played and still to play.</summary>
    public Task<CompetitionScheduleDto> GetScheduleAsync(int id) =>
        SendAsync<CompetitionScheduleDto>(HttpMethod.Get, $"api/tournaments/{id}/schedule");

    /// <summary>Competition: enters a game played outside a club evening.</summary>
    public Task<TournamentDto> AddCatchUpGameAsync(int id, CatchUpGameRequest request) =>
        SendAsync<TournamentDto>(HttpMethod.Post, $"api/tournaments/{id}/catch-up-games", request);

    // ----- Application settings -----

    public Task<TournamentDefaultsDto> GetTournamentDefaultsAsync() =>
        SendAsync<TournamentDefaultsDto>(HttpMethod.Get, "api/settings/tournament-defaults");

    public Task<TournamentDefaultsDto> UpdateTournamentDefaultsAsync(TournamentDefaultsDto request) =>
        SendAsync<TournamentDefaultsDto>(HttpMethod.Put, "api/settings/tournament-defaults", request);

    public Task<AiSettingsDto> GetAiSettingsAsync() =>
        SendAsync<AiSettingsDto>(HttpMethod.Get, "api/settings/ai");

    public Task<AiSettingsDto> UpdateAiSettingsAsync(UpdateAiSettingsRequest request) =>
        SendAsync<AiSettingsDto>(HttpMethod.Put, "api/settings/ai", request);

    /// <summary>Tests the settings without saving them.</summary>
    public Task<AiConnectionTestResult> TestAiSettingsAsync(UpdateAiSettingsRequest request) =>
        SendAsync<AiConnectionTestResult>(HttpMethod.Post, "api/settings/ai/test", request);

    public Task<List<WritingStyleDto>> GetWritingStylesAsync() =>
        SendAsync<List<WritingStyleDto>>(HttpMethod.Get, "api/settings/writing-styles");

    public Task<WritingStyleDto> CreateWritingStyleAsync(SaveWritingStyleRequest request) =>
        SendAsync<WritingStyleDto>(HttpMethod.Post, "api/settings/writing-styles", request);

    public Task<WritingStyleDto> UpdateWritingStyleAsync(int id, SaveWritingStyleRequest request) =>
        SendAsync<WritingStyleDto>(HttpMethod.Put, $"api/settings/writing-styles/{id}", request);

    public Task DeleteWritingStyleAsync(int id) =>
        SendCoreAsync(HttpMethod.Delete, $"api/settings/writing-styles/{id}");

    // ----- Central player list -----

    /// <param name="includeDeleted">Also return deleted players, e.g. to show them in the tournaments they played.</param>
    public Task<List<PlayerDto>> GetPlayersAsync(bool includeDeleted = false) =>
        SendAsync<List<PlayerDto>>(HttpMethod.Get, includeDeleted ? "api/players?includeDeleted=true" : "api/players");

    public Task<PlayerRatingDto> GetPlayerRatingAsync(int id) =>
        SendAsync<PlayerRatingDto>(HttpMethod.Get, $"api/players/{id}/rating");

    public Task<PlayerStatisticsDto> GetPlayerStatisticsAsync(int id) =>
        SendAsync<PlayerStatisticsDto>(HttpMethod.Get, $"api/players/{id}/statistics");

    public Task<PlayerDto> CreatePlayerAsync(CreatePlayerRequest request) =>
        SendAsync<PlayerDto>(HttpMethod.Post, "api/players", request);

    public Task<PlayerDto> UpdatePlayerAsync(int id, UpdatePlayerRequest request) =>
        SendAsync<PlayerDto>(HttpMethod.Put, $"api/players/{id}", request);

    public Task DeletePlayerAsync(int id) =>
        SendCoreAsync(HttpMethod.Delete, $"api/players/{id}");

    public Task<PlayerDto> SetPlayerPhotoAsync(int id, byte[] image, string contentType)
    {
        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new(contentType);
        return SendAsync<PlayerDto>(HttpMethod.Put, $"api/players/{id}/photo", content);
    }

    public Task<PlayerDto> DeletePlayerPhotoAsync(int id) =>
        SendAsync<PlayerDto>(HttpMethod.Delete, $"api/players/{id}/photo");

    // ----- Clubs -----

    public Task<List<ClubDto>> GetClubsAsync() =>
        SendAsync<List<ClubDto>>(HttpMethod.Get, "api/clubs");

    public Task<ClubDto> CreateClubAsync(SaveClubRequest request) =>
        SendAsync<ClubDto>(HttpMethod.Post, "api/clubs", request);

    public Task<ClubDto> UpdateClubAsync(int id, SaveClubRequest request) =>
        SendAsync<ClubDto>(HttpMethod.Put, $"api/clubs/{id}", request);

    public Task DeleteClubAsync(int id) =>
        SendCoreAsync(HttpMethod.Delete, $"api/clubs/{id}");

    // ----- Ratings -----

    public Task<List<RatingListEntryDto>> GetRatingListAsync() =>
        SendAsync<List<RatingListEntryDto>>(HttpMethod.Get, "api/ratings");

    public Task<RatingRulesDto> GetRatingRulesAsync() =>
        SendAsync<RatingRulesDto>(HttpMethod.Get, "api/ratings/rules");

    public Task<RatingCalculationDto> CalculateRatingAsync(int whiteRating, int blackRating, GameResult result,
        bool whiteJunior, bool blackJunior, bool whiteWasMaster, bool blackWasMaster) =>
        SendAsync<RatingCalculationDto>(HttpMethod.Get,
            $"api/ratings/calculate?whiteRating={whiteRating}&blackRating={blackRating}&result={result}" +
            $"&whiteJunior={whiteJunior}&blackJunior={blackJunior}&whiteWasMaster={whiteWasMaster}&blackWasMaster={blackWasMaster}");

    public Task<RatingLogPageDto> GetRatingLogAsync(int skip, int take, int? playerId = null) =>
        SendAsync<RatingLogPageDto>(HttpMethod.Get,
            $"api/ratings/log?skip={skip}&take={take}" + (playerId is { } id ? $"&playerId={id}" : ""));

    // ----- Accounts -----

    public Task ChangePasswordAsync(string currentPassword, string newPassword) =>
        SendCoreAsync(HttpMethod.Put, "api/auth/password", new ChangePasswordRequest(currentPassword, newPassword));

    /// <summary>Beheer, Development: resets the database (players and clubs seeded); only for the system administrator.</summary>
    public Task<DevelopmentResetDto> ResetDatabaseAsync() =>
        SendAsync<DevelopmentResetDto>(HttpMethod.Post, "api/development/reset");

    /// <summary>Tournament leader accounts; only for the system administrator.</summary>
    public Task<List<LeaderDto>> GetLeadersAsync() =>
        SendAsync<List<LeaderDto>>(HttpMethod.Get, "api/leaders");

    public Task<LeaderDto> CreateLeaderAsync(CreateLeaderRequest request) =>
        SendAsync<LeaderDto>(HttpMethod.Post, "api/leaders", request);

    public Task<LeaderDto> UpdateLeaderAsync(int id, UpdateLeaderRequest request) =>
        SendAsync<LeaderDto>(HttpMethod.Put, $"api/leaders/{id}", request);

    public Task DeleteLeaderAsync(int id) =>
        SendCoreAsync(HttpMethod.Delete, $"api/leaders/{id}");

    // ----- Activity log (system administrator only) -----

    public Task<ActivityLogPageDto> GetActivityAsync(int skip, int take, int? userId = null, bool problemsOnly = false) =>
        SendAsync<ActivityLogPageDto>(HttpMethod.Get,
            $"api/activity?skip={skip}&take={take}&problemsOnly={problemsOnly}" + (userId is { } id ? $"&userId={id}" : ""));

    public Task<List<ActivityUserDto>> GetActivityUsersAsync() =>
        SendAsync<List<ActivityUserDto>>(HttpMethod.Get, "api/activity/users");

    /// <summary>Reverts what the action of the log entry changed.</summary>
    public Task RevertActivityAsync(long entryId) =>
        SendCoreAsync(HttpMethod.Post, $"api/activity/{entryId}/revert");

    // ----- Plumbing -----

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, body);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>The body of an error answer; Code is null when the server sent no <see cref="ApiError"/>.</summary>
    private sealed record ErrorBody(ApiErrorCode? Code);

    /// <summary>Sends the request and throws <see cref="ApiClientException"/> when it fails.
    /// The body is sent as JSON, unless it is already <see cref="HttpContent"/>.
    /// A 401 means the session ended; the user is then sent to the login page.</summary>
    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, body.GetType())
        };

        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            // Not ApiError: an answer without a code (a crash, an unknown route) must not read as the first code.
            ErrorBody? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ErrorBody>(); }
            catch { /* no ApiError body */ }
            var status = response.StatusCode;
            response.Dispose();
            // Not logged in (anymore): the app switches to the login page.
            if (status == HttpStatusCode.Unauthorized) auth.OnSessionEnded();
            throw new ApiClientException(error?.Code, $"{method} {url} failed with {(int)response.StatusCode}");
        }

        return response;
    }
}
