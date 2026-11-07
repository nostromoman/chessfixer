using ChessFixer.Client.Resources;
using ChessFixer.Client.Services;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace ChessFixer.Client.Pages.Tournaments;

/// <summary>
/// Base for the pages of one tournament: loads the tournament, reloads when the server
/// reports a change (SignalR), and shows API errors as a Dutch message.
/// </summary>
public abstract class TournamentPageBase : ComponentBase, IDisposable
{
    [Parameter] public int Id { get; set; }

    [Inject] protected TournamentApi Api { get; set; } = null!;
    [Inject] protected TournamentHubClient Hub { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;
    [Inject] protected IStringLocalizer<Strings> L { get; set; } = null!;

    protected TournamentDto? Tournament { get; private set; }
    protected bool IsLoading { get; private set; } = true;
    protected bool IsNotFound { get; private set; }

    /// <summary>True while an action started with <see cref="RunAsync"/> is in progress.</summary>
    protected bool IsBusy { get; private set; }

    protected override void OnInitialized()
    {
        Hub.TournamentChanged += OnTournamentChangedAsync;
        Hub.Connected += OnHubConnectedAsync;
        Hub.EnsureStarted(); // does not wait: the page loads its data right away
    }

    protected override async Task OnParametersSetAsync()
    {
        await ReloadAsync();
        IsLoading = false;
    }

    /// <summary>Loads page-specific data. <see cref="Tournament"/> is already loaded when this is called.</summary>
    protected virtual Task LoadDataAsync() => Task.CompletedTask;

    /// <summary>Called after the tournament has been (re)loaded, before rendering.</summary>
    protected virtual void OnTournamentLoaded() { }

    protected async Task ReloadAsync()
    {
        try
        {
            Tournament = await Api.GetTournamentAsync(Id);
            IsNotFound = false;
            await LoadDataAsync();
            OnTournamentLoaded();
        }
        catch (ApiClientException e) when (e.Code == ApiErrorCode.TournamentNotFound)
        {
            Tournament = null;
            IsNotFound = true;
        }
    }

    /// <summary>Runs a user action: shows errors as a message and reloads afterwards.</summary>
    protected async Task RunAsync(Func<Task> action, string? successMessage = null)
    {
        // Render the page itself before and after: the action may come from a child component,
        // and then Blazor only re-renders that child, so the page would keep showing "busy".
        IsBusy = true;
        StateHasChanged();
        try
        {
            await action();
            if (successMessage is not null) Snackbar.Add(successMessage, Severity.Success);
            await ReloadAsync();
        }
        catch (ApiClientException e)
        {
            Snackbar.Add(e.Code is { } code ? L[$"Error_{code}"] : L["Error_Unknown"], Severity.Error);
        }
        catch (HttpRequestException)
        {
            Snackbar.Add(L["Error_ServerUnavailable"], Severity.Error);
        }
        finally
        {
            IsBusy = false;
            StateHasChanged();
        }
    }

    // ----- Shared state helpers -----

    protected bool CanStartNextRound =>
        Tournament is { State: TournamentState.New or TournamentState.WaitingForNextRound }
        && !AllRoundsPlayed;

    protected bool AllRoundsPlayed =>
        Tournament is { TotalRounds: { } total } t && t.RoundsStarted >= total && t.RoundsStarted > 0;

    protected bool CanChangePlayers =>
        Tournament is { State: not TournamentState.Closed } t
        && !(t.Settings.PairingSystem == PairingSystem.RoundRobin && t.RoundsStarted > 0);

    /// <summary>A change pushed by the server. Skipped during an own action: that reloads when it is done.</summary>
    private Task OnTournamentChangedAsync(int tournamentId) =>
        tournamentId == Id && !IsBusy ? ReloadAndRenderAsync() : Task.CompletedTask;

    /// <summary>After a (re)connect, changes may have been missed while disconnected.</summary>
    private Task OnHubConnectedAsync() => ReloadAndRenderAsync();

    private Task ReloadAndRenderAsync() =>
        InvokeAsync(async () =>
        {
            await ReloadAsync();
            StateHasChanged();
        });

    public void Dispose()
    {
        Hub.TournamentChanged -= OnTournamentChangedAsync;
        Hub.Connected -= OnHubConnectedAsync;
    }
}
