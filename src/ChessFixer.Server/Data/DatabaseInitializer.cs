using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Data;

/// <summary>
/// Development approach: no migrations. When the model changes, the database is dropped and recreated.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChessFixerDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ChessFixerDbContext>>();

        var currentHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(db.Database.GenerateCreateScript())));

        if (await GetStoredHashAsync(db) != currentHash)
        {
            logger.LogWarning("Database schema changed or missing; recreating the database. All data is lost.");
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.SchemaInfo.Add(new SchemaInfo { Hash = currentHash });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<string?> GetStoredHashAsync(ChessFixerDbContext db)
    {
        try
        {
            var tableExists = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'SchemaInfo'")
                .SingleAsync() > 0;
            if (!tableExists) return null;

            return await db.SchemaInfo.OrderBy(s => s.Id).Select(s => s.Hash).FirstOrDefaultAsync();
        }
        catch (Exception)
        {
            // Database or table does not exist, or has an old structure.
            return null;
        }
    }
}
