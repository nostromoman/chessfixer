using ChessFixer.Core.Rating;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services;
using ChessFixer.Server.Services.Ratings;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Controllers;

/// <summary>The central player list, shared across tournaments.</summary>
[ApiController]
[Route("api/players")]
public class PlayersController(ChessFixerDbContext db, TournamentService tournaments, RatingLogService ratingLog) : ControllerBase
{
    public const int MaxPhotoBytes = 1024 * 1024;
    private const int MaxRating = 4000;

    /// <summary>The player list. Deleted players are only included on request (tournament pages still show them).</summary>
    [HttpGet]
    public async Task<List<PlayerDto>> GetAll(bool includeDeleted = false) =>
        await db.Players
            .Where(p => includeDeleted || !p.IsDeleted)
            .OrderBy(p => p.Name)
            .Select(p => new PlayerDto(p.Id, p.FirstName, p.LastName, p.Name, p.Rating, p.BirthDate, p.City,
                p.Photo == null ? (int?)null : p.PhotoVersion, p.IsDeleted,
                p.IsGuest, p.ClubId, p.Club == null ? null : p.Club.Name))
            .ToListAsync();

    /// <summary>Tournament results and games of the player.</summary>
    [HttpGet("{id:int}/statistics")]
    public Task<PlayerStatisticsDto> GetStatistics(int id) => tournaments.GetPlayerStatisticsAsync(id);

    /// <summary>The rating history: every change, and every rated tournament with the change of each game.</summary>
    [HttpGet("{id:int}/rating")]
    public async Task<PlayerRatingDto> GetRating(int id) => await ratingLog.GetPlayerHistoryAsync(await FindAsync(id));

    [HttpPost]
    public async Task<ActionResult<PlayerDto>> Create(CreatePlayerRequest request)
    {
        var player = new Player { FirstName = "" };
        await ApplyAsync(player, request.FirstName, request.LastName, request.Rating ?? EloCalculator.DefaultRating,
            request.BirthDate, request.City, request.IsGuest, request.ClubId);
        db.Players.Add(player);
        await ratingLog.AddAsync(player, player.Rating, RatingChangeReason.NewPlayer);
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    [HttpPut("{id:int}")]
    public async Task<PlayerDto> Update(int id, UpdatePlayerRequest request)
    {
        var player = await FindWithPhotoAsync(id); // the response tells whether the player has a photo
        var before = player.Rating;
        await ApplyAsync(player, request.FirstName, request.LastName, request.Rating ?? player.Rating,
            request.BirthDate, request.City, request.IsGuest, request.ClubId);
        await ratingLog.AddAsync(player, before, RatingChangeReason.Manual);
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    /// <summary>
    /// Soft delete: the player disappears from the player list and cannot join tournaments anymore,
    /// but stays in the tournaments they played, so standings and games of the past remain intact.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var player = await FindAsync(id);
        player.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id)
    {
        var photo = await db.PlayerPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.PlayerId == id);
        if (photo is null) return NotFound();

        // The URL contains the photo version, so the image can be cached for a long time.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(photo.Data, photo.ContentType);
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

        var player = await FindWithPhotoAsync(id);
        player.Photo ??= new PlayerPhoto { Data = [], ContentType = "" };
        player.Photo.Data = buffer.ToArray();
        player.Photo.ContentType = contentType;
        player.PhotoVersion++;
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    [HttpDelete("{id:int}/photo")]
    public async Task<PlayerDto> DeletePhoto(int id)
    {
        var player = await FindWithPhotoAsync(id);
        if (player.Photo is not null) db.PlayerPhotos.Remove(player.Photo);
        player.Photo = null;
        player.PhotoVersion++;
        await db.SaveChangesAsync();
        return ToDto(player);
    }

    private async Task<Player> FindAsync(int id) =>
        await db.Players.FindAsync(id) ?? throw new ApiException(ApiErrorCode.PlayerNotFound);

    private async Task<Player> FindWithPhotoAsync(int id) =>
        await db.Players.Include(p => p.Photo).Include(p => p.Club).SingleOrDefaultAsync(p => p.Id == id)
        ?? throw new ApiException(ApiErrorCode.PlayerNotFound);

    private async Task ApplyAsync(Player player, string firstName, string? lastName, int rating,
        DateOnly? birthDate, string? city, bool isGuest, int? clubId)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ApiException(ApiErrorCode.FirstNameRequired);
        if (rating is < 0 or > MaxRating) throw new ApiException(ApiErrorCode.InvalidRating);

        player.FirstName = firstName.Trim();
        player.LastName = lastName?.Trim() ?? "";
        player.Rating = rating;
        player.BirthDate = birthDate;
        player.City = Normalize(city);
        player.IsGuest = isGuest;
        // Only a guest player has a club: own players belong to the own club.
        player.Club = isGuest && clubId is { } id
            ? await db.Clubs.FindAsync(id) ?? throw new ApiException(ApiErrorCode.ClubNotFound)
            : null;
        player.ClubId = player.Club?.Id;
    }

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static PlayerDto ToDto(Player p) =>
        new(p.Id, p.FirstName, p.LastName, p.Name, p.Rating, p.BirthDate, p.City,
            p.Photo is null ? null : p.PhotoVersion, p.IsDeleted, p.IsGuest, p.ClubId, p.Club?.Name);
}
