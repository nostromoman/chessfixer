using ChessFixer.Server.Services.Clubs;
using ChessFixer.Server.Services.Reports;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChessFixer.Server.Data;

/// <summary>
/// The database. Schema changes are made with EF Core migrations (see Data/Migrations), so the data is kept:
/// change the model, then run <c>dotnet ef migrations add &lt;Name&gt;</c> in src/ChessFixer.Server.
/// </summary>
public class ChessFixerDbContext(DbContextOptions<ChessFixerDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerPhoto> PlayerPhotos => Set<PlayerPhoto>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentReport> TournamentReports => Set<TournamentReport>();
    public DbSet<TournamentPlayer> TournamentPlayers => Set<TournamentPlayer>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<WritingStyle> WritingStyles => Set<WritingStyle>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<RatingLogEntry> RatingLog => Set<RatingLogEntry>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();
    public DbSet<ActivityChange> ActivityChanges => Set<ActivityChange>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name, not by number: adding or reordering enum values never changes the meaning of stored data.
        StoreAsText<GameResult>();
        StoreAsText<PairingSystem>();
        StoreAsText<ReportStatus>();
        StoreAsText<AiProvider>();
        StoreAsText<UserRole>();
        StoreAsText<RatingChangeReason>();
        StoreAsText<ActivityKind>();
        StoreAsText<ActivityChangeKind>();

        // All times are UTC. SQLite does not keep DateTimeKind, so mark the values as UTC again when reading.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();

        void StoreAsText<TEnum>() where TEnum : struct, Enum =>
            configurationBuilder.Properties<TEnum>().HaveConversion<string>().HaveMaxLength(30);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(player =>
        {
            player.Property(p => p.FirstName).HasMaxLength(50);
            player.Property(p => p.LastName).HasMaxLength(50);
            player.Property(p => p.City).HasMaxLength(100);
            player.HasOne(p => p.Club).WithMany().HasForeignKey(p => p.ClubId).OnDelete(DeleteBehavior.SetNull);
            player.Property(p => p.Name)
                .HasMaxLength(101)
                .HasComputedColumnSql("trim(\"FirstName\" || ' ' || \"LastName\")", stored: true);
            player.HasIndex(p => p.Name);
            player.HasOne(p => p.Photo).WithOne().HasForeignKey<PlayerPhoto>(photo => photo.PlayerId);
            player.ToTable(t => t.HasCheckConstraint("CK_Players_Rating", "\"Rating\" >= 0"));
        });

        modelBuilder.Entity<Club>(club =>
        {
            club.Property(c => c.Name).HasMaxLength(ClubRules.MaxNameLength);
            club.Property(c => c.City).HasMaxLength(ClubRules.MaxCityLength);
            club.Property(c => c.Description).HasMaxLength(ClubRules.MaxDescriptionLength);
            club.HasIndex(c => c.Name);
            // At most one own club.
            club.HasIndex(c => c.IsOwnClub).IsUnique().HasFilter("\"IsOwnClub\" = 1").HasDatabaseName("IX_Clubs_OwnClub");
        });

        modelBuilder.Entity<RatingLogEntry>(entry =>
        {
            entry.HasIndex(e => e.At);
            entry.HasIndex(e => new { e.PlayerId, e.At });
            entry.Property(e => e.TournamentTitle).HasMaxLength(200);
            entry.Property(e => e.ChangedBy).HasMaxLength(100);
            // Players are only soft-deleted; the log is never deleted with them.
            entry.HasOne(e => e.Player).WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne(e => e.Opponent).WithMany().HasForeignKey(e => e.OpponentId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<Tournament>().WithMany().HasForeignKey(e => e.TournamentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlayerPhoto>(photo =>
        {
            photo.HasKey(p => p.PlayerId);
            photo.Property(p => p.ContentType).HasMaxLength(50);
        });

        modelBuilder.Entity<Tournament>(tournament =>
        {
            tournament.Property(t => t.Title).HasMaxLength(200);
            tournament.Property(t => t.Location).HasMaxLength(200);
            tournament.Property(t => t.Description).HasMaxLength(2000);
            tournament.HasOne(t => t.Report).WithOne().HasForeignKey<TournamentReport>(r => r.TournamentId);
            tournament.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Tournaments_Boards", "\"Boards\" >= 1");
                t.HasCheckConstraint("CK_Tournaments_TimeControl", "\"GameTimeMinutes\" > 0 AND \"IncrementSeconds\" >= 0");
                t.HasCheckConstraint("CK_Tournaments_Points",
                    "\"PointsForWin\" >= 0 AND \"PointsForDraw\" >= 0 AND \"PointsForLoss\" >= 0 AND \"PointsForBye\" >= 0 AND \"PointsForAbsence\" >= 0");
            });
        });

        modelBuilder.Entity<TournamentReport>(report =>
        {
            report.HasKey(r => r.TournamentId);
            report.Property(r => r.Model).HasMaxLength(300);
            report.Property(r => r.WritingStyleName).HasMaxLength(WritingStyleRules.MaxNameLength);
            report.Property(r => r.WritingStyleDescription).HasMaxLength(WritingStyleRules.MaxDescriptionLength);
        });

        modelBuilder.Entity<WritingStyle>(style =>
        {
            style.Property(s => s.Name).HasMaxLength(WritingStyleRules.MaxNameLength);
            style.Property(s => s.Description).HasMaxLength(WritingStyleRules.MaxDescriptionLength);
            // The styles a new database starts with; they can be changed or deleted in the settings.
            style.HasData(WritingStyleRules.Defaults);
        });

        modelBuilder.Entity<TournamentPlayer>(participant =>
        {
            participant.HasIndex(tp => new { tp.TournamentId, tp.PlayerId }).IsUnique();
            // Players are only soft-deleted; never let a hard delete take their tournament history with it.
            participant.HasOne(tp => tp.Player).WithMany().HasForeignKey(tp => tp.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Round>(round =>
        {
            round.HasIndex(r => new { r.TournamentId, r.Number }).IsUnique();
            // At most one pending (paired, not started) round per tournament.
            round.HasIndex(r => r.TournamentId)
                .IsUnique()
                .HasFilter("\"StartedAt\" IS NULL")
                .HasDatabaseName("IX_Rounds_TournamentId_Pending");
        });

        modelBuilder.Entity<Game>(game =>
        {
            game.HasIndex(g => new { g.RoundId, g.Number }).IsUnique();
            game.HasOne(g => g.White).WithMany().HasForeignKey(g => g.WhiteId).OnDelete(DeleteBehavior.Restrict);
            game.HasOne(g => g.Black).WithMany().HasForeignKey(g => g.BlackId).OnDelete(DeleteBehavior.Restrict);
            game.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Games_DifferentPlayers", "\"BlackId\" IS NULL OR \"BlackId\" <> \"WhiteId\"");
                // A bye is exactly a game without Black, and it never takes a board.
                t.HasCheckConstraint("CK_Games_Bye",
                    "(\"BlackId\" IS NULL) = (\"Result\" = 'Bye') AND (\"Result\" <> 'Bye' OR \"Board\" IS NULL)");
                // The rating columns are filled in together, or not at all.
                t.HasCheckConstraint("CK_Games_Rating",
                    "(\"RatedAt\" IS NULL AND \"WhiteRatingBefore\" IS NULL AND \"BlackRatingBefore\" IS NULL" +
                    " AND \"WhiteRatingChange\" IS NULL AND \"BlackRatingChange\" IS NULL)" +
                    " OR (\"RatedAt\" IS NOT NULL AND \"WhiteRatingBefore\" IS NOT NULL AND \"BlackRatingBefore\" IS NOT NULL" +
                    " AND \"WhiteRatingChange\" IS NOT NULL AND \"BlackRatingChange\" IS NOT NULL)");
            });
        });

        modelBuilder.Entity<AppSettings>(settings =>
        {
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.Property(s => s.OllamaUrl).HasMaxLength(500);
            settings.Property(s => s.OllamaModel).HasMaxLength(200);
            settings.Property(s => s.ClaudeApiKey).HasMaxLength(500);
            settings.Property(s => s.ClaudeModel).HasMaxLength(200);
            settings.Property(s => s.ReportSystemPrompt).HasMaxLength(TournamentReportPrompt.MaxSystemPromptLength);
            settings.HasOne<WritingStyle>().WithMany().HasForeignKey(s => s.DefaultWritingStyleId).OnDelete(DeleteBehavior.SetNull);
            settings.ToTable(t => t.HasCheckConstraint("CK_AppSettings_Singleton", $"\"Id\" = {Data.AppSettings.SingletonId}"));
        });

        modelBuilder.Entity<AppUser>(user =>
        {
            // NOCASE: "Roel" and "roel" are the same login name.
            user.Property(u => u.UserName).HasMaxLength(50).UseCollation("NOCASE");
            user.Property(u => u.DisplayName).HasMaxLength(100);
            user.Property(u => u.PasswordHash).HasMaxLength(200);
            user.Property(u => u.SecurityStamp).HasMaxLength(50);
            user.HasIndex(u => u.UserName).IsUnique();
        });

        modelBuilder.Entity<ActivityLogEntry>(entry =>
        {
            entry.HasIndex(e => e.At);
            entry.HasIndex(e => new { e.UserId, e.At });
            entry.Property(e => e.UserName).HasMaxLength(100);
            entry.Property(e => e.DisplayName).HasMaxLength(100);
            entry.Property(e => e.Action).HasMaxLength(100);
            entry.Property(e => e.Method).HasMaxLength(10);
            entry.Property(e => e.Path).HasMaxLength(300);
            entry.Property(e => e.IpAddress).HasMaxLength(50);
            entry.Property(e => e.UserAgent).HasMaxLength(300);
            entry.HasIndex(e => e.RevertOfId);
            // The changes go with the entry (also when old entries are cleaned up at startup).
            entry.HasMany(e => e.Changes).WithOne().HasForeignKey(c => c.EntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActivityChange>(change =>
        {
            change.Property(c => c.EntityType).HasMaxLength(50);
            change.Property(c => c.EntityKey).HasMaxLength(50);
            change.Property(c => c.Label).HasMaxLength(200);
        });
    }

    public sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
