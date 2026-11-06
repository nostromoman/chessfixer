using ChessFixer.Server.Data;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Controllers;

/// <summary>The central player list, shared across tournaments.</summary>
[ApiController]
[Route("api/players")]
public class PlayersController(ChessFixerDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<PlayerDto>> GetAll() =>
        await db.Players
            .OrderBy(p => p.Name)
            .Select(p => new PlayerDto(p.Id, p.Name, p.Rating))
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<PlayerDto>> Create(CreatePlayerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Naam is verplicht.");

        var player = new Player { Name = request.Name.Trim(), Rating = request.Rating };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return new PlayerDto(player.Id, player.Name, player.Rating);
    }
}
