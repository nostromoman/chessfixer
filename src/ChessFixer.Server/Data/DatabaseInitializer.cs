using System.Security.Cryptography;
using System.Text;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Data;

/// <summary>
/// Development approach: no migrations. When the model changes, the database is dropped and recreated.
/// A new database is seeded with the system administrator, a tournament leader and (unless disabled with
/// the setting <c>Seed:DemoPlayers = false</c>) a few players.
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
            Seed(db,
                scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("Seed:DemoPlayers", true));
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Development accounts. Change these passwords before using the application for real.</summary>
    private static void Seed(ChessFixerDbContext db, IPasswordHasher<AppUser> hasher, bool demoPlayers)
    {
        AddUser("admin", "Systeembeheerder", "HelloWorld1!", UserRole.Admin);
        AddUser("roel", "Roel Gielen", "Appelsap1!", UserRole.TournamentLeader);

        if (demoPlayers)
        {
            db.Players.AddRange(
                new Player { FirstName = "Roel", LastName = "Gielen", BirthDate = new DateOnly(1982, 1, 15), Rating = 1400 },
                new Player { FirstName = "Bram", LastName = "Gielen", BirthDate = new DateOnly(2014, 6, 17), Rating = 800 });
        }

        void AddUser(string userName, string displayName, string password, UserRole role)
        {
            var user = new AppUser { UserName = userName, DisplayName = displayName, Role = role };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
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
