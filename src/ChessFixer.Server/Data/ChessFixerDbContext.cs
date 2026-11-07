using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Data;

public class ChessFixerDbContext(DbContextOptions<ChessFixerDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentPlayer> TournamentPlayers => Set<TournamentPlayer>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<Game> Games => Set<Game>();
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
        modelBuilder.Entity<Tournament>().Property(t => t.Title).HasMaxLength(200);

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
