using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChessFixer.Client.Services;

/// <summary>
/// One SignalR connection for the whole app. Pages subscribe to the events and re-fetch data through the API.
/// </summary>
public sealed class TournamentHubClient(NavigationManager navigation) : IAsyncDisposable
{
    private HubConnection? _connection;
    private Task? _startTask;

    /// <summary>Raised with the tournament id when something in that tournament changed.</summary>
    public event Func<int, Task>? TournamentChanged;

    /// <summary>Raised when the list of tournaments changed.</summary>
    public event Func<Task>? TournamentListChanged;

    public Task EnsureStartedAsync() => _startTask ??= StartAsync();

    private async Task StartAsync()
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(TournamentHubContract.Path))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<int>(TournamentHubContract.TournamentChanged,
            id => RaiseAsync(TournamentChanged, handler => handler(id)));
        _connection.On(TournamentHubContract.TournamentListChanged,
            () => RaiseAsync(TournamentListChanged, handler => handler()));

        await _connection.StartAsync();
    }

    private static async Task RaiseAsync<THandler>(THandler? handlers, Func<THandler, Task> invoke)
        where THandler : Delegate
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<THandler>())
            await invoke(handler);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
