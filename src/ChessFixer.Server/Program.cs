using System.Threading.RateLimiting;
using ChessFixer.Server.Controllers;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Hubs;
using ChessFixer.Server.Services;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Server.Services.Clubs;
using ChessFixer.Server.Services.Ratings;
using ChessFixer.Server.Services.Reports;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Every API endpoint requires a logged-in user, unless it is marked [AllowAnonymous] (only logging in and out).
builder.Services.AddControllers(options => options.Filters.Add(new AuthorizeFilter()));

// Authentication: a cookie, set by AuthController. The API answers 401/403 instead of redirecting to a login page.
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ChessFixer.Auth";
        options.Cookie.HttpOnly = true;
        // Strict: the cookie is never sent with requests started by another site (protects against CSRF).
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Secure (HTTPS only) when the request came in over HTTPS, also via the reverse proxy (see forwarded headers);
        // plain HTTP on the LAN (http://<ip>:5016) keeps working.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = UserSession.ValidateAsync;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// The keys that sign the login cookie. In Docker they are kept on the data volume (DataProtection__KeysPath),
// otherwise every container update would log everybody out.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrEmpty(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("ChessFixer")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

// Slows down password guessing: a limited number of login attempts per minute per client address.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// Behind a reverse proxy (e.g. Nginx Proxy Manager with Let's Encrypt) every request comes from the proxy.
// Take the real client address and scheme from X-Forwarded-For/-Proto, so the login rate limit works per client
// and the app knows the request was HTTPS. Only proxies on the local/private network are trusted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7" })
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TournamentService>();

// AI-written tournament reports. Register one IAiTextGenerator per provider (Ollama now; e.g. Claude later).
builder.Services.AddScoped<AiSettingsService>();
builder.Services.AddScoped<IAiTextGenerator, OllamaTextGenerator>();
builder.Services.AddScoped<IAiTextGenerator, ClaudeTextGenerator>();
builder.Services.AddHttpClient(OllamaTextGenerator.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddSingleton<ReportQueue>();
builder.Services.AddScoped<TournamentReportWriter>();
builder.Services.AddScoped<WritingStyleService>();
builder.Services.AddScoped<ClubService>();
builder.Services.AddScoped<RatingLogService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService<ReportWorker>();
builder.Services.AddDbContext<ChessFixerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ChessFixer")));

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Static files and index.html need no login: the client shows the login page itself.
app.MapStaticAssets();
app.MapControllers();
app.MapHub<TournamentHub>(TournamentHubContract.Path);
// index.html refers to fingerprinted framework files, so it must never be served from cache:
// after an update a stale copy would point to files that no longer exist and the app would hang.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
