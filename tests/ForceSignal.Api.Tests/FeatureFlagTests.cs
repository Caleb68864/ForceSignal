using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ForceSignal.Application.Features;
using ForceSignal.Contracts.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ForceSignal.Api.Tests;

/// <summary>
/// The ground-combat engines are built alongside the working Full Thrust game, so at any moment
/// one of them may be half-finished. These pin down the property that matters: with a flag off,
/// the engine is absent rather than present and broken, and nothing about Full Thrust changes.
/// </summary>
public sealed class FeatureFlagTests
{
    [Fact]
    public void BothGroundCombatEnginesAreOffUnlessSomethingTurnsThemOn()
    {
        var features = FeatureFlags.Read(new StubConfiguration());

        Assert.False(features.StarGrunt);
        Assert.False(features.Dirtside);
    }

    [Theory]
    // Anything that is not an explicit true leaves the engine off. An unclear setting must never
    // be the reason an unfinished engine reaches a game.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("maybe")]
    [InlineData("True-ish")]
    public void AnUnclearSettingLeavesTheEngineOff(string configured)
    {
        var features = FeatureFlags.Read(new StubConfiguration
        {
            ["Features:StarGrunt"] = configured,
        });

        Assert.False(features.StarGrunt);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData(" true ")]
    public void AnExplicitTrueTurnsTheEngineOn(string configured)
    {
        var features = FeatureFlags.Read(new StubConfiguration
        {
            ["Features:StarGrunt"] = configured,
        });

        Assert.True(features.StarGrunt);
        Assert.False(features.Dirtside);
    }

    [Fact]
    public void TheEnvironmentVariableWorksForAContainerWithNoConfigFile()
    {
        var features = FeatureFlags.Read(new StubConfiguration
        {
            ["FORCESIGNAL_FEATURES_DIRTSIDE"] = "true",
        });

        Assert.True(features.Dirtside);
        Assert.False(features.StarGrunt);
    }

    [Fact]
    public async Task WithTheFlagsOff_TheGroundCombatRoutesDoNotExist()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Probed through the create routes. This used to ask the two `/status` routes, which were
        // removed: each returned a compile-time literal, so the only fact either could establish was
        // that it had been mapped - and probing a route whose whole body is a constant tells you
        // nothing about the engine behind it. The create route is the real first door into each
        // engine and it is the one that has to be absent.
        using var starGrunt = await client.PostAsJsonAsync("/api/stargrunt/games", new { name = "Hill 43" });
        using var dirtside = await client.PostAsJsonAsync("/api/dirtside/games", new { name = "Ridge 9" });

        // Not "disabled" - absent. A route that answers at all is a route that can go wrong.
        Assert.Equal(HttpStatusCode.NotFound, starGrunt.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, dirtside.StatusCode);
    }

    [Fact]
    public async Task WithTheFlagsOff_TheClientIsToldThereIsNothingToShow()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var flags = await client.GetFromJsonAsync<FeatureFlagsDto>("/api/features");

        Assert.NotNull(flags);
        Assert.False(flags.StarGrunt);
        Assert.False(flags.Dirtside);
    }

    [Fact]
    public async Task WithAFlagOn_TheEngineMountsAndReadinessSaysItIsUnfinished()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:StarGrunt"] = "true",
        });
        using var client = factory.CreateClient();

        // Mounted: the create route answers rather than 404ing. Asserted on the route that does
        // something, not on a constant.
        using var mounted = await client.PostAsJsonAsync("/api/stargrunt/games", new { name = "Hill 43" });
        mounted.EnsureSuccessStatusCode();

        var flags = await client.GetFromJsonAsync<FeatureFlagsDto>("/api/features");
        Assert.True(flags!.StarGrunt);
        Assert.False(flags.Dirtside);

        // Dirtside stayed off: one flag does not carry the other.
        using var stillOff = await client.PostAsJsonAsync("/api/dirtside/games", new { name = "Ridge 9" });
        Assert.Equal(HttpStatusCode.NotFound, stillOff.StatusCode);

        var ready = await client.GetFromJsonAsync<JsonElement>("/ready");
        var warnings = ready.GetProperty("warnings").EnumerateArray().Select(w => w.GetString() ?? string.Empty).ToArray();
        Assert.Contains(warnings, warning => warning.Contains("StarGrunt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithAFlagOn_TheFullThrustWorkflowIsUnchanged()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:StarGrunt"] = "true",
            ["Features:Dirtside"] = "true",
        });
        using var client = factory.CreateClient();

        // The whole point of the flag is that turning an engine on cannot disturb the game people
        // actually came to play. Creating and joining a match must behave exactly as before.
        var created = await (await client.PostAsJsonAsync("/api/matches",
            new Contracts.Matches.CreateMatchRequest("Blue", "Unchanged", 72, 48)))
            .Content.ReadFromJsonAsync<Contracts.Matches.MatchCreatedResponse>();
        Assert.NotNull(created);

        var joined = await (await client.PostAsJsonAsync("/api/matches/join",
            new Contracts.Matches.JoinMatchRequest(created.JoinCode, "Red")))
            .Content.ReadFromJsonAsync<Contracts.Matches.MatchJoinedResponse>();
        Assert.NotNull(joined);
        Assert.Equal(created.MatchId, joined.MatchId);
    }

    private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? settings = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                if (settings is not null)
                {
                    builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings));
                }
            });

    private sealed class StubConfiguration : Dictionary<string, string>, FeatureFlags.IConfigurationSource
    {
        public string? GetValue(string key) => TryGetValue(key, out var value) ? value : null;
    }
}
