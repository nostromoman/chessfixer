using ChessFixer.Server.Services;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ChessFixer.Server.Controllers;

[ApiController]
[Route("api/tournaments")]
public class TournamentsController(TournamentService service) : ControllerBase
{
    [HttpGet]
    public Task<List<TournamentSummaryDto>> GetAll() => service.GetAllAsync();

    [HttpGet("{id:int}")]
    public Task<TournamentDto> Get(int id) => service.GetAsync(id);

    [HttpPost]
    public async Task<ActionResult<TournamentDto>> Create(CreateTournamentRequest request)
    {
        var tournament = await service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = tournament.Id }, tournament);
    }

    /// <summary>Changes the title and settings. Both are locked once round 1 has started.</summary>
    [HttpPut("{id:int}")]
    public Task<TournamentDto> Update(int id, UpdateTournamentRequest request) => service.UpdateAsync(id, request);

    /// <summary>Starts the next round (which is already paired while waiting). Without a body every game is played.</summary>
    [HttpPost("{id:int}/rounds/start")]
    public Task<TournamentDto> StartNextRound(int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartRoundRequest? request = null) =>
        service.StartNextRoundAsync(id, request?.SkipRepeatMeetings ?? false);

    /// <summary>Cancels the round in progress: its pairing and any results are removed, rating changes are undone.</summary>
    [HttpPost("{id:int}/rounds/cancel")]
    public Task<TournamentDto> CancelRound(int id) => service.CancelRoundAsync(id);

    [HttpPost("{id:int}/close")]
    public Task<TournamentDto> Close(int id) => service.CloseAsync(id);

    /// <summary>Has the AI (re)write the report of a closed tournament, with the noteworthy events and writing style
    /// from the report wizard. The report is written in the background.</summary>
    [HttpPost("{id:int}/report")]
    public Task<TournamentDto> GenerateReport(int id, GenerateReportRequest? request) =>
        service.GenerateReportAsync(id, request ?? new GenerateReportRequest());

    /// <summary>The prompt that was sent to the AI for the current report.</summary>
    [HttpGet("{id:int}/report/prompt")]
    public Task<ReportPromptDto> GetReportPrompt(int id) => service.GetReportPromptAsync(id);

    /// <summary>Has the AI (re)write the report of a finished round, with the events and writing style from the
    /// report wizard. The report is written in the background.</summary>
    [HttpPost("{id:int}/rounds/{number:int}/report")]
    public Task<TournamentDto> GenerateRoundReport(int id, int number, GenerateReportRequest? request) =>
        service.GenerateRoundReportAsync(id, number, request ?? new GenerateReportRequest());

    /// <summary>The prompt that was sent to the AI for the current report of a round.</summary>
    [HttpGet("{id:int}/rounds/{number:int}/report/prompt")]
    public Task<ReportPromptDto> GetRoundReportPrompt(int id, int number) => service.GetRoundReportPromptAsync(id, number);

    // ----- Players in this tournament -----

    [HttpGet("{id:int}/players")]
    public Task<List<TournamentPlayerDto>> GetPlayers(int id) => service.GetPlayersAsync(id);

    [HttpPost("{id:int}/players")]
    public Task<TournamentDto> AddPlayer(int id, AddTournamentPlayerRequest request) =>
        service.AddPlayerAsync(id, request.PlayerId);

    [HttpDelete("{id:int}/players/{playerId:int}")]
    public Task<TournamentDto> RemovePlayer(int id, int playerId) => service.RemovePlayerAsync(id, playerId);

    /// <summary>Marks a player as present or absent for the next round.</summary>
    [HttpPut("{id:int}/players/{playerId:int}/presence")]
    public Task<TournamentDto> SetPresence(int id, int playerId, SetPresenceRequest request) =>
        service.SetPresenceAsync(id, playerId, request.IsPresent);

    // ----- Rounds, results, standings -----

    /// <summary>All rounds; the last one is the pending round when IsStarted is false.</summary>
    [HttpGet("{id:int}/rounds")]
    public Task<List<RoundDto>> GetRounds(int id) => service.GetRoundsAsync(id);

    [HttpPut("{id:int}/games/{gameId:int}/result")]
    public Task<TournamentDto> SetResult(int id, int gameId, SetResultRequest request) =>
        service.SetResultAsync(id, gameId, request.Result);

    /// <summary>Competition: everyone against everyone, with the games played and still to play.</summary>
    [HttpGet("{id:int}/schedule")]
    public Task<CompetitionScheduleDto> GetSchedule(int id) => service.GetScheduleAsync(id);

    /// <summary>Competition: enters a game that was played outside a club evening. Clearing its result
    /// (PUT games/{gameId}/result with NotPlayed) removes it again.</summary>
    [HttpPost("{id:int}/catch-up-games")]
    public Task<TournamentDto> AddCatchUpGame(int id, CatchUpGameRequest request) =>
        service.AddCatchUpGameAsync(id, request);

    [HttpGet("{id:int}/standings")]
    public Task<List<StandingDto>> GetStandings(int id) => service.GetStandingsAsync(id);
}
