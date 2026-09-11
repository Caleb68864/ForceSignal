using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Features;
using ForceSignal.Contracts.Ground;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Covers the StarGrunt routes, the promise the feature flag makes about them, and the token that
/// guards every one of them but the two that need no game.
/// </summary>
public sealed class StarGruntEndpointTests
{
    private const string TokenHeader = "X-Game-Token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task WithTheFlagOffTheRoutesAreAbsentRatherThanDisabled()
    {
        using var factory = CreateFactory(starGrunt: false);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/stargrunt/games", new CreateStarGruntGameRequest("Hill 43"));

        // Absent, not merely refused. This is the flag's whole promise: a half-built engine cannot
        // get in the way of a Full Thrust game someone turned up to play.
        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);

        // And the client is told so rather than having to discover it from a 404. This used to be
        // asserted through `GET /api/stargrunt/status`, which was removed: it returned a
        // compile-time literal, so the only thing it could establish was that the route had been
        // mapped, which is what `/api/features` reports from the same FeatureFlags instance that
        // decides the mapping.
        var flags = await client.GetFromJsonAsync<FeatureFlagsDto>("/api/features");
        Assert.False(flags!.StarGrunt);
    }

    [Fact]
    public async Task CreatingAGameHandsBackItsTokenOnceAndTheSnapshotNeverCarriesIt()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/stargrunt/games", new CreateStarGruntGameRequest("Hill 43"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var token = body.GetProperty("token").GetString();
        Assert.Matches("^[0-9a-f]{64}$", token);
        Assert.False(body.GetProperty("snapshot").TryGetProperty("token", out _));

        // And the snapshot read back by that token does not carry it either.
        client.DefaultRequestHeaders.Add(TokenHeader, token);
        var snapshot = await client.GetFromJsonAsync<JsonElement>($"/api/stargrunt/games/{body.GetProperty("gameId").GetGuid()}");
        Assert.False(snapshot.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task AGameCanBePlayedOverHttp()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        await Post(client, $"/api/stargrunt/games/{game}/units", Squad("alpha", "Alpha Squad", "blue"));
        await Post(client, $"/api/stargrunt/games/{game}/units", Squad("bravo", "Bravo Squad", "red"));
        await Post(client, $"/api/stargrunt/games/{game}/turns/begin", new { });
        await Post(client, $"/api/stargrunt/games/{game}/turns/current/first-activator", new ChooseFirstActivatorRequest("blue", true));
        await Post(client, $"/api/stargrunt/games/{game}/activations", new BeginStarGruntActivationRequest("blue", "alpha"));

        var fired = await Post(client, $"/api/stargrunt/games/{game}/activations/current/fire", new StarGruntFireRequest(
            "alpha", "bravo", "Rifles", 10, [], 9, "Soft"));

        Assert.NotEmpty(fired!.Log);
        Assert.Equal("alpha", fired.ActivatingUnitId);
    }

    [Fact]
    public async Task ASnapshotCarriesTheLegalityTheScreenNeeds()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();
        var game = await TableWithTwoSquads(client);

        await Post(client, $"/api/stargrunt/games/{game}/turns/begin", new { });
        var snapshot = await Post(client, $"/api/stargrunt/games/{game}/turns/current/first-activator",
            new ChooseFirstActivatorRequest("blue", true));

        var alpha = snapshot!.Units.Single(unit => unit.Id == "alpha");
        var bravo = snapshot.Units.Single(unit => unit.Id == "bravo");

        Assert.True(alpha.CanActivate);
        Assert.False(bravo.CanActivate);
        Assert.False(string.IsNullOrWhiteSpace(bravo.ActivationBlocker));
        Assert.NotEmpty(alpha.WeaponLegality);
    }

    [Fact]
    public async Task ARefusedCommandComesBackAsAProblem()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();
        var game = await TableWithTwoSquads(client);
        await Post(client, $"/api/stargrunt/games/{game}/turns/begin", new { });
        await Post(client, $"/api/stargrunt/games/{game}/turns/current/first-activator", new ChooseFirstActivatorRequest("blue", true));

        using var response = await client.PostAsJsonAsync(
            $"/api/stargrunt/games/{game}/activations",
            new BeginStarGruntActivationRequest("red", "bravo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task ADieThatIsNotOnTheLadderIsRefused()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/stargrunt/games/{game}/units",
            new AddStarGruntUnitRequest("alpha", "Alpha", "blue", "Squad", 7, 2, [], []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AGameNobodyStartedIsNotFound()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TokenHeader, new string('0', 64));

        using var response = await client.GetAsync(new Uri($"/api/stargrunt/games/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutTheTokenAGameCannotBeReadOrMoved()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        // The id is not a secret - it is in the URL, the proxy log and the browser history - so
        // the id alone opens nothing. A missing header is a 401 that names the header; a blank
        // one is the same as missing.
        client.DefaultRequestHeaders.Remove(TokenHeader);
        using var read = await client.GetAsync(new Uri($"/api/stargrunt/games/{game}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        var problem = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(TokenHeader, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var move = await client.PostAsJsonAsync($"/api/stargrunt/games/{game}/turns/begin", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, move.StatusCode);

        client.DefaultRequestHeaders.Add(TokenHeader, "   ");
        using var blank = await client.GetAsync(new Uri($"/api/stargrunt/games/{game}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, blank.StatusCode);
    }

    [Fact]
    public async Task TheWrongTokenIsRefused_IncludingAnotherGamesToken()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        // A token from a different game is no better than a made-up one.
        var other = await Create(client);
        Assert.NotEqual(game, other);
        using var read = await client.GetAsync(new Uri($"/api/stargrunt/games/{game}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        client.DefaultRequestHeaders.Remove(TokenHeader);
        client.DefaultRequestHeaders.Add(TokenHeader, new string('f', 64));
        using var move = await client.PostAsJsonAsync($"/api/stargrunt/games/{game}/turns/begin", new { });
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);
    }

    [Fact]
    public async Task TheOnlyRouteOpenWithoutAGameTokenIsTheOneThatIssuesIt()
    {
        // This replaces `TheStatusRouteStaysOpen`. `GET /api/stargrunt/status` is gone: it returned
        // a compile-time literal naming the engine and a readiness word, so it could only ever prove
        // that the route had been mapped - and the readiness word was a third hand-written
        // description of a capability `/api/features` and the `/ready` warnings already carry.
        //
        // What is worth asserting is the thing the old test was standing in for: the create route is
        // open because that is where the token comes from, and nothing else is.
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/stargrunt/games", new CreateStarGruntGameRequest("Hill 43"));
        created.EnsureSuccessStatusCode();

        var body = await created.Content.ReadFromJsonAsync<StarGruntGameCreatedResponse>(JsonOptions);
        Assert.NotNull(body);

        // Reading it back without the token it just issued is refused, so the create route really is
        // the only open door rather than one of several. 401 rather than 403: no header at all is a
        // missing credential, where a header that does not open this game is the 403 above.
        using var withoutToken = await client.GetAsync(new Uri($"/api/stargrunt/games/{body.GameId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
    }

    [Fact]
    public async Task TheRemovedStatusRouteIsGoneWithTheFlagOnAsWellAsOff()
    {
        // The control on the removal. A 404 with the flag off proves nothing - every StarGrunt route
        // 404s then - so this asks with the flag *on*, where a route that still existed would answer.
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();

        using var status = await client.GetAsync(new Uri("/api/stargrunt/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

        // And the route it was standing in for does answer, so this is not a factory that failed to
        // mount anything.
        var flags = await client.GetFromJsonAsync<FeatureFlagsDto>("/api/features");
        Assert.True(flags!.StarGrunt);
    }

    /// <summary>Starts a game and leaves its token on the client for everything after.</summary>
    private static async Task<Guid> Create(HttpClient client)
    {
        var created = await (await client.PostAsJsonAsync("/api/stargrunt/games", new CreateStarGruntGameRequest("Hill 43")))
            .Content.ReadFromJsonAsync<StarGruntGameCreatedResponse>(JsonOptions);
        Assert.NotNull(created);
        client.DefaultRequestHeaders.Remove(TokenHeader);
        client.DefaultRequestHeaders.Add(TokenHeader, created.Token);
        return created.GameId;
    }

    private static async Task<Guid> TableWithTwoSquads(HttpClient client)
    {
        var game = await Create(client);
        await Post(client, $"/api/stargrunt/games/{game}/units", Squad("alpha", "Alpha Squad", "blue"));
        await Post(client, $"/api/stargrunt/games/{game}/units", Squad("bravo", "Bravo Squad", "red"));
        return game;
    }

    private static async Task<StarGruntSnapshotDto?> Post(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StarGruntSnapshotDto>(JsonOptions);
    }

    private static AddStarGruntUnitRequest Squad(string id, string name, string side) => new(
        id,
        name,
        side,
        "Squad",
        QualityDie: 8,
        LeadershipValue: 2,
        Figures: [.. Enumerable.Repeat(new StarGruntFigureDto(6), 8)],
        Weapons: [new StarGruntWeaponDto("Rifles", 10)]);

    private static WebApplicationFactory<Program> CreateFactory(bool starGrunt) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Features:StarGrunt", starGrunt ? "true" : "false");
        });
}
