using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class TournamentFlowTests : ApiTestBase
{
    [Fact]
    public async Task SwissTournament_FullRound()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 5);

        // Round 1 is already paired (not started) so it can be shown before the start.
        var pending = Assert.Single(await GetRoundsAsync(id));
        Assert.False(pending.IsStarted);
        Assert.Equal(3, pending.Games.Count);
        var bye = Assert.Single(pending.Games, g => g.Black is null);
        Assert.Equal(GameResult.Bye, bye.Result);

        var tournament = await StartRoundAsync(id);
        Assert.Equal(TournamentState.RoundActive, tournament.State);
        Assert.Equal(1, tournament.RoundsStarted);
        Assert.True(tournament.SettingsLocked);

        var round1 = Assert.Single(await GetRoundsAsync(id));
        Assert.True(round1.IsStarted);

        var games = round1.Games.Where(g => g.Black is not null).ToList();
        tournament = await SetResultAsync(id, games[0].Id, GameResult.WhiteWins);
        Assert.Equal(TournamentState.RoundActive, tournament.State);

        tournament = await SetResultAsync(id, games[1].Id, GameResult.Draw);
        Assert.Equal(TournamentState.WaitingForNextRound, tournament.State);

        // Round 2 is paired as soon as all results are in.
        var rounds = await GetRoundsAsync(id);
        Assert.Equal(2, rounds.Count);
        Assert.False(rounds[1].IsStarted);

        var standings = await GetStandingsAsync(id);
        Assert.Equal(5, standings.Count);
        Assert.Equal(3, standings.Sum(s => s.Points)); // win + draw + bye

        tournament = await StartRoundAsync(id);
        Assert.Equal(TournamentState.RoundActive, tournament.State);
        Assert.Equal(2, tournament.RoundsStarted);
    }

    [Fact]
    public async Task Settings_CannotChangeDuringARound()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        var tournament = await StartRoundAsync(id);

        await AssertErrorAsync(
            await Client.PutAsJsonAsync($"api/tournaments/{id}",
                new UpdateTournamentRequest("Nieuwe titel", tournament.Settings)),
            HttpStatusCode.Conflict, ApiErrorCode.RoundInProgress);

        // Saving unchanged values is harmless.
        tournament = await PutAsync<TournamentDto>($"api/tournaments/{id}",
            new UpdateTournamentRequest("Test", tournament.Settings));
        Assert.Equal("Test", tournament.Title);
    }

    [Fact]
    public async Task BetweenRounds_OnlyTitleTimeControlAndBoardsCanChange()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 4);
        await StartRoundAsync(id);
        var tournament = await FinishActiveRoundAsync(id);
        Assert.True(tournament.SettingsLocked);

        tournament = await PutAsync<TournamentDto>($"api/tournaments/{id}", new UpdateTournamentRequest("Nieuwe titel",
            tournament.Settings with { GameTimeMinutes = 25, IncrementSeconds = 0, Boards = 1 }));
        Assert.Equal("Nieuwe titel", tournament.Title);
        Assert.Equal(new[] { 25, 0, 1 }, new[] { tournament.Settings.GameTimeMinutes, tournament.Settings.IncrementSeconds, tournament.Settings.Boards });

        // The pending round is re-paired with the new number of boards.
        var round2 = (await GetRoundsAsync(id))[1];
        Assert.Equal(new int?[] { 1, null }, round2.Games.Select(g => g.Board).ToArray());

        await AssertErrorAsync(
            await Client.PutAsJsonAsync($"api/tournaments/{id}",
                new UpdateTournamentRequest("Nieuwe titel", tournament.Settings with { PointsForWin = 3 })),
            HttpStatusCode.Conflict, ApiErrorCode.SettingsLocked);
        await AssertErrorAsync(
            await Client.PutAsJsonAsync($"api/tournaments/{id}",
                new UpdateTournamentRequest("Nieuwe titel", tournament.Settings with { PairingSystem = PairingSystem.RoundRobin })),
            HttpStatusCode.Conflict, ApiErrorCode.SettingsLocked);
        await AssertErrorAsync(
            await Client.PutAsJsonAsync($"api/tournaments/{id}",
                new UpdateTournamentRequest("Nieuwe titel", tournament.Settings with { Boards = 0 })),
            HttpStatusCode.BadRequest, ApiErrorCode.InvalidSettings);
    }

    [Fact]
    public async Task Boards_GamesThatDoNotFitWaitInTheQueue()
    {
        var id = await CreateTournamentAsync();
        var tournament = await PutAsync<TournamentDto>($"api/tournaments/{id}",
            new UpdateTournamentRequest("Test", (await GetTournamentAsync(id)).Settings with { Boards = 2 }));
        await AddNewPlayersAsync(id, 7);
        await StartRoundAsync(id);

        // 3 games + bye: games 1 and 2 on a board, game 3 in the queue, the bye without a board.
        var games = (await GetRoundsAsync(id))[0].Games;
        Assert.Equal(new int?[] { 1, 2, null, null }, games.Select(g => g.Board).ToArray());
        Assert.Equal(GameResult.Bye, games[3].Result);

        // A queued game has not started: no result yet.
        await AssertErrorAsync(
            await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{games[2].Id}/result", new SetResultRequest(GameResult.Draw)),
            HttpStatusCode.Conflict, ApiErrorCode.ResultNotEditable);

        // Board 2 is free: the queued game moves there.
        await SetResultAsync(id, games[1].Id, GameResult.Draw);
        games = (await GetRoundsAsync(id))[0].Games;
        Assert.Equal(new int?[] { 1, 2, 2, null }, games.Select(g => g.Board).ToArray());

        // Reopening game 2: its board is taken, so it waits in the queue for the next free board.
        await SetResultAsync(id, games[1].Id, GameResult.NotPlayed);
        games = (await GetRoundsAsync(id))[0].Games;
        Assert.Equal(new int?[] { 1, null, 2, null }, games.Select(g => g.Board).ToArray());

        await SetResultAsync(id, games[0].Id, GameResult.WhiteWins);
        games = (await GetRoundsAsync(id))[0].Games;
        Assert.Equal(new int?[] { 1, 1, 2, null }, games.Select(g => g.Board).ToArray());
    }

    [Fact]
    public async Task Title_CanChangeBeforeRound1()
    {
        var id = await CreateTournamentAsync();
        var tournament = await GetTournamentAsync(id);

        tournament = await PutAsync<TournamentDto>($"api/tournaments/{id}",
            new UpdateTournamentRequest("Nieuwe titel", tournament.Settings));

        Assert.Equal("Nieuwe titel", tournament.Title);
    }

    [Fact]
    public async Task InvalidSettings_AreRejected()
    {
        var id = await CreateTournamentAsync();
        var tournament = await GetTournamentAsync(id);

        var response = await Client.PutAsJsonAsync($"api/tournaments/{id}",
            new UpdateTournamentRequest("Test", tournament.Settings with { GameTimeMinutes = 0 }));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrorCode.InvalidSettings);
    }

    [Fact]
    public async Task Players_CanJoinAndWithdrawDuringARound()
    {
        var id = await CreateTournamentAsync();
        var players = await AddNewPlayersAsync(id, 4);
        await StartRoundAsync(id);
        var late = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Laatkomer", null, null));

        // The late joiner waits: the active round does not change.
        await PostAsync<TournamentDto>($"api/tournaments/{id}/players", new AddTournamentPlayerRequest(late.Id));
        var rounds = await GetRoundsAsync(id);
        Assert.Single(rounds);
        Assert.Equal(2, rounds[0].Games.Count);

        // A player cannot withdraw while their game is still being played.
        await AssertErrorAsync(
            await Client.DeleteAsync($"api/tournaments/{id}/players/{players[0]}"),
            HttpStatusCode.Conflict, ApiErrorCode.PlayerInActiveGame);

        // Once their game is finished they can, even though the round is still active.
        var game = rounds[0].Games.Single(g => g.White.PlayerId == players[0] || g.Black?.PlayerId == players[0]);
        var tournament = await SetResultAsync(id, game.Id, GameResult.Draw);
        Assert.Equal(TournamentState.RoundActive, tournament.State);
        Assert.Equal(HttpStatusCode.OK, (await Client.DeleteAsync($"api/tournaments/{id}/players/{players[0]}")).StatusCode);
        Assert.Equal(2, (await GetRoundsAsync(id))[0].Games.Count);

        await FinishActiveRoundAsync(id);
        var round2 = (await GetRoundsAsync(id))[1];
        var paired = round2.Games.SelectMany(g => new[] { g.White.PlayerId, g.Black?.PlayerId }).ToList();
        Assert.Contains(late.Id, paired);
        Assert.DoesNotContain(players[0], paired);
    }

    [Fact]
    public async Task LateJoiner_IsIncludedInNextPendingRound()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 4);
        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);
        Assert.Equal(2, (await GetRoundsAsync(id))[1].Games.Count);

        var late = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Laatkomer", null, null));
        await PostAsync<TournamentDto>($"api/tournaments/{id}/players", new AddTournamentPlayerRequest(late.Id));

        var round2 = (await GetRoundsAsync(id))[1];
        Assert.Equal(3, round2.Games.Count); // 2 games + bye
        Assert.Contains(round2.Games, g => g.White.PlayerId == late.Id || g.Black?.PlayerId == late.Id);
    }

    [Fact]
    public async Task CorrectingAResult_RepairsNextRound_ClearingItReopensTheRound()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 4);
        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);
        var game = (await GetRoundsAsync(id))[0].Games[0];

        var tournament = await SetResultAsync(id, game.Id, GameResult.Draw);
        Assert.Equal(TournamentState.WaitingForNextRound, tournament.State);
        Assert.Equal(2, (await GetRoundsAsync(id)).Count);

        tournament = await SetResultAsync(id, game.Id, GameResult.NotPlayed);
        Assert.Equal(TournamentState.RoundActive, tournament.State);
        Assert.Single(await GetRoundsAsync(id)); // pending round removed
    }

    [Fact]
    public async Task ResultOfAnOlderRound_CannotBeChanged()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 4);
        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);
        await StartRoundAsync(id);
        var round1Game = (await GetRoundsAsync(id))[0].Games[0];

        var response = await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{round1Game.Id}/result",
            new SetResultRequest(GameResult.Draw));

        await AssertErrorAsync(response, HttpStatusCode.Conflict, ApiErrorCode.ResultNotEditable);
    }

    [Fact]
    public async Task ByeResult_CannotBeChanged()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 3);
        await StartRoundAsync(id);
        var bye = (await GetRoundsAsync(id))[0].Games.Single(g => g.Black is null);

        var response = await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{bye.Id}/result",
            new SetResultRequest(GameResult.WhiteWins));

        await AssertErrorAsync(response, HttpStatusCode.Conflict, ApiErrorCode.ResultNotEditable);
    }

    [Fact]
    public async Task RemovedPlayer_WithGames_StaysInStandings_WithoutGames_IsGone()
    {
        var id = await CreateTournamentAsync();
        var players = await AddNewPlayersAsync(id, 4);

        // Before round 1: removed completely.
        await Client.DeleteAsync($"api/tournaments/{id}/players/{players[3]}");
        Assert.DoesNotContain(await GetAsync<List<TournamentPlayerDto>>($"api/tournaments/{id}/players"),
            p => p.PlayerId == players[3]);

        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);

        // After round 1: kept, but inactive and no longer paired.
        (await Client.DeleteAsync($"api/tournaments/{id}/players/{players[0]}")).EnsureSuccessStatusCode();

        var player = Assert.Single(await GetAsync<List<TournamentPlayerDto>>($"api/tournaments/{id}/players"),
            p => p.PlayerId == players[0]);
        Assert.False(player.IsActive);
        Assert.Contains(await GetStandingsAsync(id), s => s.PlayerId == players[0] && !s.IsActive);
        var round2 = (await GetRoundsAsync(id))[1];
        Assert.DoesNotContain(round2.Games, g => g.White.PlayerId == players[0] || g.Black?.PlayerId == players[0]);
    }

    [Fact]
    public async Task RoundRobin_PlayerCanJoinBetweenRounds_AndContinuesAfterAllRounds()
    {
        var id = await CreateTournamentAsync(PairingSystem.RoundRobin);
        await AddNewPlayersAsync(id, 3);
        var tournament = await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);

        // A late joiner is welcome between rounds.
        var late = await PostAsync<PlayerDto>("api/players", new CreatePlayerRequest("Laatkomer", null, null));
        var response = await Client.PostAsJsonAsync($"api/tournaments/{id}/players", new AddTournamentPlayerRequest(late.Id));
        response.EnsureSuccessStatusCode();

        for (var round = 2; round <= 4; round++)
        {
            await StartRoundAsync(id);
            await FinishActiveRoundAsync(id);
        }

        tournament = await GetTournamentAsync(id);
        Assert.Equal(TournamentState.WaitingForNextRound, tournament.State);
        Assert.Equal(4, tournament.RoundsStarted);

        // A next round can still be started (the schedule starts again).
        tournament = await StartRoundAsync(id);
        Assert.Equal(TournamentState.RoundActive, tournament.State);
        Assert.Equal(5, tournament.RoundsStarted);
    }

    [Fact]
    public async Task ClosedTournament_AcceptsNoMoreChanges()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);
        var game = (await GetRoundsAsync(id))[0].Games[0];

        var tournament = await PostAsync<TournamentDto>($"api/tournaments/{id}/close", null);
        Assert.Equal(TournamentState.Closed, tournament.State);
        Assert.Single(await GetRoundsAsync(id)); // pending round removed

        await AssertErrorAsync(
            await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{game.Id}/result", new SetResultRequest(GameResult.Draw)),
            HttpStatusCode.Conflict, ApiErrorCode.TournamentClosed);
        await AssertErrorAsync(
            await Client.PostAsync($"api/tournaments/{id}/rounds/start", null),
            HttpStatusCode.Conflict, ApiErrorCode.TournamentClosed);
    }

    [Fact]
    public async Task CancelRound_RemovesTheRoundAndUndoesRatings()
    {
        var id = await CreateTournamentAsync();
        var players = await AddNewPlayersAsync(id, 4);
        await StartRoundAsync(id);
        await FinishActiveRoundAsync(id);
        var ratingsAfterRound1 = await RatingsAsync(players);

        var tournament = await StartRoundAsync(id);
        Assert.Equal(2, tournament.RoundsStarted);
        var round2 = (await GetRoundsAsync(id)).Last(r => r.IsStarted);
        Assert.NotNull(round2.StartedAt);
        await SetResultAsync(id, round2.Games.First(g => g.Black is not null).Id, GameResult.BlackWins);
        Assert.NotEqual(ratingsAfterRound1, await RatingsAsync(players));

        tournament = await PostAsync<TournamentDto>($"api/tournaments/{id}/rounds/cancel", null);
        Assert.Equal(TournamentState.WaitingForNextRound, tournament.State);
        Assert.Equal(1, tournament.RoundsStarted);
        Assert.Equal(ratingsAfterRound1, await RatingsAsync(players));

        // Round 2 is paired again, and can be started again.
        var rounds = await GetRoundsAsync(id);
        Assert.Equal([1, 2], rounds.Select(r => r.Number));
        Assert.False(rounds[1].IsStarted);
        tournament = await StartRoundAsync(id);
        Assert.Equal(2, tournament.RoundsStarted);
    }

    [Fact]
    public async Task CancelRound_OnlyWhileARoundIsInProgress()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await AssertErrorAsync(await Client.PostAsync($"api/tournaments/{id}/rounds/cancel", null),
            HttpStatusCode.Conflict, ApiErrorCode.NoRoundInProgress);

        await StartRoundAsync(id);
        var tournament = await PostAsync<TournamentDto>($"api/tournaments/{id}/rounds/cancel", null);
        Assert.Equal(TournamentState.New, tournament.State);
        Assert.False(Assert.Single(await GetRoundsAsync(id)).IsStarted);
    }

    [Fact]
    public async Task CancelRound_RemovesAPlayerWhoWithdrewWithoutOtherGames()
    {
        var id = await CreateTournamentAsync();
        var players = await AddNewPlayersAsync(id, 4);
        await StartRoundAsync(id);
        var game = (await GetRoundsAsync(id))[0].Games.First(g => g.Black is not null);
        await SetResultAsync(id, game.Id, GameResult.Draw);
        await Client.DeleteAsync($"api/tournaments/{id}/players/{game.White.PlayerId}");

        await PostAsync<TournamentDto>($"api/tournaments/{id}/rounds/cancel", null);

        var participants = await GetAsync<List<TournamentPlayerDto>>($"api/tournaments/{id}/players");
        Assert.DoesNotContain(participants, p => p.PlayerId == game.White.PlayerId);
        Assert.Equal(3, participants.Count);
    }

    private async Task<List<int>> RatingsAsync(IEnumerable<int> playerIds)
    {
        var ratings = new List<int>();
        foreach (var playerId in playerIds)
            ratings.Add((await GetAsync<PlayerRatingDto>($"api/players/{playerId}/rating")).Rating);
        return ratings;
    }

    [Fact]
    public async Task CannotCloseDuringARound()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await StartRoundAsync(id);

        await AssertErrorAsync(
            await Client.PostAsync($"api/tournaments/{id}/close", null),
            HttpStatusCode.Conflict, ApiErrorCode.RoundInProgress);
    }

    [Fact]
    public async Task StartWithOnePlayer_ReturnsNotEnoughPlayers()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 1);

        await AssertErrorAsync(
            await Client.PostAsync($"api/tournaments/{id}/rounds/start", null),
            HttpStatusCode.Conflict, ApiErrorCode.NotEnoughPlayers);
    }

    [Fact]
    public async Task UnknownTournament_Returns404()
    {
        await AssertErrorAsync(await Client.GetAsync("api/tournaments/999"),
            HttpStatusCode.NotFound, ApiErrorCode.TournamentNotFound);
    }

    [Fact]
    public async Task EnumsAreSerializedAsText()
    {
        var id = await CreateTournamentAsync();
        var json = await Client.GetStringAsync($"api/tournaments/{id}");

        Assert.Contains("\"state\":\"New\"", json);
        Assert.Contains("\"pairingSystem\":\"Swiss\"", json);
    }
}
