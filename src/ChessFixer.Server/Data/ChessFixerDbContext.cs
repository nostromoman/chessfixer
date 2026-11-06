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
        modelBuilder.Entity<Player>().Property(p => p.Name).HasMaxLength(100);
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
