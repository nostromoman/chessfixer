using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Hubs;
using ChessFixer.Server.Services;
using ChessFixer.Shared.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TournamentService>();
builder.Services.AddDbContext<ChessFixerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ChessFixer")));

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.MapStaticAssets();
app.MapControllers();
app.MapHub<TournamentHub>(TournamentHubContract.Path);
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
