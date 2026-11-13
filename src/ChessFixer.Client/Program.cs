using System.Globalization;
using ChessFixer.Client;
using ChessFixer.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

// Dutch only for now. Texts live in Resources/Strings.resx so other languages can be added later.
var culture = new CultureInfo("nl-NL");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// A longer timeout than the default 100 s: reading a scoresheet photo with Claude can take a minute or two.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(5)
});
builder.Services.AddScoped<TournamentApi>();
builder.Services.AddScoped<DeviceInfo>();

// Login: the server sets a cookie; the app asks the server who is logged in.
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ServerAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(services => services.GetRequiredService<ServerAuthenticationStateProvider>());
builder.Services.AddSingleton<TournamentHubClient>();
builder.Services.AddMudServices();
builder.Services.AddLocalization();

await builder.Build().RunAsync();
