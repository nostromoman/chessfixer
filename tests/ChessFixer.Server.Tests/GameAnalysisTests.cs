using System.Net.Http.Json;
using ChessFixer.Core.Chess;
using ChessFixer.Server.Services.Analysis;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessFixer.Server.Tests;

public class GameAnalysisTests : ApiTestBase
{
    /// <summary>A stand-in for Stockfish that looks one move ahead: the side to move plays the move that leaves the
    /// best material balance (the first such move), and the evaluation is that balance. Enough to see a hanging queen.</summary>
    private sealed class FakeEngine : IChessEngine, IChessEngineFactory
    {
        public string Name => "Fake engine 1";

        public Task<IChessEngine> StartAsync(CancellationToken cancellationToken) => Task.FromResult<IChessEngine>(this);

        public Task<EngineResult> AnalyseAsync(string fen, int depth, CancellationToken cancellationToken)
        {
            var position = Position.FromFen(fen);
            var moves = position.LegalMoves();
            if (moves.Count == 0)
                return Task.FromResult(new EngineResult(position.InCheck ? new Evaluation(null, 0) : new Evaluation(0, null), null, []));

            Move? best = null;
            var bestMaterial = 0;
            foreach (var move in moves)
            {
                var material = Material(position.Play(move));
                if (best is null || (position.WhiteToMove ? material > bestMaterial : material < bestMaterial))
                    (best, bestMaterial) = (move, material);
            }
            var uci = best!.Value.ToUci();
            return Task.FromResult(new EngineResult(new Evaluation(bestMaterial, null), uci, [uci]));
        }

        private static int Material(Position position)
        {
            var material = 0;
            for (var square = 0; square < 64; square++)
            {
                var value = char.ToUpperInvariant(position[square]) switch { 'P' => 100, 'N' or 'B' => 300, 'R' => 500, 'Q' => 900, _ => 0 };
                material += char.IsUpper(position[square]) ? value : -value;
            }
            return material;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IChessEngineFactory>();
        services.AddSingleton<IChessEngineFactory>(new FakeEngine());
    }

    private async Task<int> PlayedGameAsync()
    {
        var id = await CreateTournamentAsync();
        await AddNewPlayersAsync(id, 2);
        await StartRoundAsync(id);
        var game = Assert.Single((await GetRoundsAsync(id))[0].Games);
        await SetResultAsync(id, game.Id, GameResult.WhiteWins);
        return id;
    }

    private async Task<GameAnalysisDto> WaitForAnalysisAsync(int gameId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var analysis = await GetAsync<GameAnalysisDto>($"api/games/{gameId}");
            if (analysis.Analysis.Status is AnalysisStatus.Done or AnalysisStatus.Failed) return analysis;
            await Task.Delay(100);
        }
        throw new TimeoutException("The analysis did not finish.");
    }

    [Fact]
    public async Task Pgn_IsAnalysed_AndListed()
    {
        var id = await PlayedGameAsync();
        var gameId = Assert.Single((await GetRoundsAsync(id))[0].Games).Id;
        await ReadAsync<TournamentDto>(await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{gameId}/pgn",
            new SetPgnRequest("1. e4 e5 2. Nf3 d6 3. Bc4 Qe7 4. Bxf7+ 1-0")));

        var analysis = await WaitForAnalysisAsync(gameId);
        Assert.Equal(AnalysisStatus.Done, analysis.Analysis.Status);
        Assert.Equal("Fake engine 1", analysis.Engine);
        Assert.Null(analysis.MoveError);
        Assert.Equal(7, analysis.Plies.Count);
        Assert.Equal(8, analysis.Analysis.PositionsTotal);
        Assert.Equal("Bxf7+", analysis.Plies[^1].San);
        Assert.NotNull(analysis.StartEvaluation);
        Assert.All(analysis.Plies, p =>
        {
            Assert.NotNull(p.Evaluation);
            Assert.NotNull(p.Quality);
            Assert.NotNull(p.BestSan);
        });
        Assert.NotNull(analysis.Analysis.WhiteAccuracy);
        Assert.NotNull(analysis.Analysis.BlackAccuracy);

