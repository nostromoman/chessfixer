using ChessFixer.Server.Data;
using ChessFixer.Server.Hubs;
using ChessFixer.Shared.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddDbContext<ChessFixerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ChessFixer")));

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.MapStaticAssets();
app.MapControllers();
app.MapHub<TournamentHub>(TournamentHubContract.Path);
app.MapFallbackToFile("index.html");

app.Run();
