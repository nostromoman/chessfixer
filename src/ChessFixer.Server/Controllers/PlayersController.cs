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
    public const int MaxPhotoBytes = 1024 * 1024;

    [HttpGet]
    public async Task<List<PlayerDto>> GetAll() =>
        await db.Players
            .OrderBy(p => p.Name)
            .Select(p => new PlayerDto(p.Id, p.FirstName, p.LastName, p.Name, p.Rating, p.BirthDate, p.City, p.Club,
                p.Photo == null ? null : p.PhotoVersion))
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<PlayerDto>> Create(CreatePlayerRequest request)
    {
        var player = new Player { FirstName = "" };
        Apply(player, request.FirstName, request.LastName, request.Rating, request.BirthDate, request.City, request.Club);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    [HttpPut("{id:int}")]
    public async Task<PlayerDto> Update(int id, UpdatePlayerRequest request)
    {
        var player = await FindAsync(id);
        Apply(player, request.FirstName, request.LastName, request.Rating, request.BirthDate, request.City, request.Club);
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    /// <summary>Deletes a player. Players that are (or were) in a tournament cannot be deleted.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var player = await FindAsync(id);
        if (await db.TournamentPlayers.AnyAsync(tp => tp.PlayerId == id))
            throw new ApiException(ApiErrorCode.PlayerInTournament);

        db.Players.Remove(player);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id)
    {
        var photo = await db.Players
            .Where(p => p.Id == id && p.Photo != null)
            .Select(p => new { p.Photo, p.PhotoContentType })
            .SingleOrDefaultAsync();
        if (photo is null) return NotFound();

        // The URL contains the photo version, so the image can be cached for a long time.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(photo.Photo!, photo.PhotoContentType ?? "image/jpeg");
    }

    /// <summary>Replaces the profile photo. The body is the raw image (max 1 MB).</summary>
    [HttpPut("{id:int}/photo")]
    [RequestSizeLimit(MaxPhotoBytes + 1024)]
    public async Task<PlayerDto> SetPhoto(int id)
    {
        var contentType = Request.ContentType;
        if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(ApiErrorCode.InvalidPhoto);

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        if (buffer.Length == 0 || buffer.Length > MaxPhotoBytes) throw new ApiException(ApiErrorCode.InvalidPhoto);

        var player = await FindAsync(id);
        player.Photo = buffer.ToArray();
        player.PhotoContentType = contentType;
        player.PhotoVersion++;
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    [HttpDelete("{id:int}/photo")]
    public async Task<PlayerDto> DeletePhoto(int id)
    {
        var player = await FindAsync(id);
        player.Photo = null;
        player.PhotoContentType = null;
        player.PhotoVersion++;
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    private async Task<Player> FindAsync(int id) =>
        await db.Players.FindAsync(id) ?? throw new ApiException(ApiErrorCode.PlayerNotFound);

    private static void Apply(Player player, string firstName, string? lastName, int? rating,
        DateOnly? birthDate, string? city, string? club)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ApiException(ApiErrorCode.FirstNameRequired);

        player.FirstName = firstName.Trim();
        player.LastName = lastName?.Trim() ?? "";
        player.Rating = rating;
        player.BirthDate = birthDate;
        player.City = Normalize(city);
        player.Club = Normalize(club);
    }

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static PlayerDto ToDto(Player p) =>
        new(p.Id, p.FirstName, p.LastName, p.Name, p.Rating, p.BirthDate, p.City, p.Club,
            p.Photo is null ? null : p.PhotoVersion);
}
