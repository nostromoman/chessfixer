using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Data;

public class ChessFixerDbContext(DbContextOptions<ChessFixerDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentPlayer> TournamentPlayers => Set<TournamentPlayer>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SchemaInfo> SchemaInfo => Set<SchemaInfo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(player =>
        {
            player.Property(p => p.FirstName).HasMaxLength(50);
            player.Property(p => p.LastName).HasMaxLength(50);
            player.Property(p => p.City).HasMaxLength(100);
            player.Property(p => p.Club).HasMaxLength(100);
            player.Property(p => p.PhotoContentType).HasMaxLength(50);
            player.Property(p => p.Name)
                .HasMaxLength(101)
                .HasComputedColumnSql("trim(\"FirstName\" || ' ' || \"LastName\")", stored: true);
        });
        modelBuilder.Entity<Tournament>(tournament =>
        {
            tournament.Property(t => t.Title).HasMaxLength(200);
            tournament.Property(t => t.Location).HasMaxLength(200);
            tournament.Property(t => t.Description).HasMaxLength(2000);
        });

        modelBuilder.Entity<AppSettings>(settings =>
        {
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.Property(s => s.OllamaUrl).HasMaxLength(500);
            settings.Property(s => s.OllamaModel).HasMaxLength(200);
            settings.Property(s => s.ClaudeApiKey).HasMaxLength(500);
            settings.Property(s => s.ClaudeModel).HasMaxLength(200);
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

        modelBuilder.Entity<TournamentPlayer>()
            .HasIndex(tp => new { tp.TournamentId, tp.PlayerId })
            .IsUnique();

        modelBuilder.Entity<Round>()
            .HasIndex(r => new { r.TournamentId, r.Number })
            .IsUnique();

        modelBuilder.Entity<Game>(game =>
        {
            game.HasOne(g => g.White).WithMany().HasForeignKey(g => g.WhiteId).OnDelete(DeleteBehavior.Restrict);
            game.HasOne(g => g.Black).WithMany().HasForeignKey(g => g.BlackId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
