using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChessFixer.Client.Services;

/// <summary>
/// One SignalR connection for the whole app. Pages subscribe to the events and re-fetch data through the API.
/// Pages never wait for this connection: it is started in the background and keeps retrying, so the app
/// also works (without live updates) when the connection is slow or unavailable.
/// </summary>
public sealed class TournamentHubClient(NavigationManager navigation) : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stopping = new();
    private HubConnection? _connection;

    /// <summary>Raised with the tournament id when something in that tournament changed.</summary>
    public event Func<int, Task>? TournamentChanged;

    /// <summary>Raised when the list of tournaments changed.</summary>
    public event Func<Task>? TournamentListChanged;

    /// <summary>Raised with the game id when the engine analysis of that game changed (requested, progress, done).</summary>
    public event Func<int, Task>? GameAnalysisChanged;

    /// <summary>Raised after the connection was (re)established; updates may have been missed, so reload.</summary>
    public event Func<Task>? Connected;

    /// <summary>Starts connecting in the background (once). Does not wait for the connection.</summary>
    public void EnsureStarted()
    {
        if (_connection is not null) return;

        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(TournamentHubContract.Path))
            .WithAutomaticReconnect(new RetryForever())
            .Build();

        _connection.On<int>(TournamentHubContract.TournamentChanged,
            id => RaiseAsync(TournamentChanged, handler => handler(id)));
        _connection.On(TournamentHubContract.TournamentListChanged,
            () => RaiseAsync(TournamentListChanged, handler => handler()));
        _connection.On<int>(TournamentHubContract.GameAnalysisChanged,
            id => RaiseAsync(GameAnalysisChanged, handler => handler(id)));
        _connection.Reconnected += _ => RaiseAsync(Connected, handler => handler());

        _ = ConnectAsync(_connection);
    }

    /// <summary>The first connect is not covered by automatic reconnect, so retry it here.</summary>
    private async Task ConnectAsync(HubConnection connection)
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await connection.StartAsync(_stopping.Token);
                await RaiseAsync(Connected, handler => handler());
                return;
            }
            catch (Exception) when (!_stopping.IsCancellationRequested)
            {
                await Task.Delay(RetryDelay, _stopping.Token).ContinueWith(_ => { });
            }
        }
    }

    private static async Task RaiseAsync<THandler>(THandler? handlers, Func<THandler, Task> invoke)
        where THandler : Delegate
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<THandler>())
        {
            try
            {
                await invoke(handler);
            }
            catch (Exception)
            {
                // One failing page must not stop the others from updating.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private sealed class RetryForever : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext context) =>
            context.PreviousRetryCount < 3 ? TimeSpan.FromSeconds(1) : RetryDelay;
    }
}