        var listed = Assert.Single(await GetAsync<List<AnalysedGameSummaryDto>>("api/games"));
        Assert.Equal(gameId, listed.GameId);
        Assert.Equal(4, listed.Moves);
        Assert.Equal(AnalysisStatus.Done, listed.Analysis.Status);
    }

    [Fact]
    public async Task HangingTheQueen_IsABlunder_WithTheBetterMove()
    {
        var id = await PlayedGameAsync();
        var gameId = Assert.Single((await GetRoundsAsync(id))[0].Games).Id;
        await ReadAsync<TournamentDto>(await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{gameId}/pgn",
            new SetPgnRequest("1. e4 d5 2. Qg4 Bxg4 0-1")));

        var analysis = await WaitForAnalysisAsync(gameId);

        // 2. Qg4?? lets the bishop take the queen; exd5 (a pawn up) was better.
        var blunder = analysis.Plies[2];
        Assert.Equal("Qg4", blunder.San);
        Assert.Equal(MoveQuality.Blunder, blunder.Quality);
        Assert.Equal("exd5", blunder.BestSan);
        Assert.Equal(100, blunder.BestEvaluation!.Centipawns);
        Assert.Equal(-900, blunder.Evaluation!.Centipawns);
        Assert.Equal(["Bxg4"], blunder.NextLine);

        // Taking the queen is the engine's move.
        Assert.Equal(MoveQuality.Best, analysis.Plies[3].Quality);
        Assert.Equal(1, analysis.Analysis.White!.Blunders);
        Assert.Equal(0, analysis.Analysis.Black!.Blunders);
    }

    [Fact]
    public async Task PlayerGames_ListsGamesWithTheAnalysis()
    {
        var id = await PlayedGameAsync();
        var game = Assert.Single((await GetRoundsAsync(id))[0].Games);
        var whiteId = game.White.PlayerId;
        var blackId = game.Black!.PlayerId;

        var listed = Assert.Single(await GetAsync<List<PlayerAnalysedGameDto>>($"api/players/{blackId}/games"));
        Assert.Equal(game.Id, listed.GameId);
        Assert.False(listed.IsWhite);
        Assert.Equal(whiteId, listed.White.PlayerId);
        Assert.Equal(GameResult.WhiteWins, listed.Result);
        Assert.True(listed.CanHavePgn);
        Assert.False(listed.HasPgn);
        Assert.Equal(AnalysisStatus.None, listed.Analysis.Status);

        await ReadAsync<TournamentDto>(await Client.PutAsJsonAsync($"api/tournaments/{id}/games/{game.Id}/pgn",
            new SetPgnRequest("1. e4 d5 2. Qg4 Bxg4 0-1")));
        await WaitForAnalysisAsync(game.Id);

        listed = Assert.Single(await GetAsync<List<PlayerAnalysedGameDto>>($"api/players/{whiteId}/games"));
        Assert.True(listed.IsWhite);
        Assert.True(listed.HasPgn);
        Assert.Equal(AnalysisStatus.Done, listed.Analysis.Status);
        Assert.Equal(1, listed.Analysis.White!.Blunders);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await Client.GetAsync("api/players/99999/games")).StatusCode);
    }

    [Fact]
    public async Task ChangedMoves_AreAnalysedAgain_RemovedMoves_RemoveTheGameFromTheList()
    {
        var id = await PlayedGameAsync();
        var gameId = Assert.Single((await GetRoundsAsync(id))[0].Games).Id;
        var url = $"api/tournaments/{id}/games/{gameId}/pgn";

        await ReadAsync<TournamentDto>(await Client.PutAsJsonAsync(url, new SetPgnRequest("1. e4 e5 1-0")));
        Assert.Equal(2, (await WaitForAnalysisAsync(gameId)).Plies.Count);

        // A typo stops reading there; the moves before it are analysed.
        await ReadAsync<TournamentDto>(await Client.PutAsJsonAsync(url, new SetPgnRequest("1. d4 d5 2. Kf3 1-0")));
        var analysis = await WaitForAnalysisAsync(gameId);
        Assert.Equal(2, analysis.Plies.Count);
        Assert.Equal("d4", analysis.Plies[0].San);
        Assert.Equal(new PgnMoveErrorDto(2, true, "Kf3"), analysis.MoveError);

        await ReadAsync<TournamentDto>(await Client.PutAsJsonAsync(url, new SetPgnRequest(null)));
        Assert.Empty(await GetAsync<List<AnalysedGameSummaryDto>>("api/games"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await Client.GetAsync($"api/games/{gameId}")).StatusCode);
    }
}
