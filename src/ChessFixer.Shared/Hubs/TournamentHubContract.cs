namespace ChessFixer.Shared.Hubs;

/// <summary>
/// SignalR contract. The hub only notifies; clients re-fetch data through the Web API.
/// </summary>
public static class TournamentHubContract
{
    public const string Path = "/hubs/tournament";

    /// <summary>Sent with the tournament id whenever anything in that tournament changes.</summary>
    public const string TournamentChanged = nameof(TournamentChanged);

    /// <summary>Sent when the list of tournaments changes (created, deleted, state changed).</summary>
    public const string TournamentListChanged = nameof(TournamentListChanged);
}
