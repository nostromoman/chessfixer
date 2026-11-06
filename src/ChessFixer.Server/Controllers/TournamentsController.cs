using ChessFixer.Server.Data;
using ChessFixer.Server.Hubs;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Controllers;

[ApiController]
[Route("api/tournaments")]
public class TournamentsController(ChessFixerDbContext db, IHubContext<TournamentHub> hub) : ControllerBase
{
    [HttpGet]
    public async Task<List<TournamentSummaryDto>> GetAll() =>
        await db.Tournaments
            .OrderByDescending(t => t.Id)
            .Select(t => new TournamentSummaryDto(
                t.Id, t.Title, t.State, t.Players.Count(p => p.IsActive), t.Rounds.Count))
            .ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TournamentDto>> Get(int id)
    {
        var tournament = await db.Tournaments.Include(t => t.Rounds).FirstOrDefaultAsync(t => t.Id == id);
        return tournament is null ? NotFound() : ToDto(tournament);
    }

    [HttpPost]
    public async Task<ActionResult<TournamentDto>> Create(CreateTournamentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Titel is verplicht.");

        var tournament = new Tournament { Title = request.Title.Trim() };
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();

        await hub.Clients.All.SendAsync(TournamentHubContract.TournamentListChanged);
        return CreatedAtAction(nameof(Get), new { id = tournament.Id }, ToDto(tournament));
    }

    private static TournamentDto ToDto(Tournament t) => new(
        t.Id,
        t.Title,
        t.State,
        new TournamentSettingsDto(
            t.PairingSystem, t.GameTimeMinutes, t.IncrementSeconds,
            t.PointsForWin, t.PointsForDraw, t.PointsForLoss, t.PointsForBye),
        t.Rounds.Count);
}
