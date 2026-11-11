using ChessFixer.Server.Services.Auth;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Data;

/// <summary>
/// Brings the database up to date with EF Core migrations, so existing data is kept.
/// Before migrating an existing database, a copy is made next to it. A new database only gets the
/// system administrator account.
/// </summary>
public static class DatabaseInitializer
{
    public const string AdminUserName = "admin";
    public const string AdminDisplayName = "Systeembeheerder";

    /// <summary>Initial password of the administrator. Change it after the first login.</summary>
    public const string InitialAdminPassword = "HelloWorld1!";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChessFixerDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ChessFixerDbContext>>();

        await DropPreMigrationsDatabaseAsync(db, logger);
        await BackUpBeforeMigratingAsync(db, logger);
        await db.Database.MigrateAsync();
        await SeedAdminAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>(), logger);
        await PruneActivityLogAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    /// <summary>The activity log only keeps the last <see cref="ActivityLog.RetentionDays"/> days.</summary>
    private static async Task PruneActivityLogAsync(ChessFixerDbContext db, TimeProvider time)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-ActivityLog.RetentionDays);
        await db.ActivityLog.Where(e => e.At < cutoff).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Databases from before the migrations were created without migration history (they had a SchemaInfo table).
    /// They cannot be migrated, so they are replaced once by a new database.
    /// </summary>
    private static async Task DropPreMigrationsDatabaseAsync(ChessFixerDbContext db, ILogger logger)
    {
        if (!await TableExistsAsync(db, "SchemaInfo")) return;

        logger.LogWarning("Database from before the migrations found; recreating it. Its data is lost.");
        await db.Database.EnsureDeletedAsync();
    }

    private static async Task BackUpBeforeMigratingAsync(ChessFixerDbContext db, ILogger logger)
    {
        if (!await TableExistsAsync(db, "__EFMigrationsHistory")) return; // new database: nothing to keep
        if (!(await db.Database.GetPendingMigrationsAsync()).Any()) return;

        var path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(path) || path == ":memory:") return;

        var backup = $"{Path.GetFullPath(path)}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
        logger.LogInformation("Database schema will be updated; making a backup first: {Backup}", backup);
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {backup}");
    }

    private static async Task SeedAdminAsync(ChessFixerDbContext db, IPasswordHasher<AppUser> hasher, ILogger logger)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin)) return;

        var admin = new AppUser { UserName = AdminUserName, DisplayName = AdminDisplayName, Role = UserRole.Admin };
        admin.PasswordHash = hasher.HashPassword(admin, InitialAdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        logger.LogWarning("Created the system administrator '{UserName}' with the initial password; change it after logging in.",
            AdminUserName);
    }

    private static async Task<bool> TableExistsAsync(ChessFixerDbContext db, string table)
    {
        if (!await db.Database.CanConnectAsync()) return false;
        return await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {table}")
            .SingleAsync() > 0;
    }
}
