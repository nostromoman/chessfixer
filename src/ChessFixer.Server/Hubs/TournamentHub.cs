using Microsoft.AspNetCore.SignalR;

namespace ChessFixer.Server.Hubs;

/// <summary>Push-only hub. Controllers send notifications through IHubContext&lt;TournamentHub&gt;.</summary>
public class TournamentHub : Hub;
