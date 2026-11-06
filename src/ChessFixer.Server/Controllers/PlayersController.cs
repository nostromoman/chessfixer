using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
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
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ApiException(ApiErrorCode.NameRequired);

        var player = new Player { Name = request.Name.Trim(), Rating = request.Rating };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return new PlayerDto(player.Id, player.Name, player.Rating);
    }

    [HttpPut("{id:int}")]
    public async Task<PlayerDto> Update(int id, UpdatePlayerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ApiException(ApiErrorCode.NameRequired);

        var player = await db.Players.FindAsync(id) ?? throw new ApiException(ApiErrorCode.PlayerNotFound);
        player.Name = request.Name.Trim();
        player.Rating = request.Rating;
        await db.SaveChangesAsync();
        return new PlayerDto(player.Id, player.Name, player.Rating);
    }
}
