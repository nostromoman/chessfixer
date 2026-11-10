using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChessFixer.Server.Hubs;

/// <summary>Push-only hub. Controllers send notifications through IHubContext&lt;TournamentHub&gt;.
/// Only logged-in users can connect (the browser sends the login cookie along).</summary>
[Authorize]
public class TournamentHub : Hub;
