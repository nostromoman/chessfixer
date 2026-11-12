using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Reports;

/// <summary>The writing styles a leader can choose from when asking for a report.</summary>
public class WritingStyleService(ChessFixerDbContext db)
{
    public async Task<List<WritingStyleDto>> GetAllAsync()
    {
        var defaultId = await db.AppSettings
            .Where(s => s.Id == AppSettings.SingletonId)
            .Select(s => s.DefaultWritingStyleId)
            .SingleOrDefaultAsync();
        return await db.WritingStyles
            .OrderBy(s => s.Id)
            .Select(s => new WritingStyleDto(s.Id, s.Name, s.Description, s.Id == defaultId))
            .ToListAsync();
    }

    public async Task<WritingStyleDto> CreateAsync(SaveWritingStyleRequest request)
    {
        var style = new WritingStyle { Name = "", Description = "" };
        Apply(style, request);
        db.WritingStyles.Add(style);
        await db.SaveChangesAsync();
        return ToDto(style);
    }

    public async Task<WritingStyleDto> UpdateAsync(int id, SaveWritingStyleRequest request)
    {
        var style = await FindAsync(id);
        Apply(style, request);
        await db.SaveChangesAsync();
        return ToDto(style);
    }

    /// <summary>Reports written with this style keep their copy of it.</summary>
    public async Task DeleteAsync(int id)
    {
        var style = await FindAsync(id);
        // The database would do this as well, but explicitly the activity log sees it (and a revert can restore it).
        foreach (var settings in await db.AppSettings.Where(s => s.DefaultWritingStyleId == id).ToListAsync())
            settings.DefaultWritingStyleId = null;
        db.WritingStyles.Remove(style);
        await db.SaveChangesAsync();
    }

    public async Task<WritingStyle> FindAsync(int id) =>
        await db.WritingStyles.FindAsync(id) ?? throw new ApiException(ApiErrorCode.WritingStyleNotFound);

    private static void Apply(WritingStyle style, SaveWritingStyleRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > WritingStyleRules.MaxNameLength) throw new ApiException(ApiErrorCode.WritingStyleNameRequired);

        var description = request.Description?.Trim() ?? "";
        style.Name = name;
        style.Description = description.Length <= WritingStyleRules.MaxDescriptionLength
            ? description
            : description[..WritingStyleRules.MaxDescriptionLength];
    }

    private static WritingStyleDto ToDto(WritingStyle s) => new(s.Id, s.Name, s.Description);
}
