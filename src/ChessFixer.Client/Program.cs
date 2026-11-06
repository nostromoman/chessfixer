using System.Globalization;
using ChessFixer.Client;
using ChessFixer.Client.Services;
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

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<TournamentApi>();
builder.Services.AddSingleton<TournamentHubClient>();
builder.Services.AddMudServices();
builder.Services.AddLocalization();

await builder.Build().RunAsync();
