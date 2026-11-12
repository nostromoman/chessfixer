using System.Net;
using System.Net.Http.Json;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Tests;

public class TournamentDefaultsTests : ApiTestBase
{
    [Fact]
    public async Task Defaults_StartWithBuiltInValues_AndAreSaved()
    {
        var builtIn = TournamentSettingsDto.Default;
        var defaults = await GetAsync<TournamentDefaultsDto>("api/settings/tournament-defaults");
        Assert.Equal(builtIn, defaults.ToSettings(builtIn.PairingSystem));

        var changed = new TournamentDefaultsDto(GameTimeMinutes: 10, IncrementSeconds: 5, Boards: 12,
            PointsForWin: 3, PointsForDraw: 1, PointsForLoss: 0, PointsForBye: 2, PointsForAbsence: 0.5, IsRated: false);
        Assert.Equal(changed, await PutAsync<TournamentDefaultsDto>("api/settings/tournament-defaults", changed));
        Assert.Equal(changed, await GetAsync<TournamentDefaultsDto>("api/settings/tournament-defaults"));
    }

    [Fact]
    public async Task Defaults_AreValidatedLikeTournamentSettings()
    {
        var invalid = new TournamentDefaultsDto(0, 5, 8, 1, 0.5, 0, 1, 0, true);
        await AssertErrorAsync(await Client.PutAsJsonAsync("api/settings/tournament-defaults", invalid),
            HttpStatusCode.BadRequest, ApiErrorCode.InvalidSettings);
    }
}
