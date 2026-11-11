using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Clubs;

public static class ClubRules
{
    public const int MaxNameLength = 100;
    public const int MaxCityLength = 100;
    public const int MaxDescriptionLength = 2000;
}

/// <summary>The chess clubs: guest players can belong to one, and one club can be marked as the own club.</summary>
public class ClubService(ChessFixerDbContext db)
{
    /// <summary>All clubs: the own club first, then by name.</summary>
    public async Task<List<ClubDto>> GetAllAsync()
    {
        var guests = await db.Players
            .Where(p => p.ClubId != null && p.IsGuest && !p.IsDeleted)
            .GroupBy(p => p.ClubId!.Value)
            .Select(g => new { ClubId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ClubId, g => g.Count);
        var clubs = await db.Clubs.ToListAsync();
        return clubs
            .OrderByDescending(c => c.IsOwnClub).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => ToDto(c, guests.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<ClubDto> CreateAsync(SaveClubRequest request)
    {
        var club = new Club { Name = "" };
        await ApplyAsync(club, request);
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        return ToDto(club, 0);
    }

    public async Task<ClubDto> UpdateAsync(int id, SaveClubRequest request)
    {
        var club = await FindAsync(id);
        await ApplyAsync(club, request);
        await db.SaveChangesAsync();
        return ToDto(club, await db.Players.CountAsync(p => p.ClubId == id && p.IsGuest && !p.IsDeleted));
    }

    /// <summary>Guest players of the club keep playing, without a club.</summary>
    public async Task DeleteAsync(int id)
    {
        db.Clubs.Remove(await FindAsync(id));
        await db.SaveChangesAsync();
    }

    public async Task<Club> FindAsync(int id) =>
        await db.Clubs.FindAsync(id) ?? throw new ApiException(ApiErrorCode.ClubNotFound);

    private async Task ApplyAsync(Club club, SaveClubRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > ClubRules.MaxNameLength) throw new ApiException(ApiErrorCode.ClubNameRequired);

        club.Name = name;
        club.City = Limit(request.City, ClubRules.MaxCityLength);
        club.Description = Limit(request.Description, ClubRules.MaxDescriptionLength);

        // Only one own club: marking this one unmarks the previous one (saved first, for the unique index).
        if (request.IsOwnClub && !club.IsOwnClub)
        {
            var previous = await db.Clubs.Where(c => c.IsOwnClub && c.Id != club.Id).ToListAsync();
            if (previous.Count > 0)
            {
                foreach (var other in previous) other.IsOwnClub = false;
                await db.SaveChangesAsync();
            }
        }
        club.IsOwnClub = request.IsOwnClub;
    }

    private static string? Limit(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static ClubDto ToDto(Club c, int guestPlayers) =>
        new(c.Id, c.Name, c.City, c.Description, c.IsOwnClub, guestPlayers);
}
