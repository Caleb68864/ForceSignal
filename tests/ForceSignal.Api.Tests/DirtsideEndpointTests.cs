using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Ground;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Covers the Dirtside routes, the promise the feature flag makes about them, and the token that
/// guards every one of them but the two that need no game.
/// </summary>
/// <remarks>
/// The dice and the chit pot here are the real ones, so nothing asserts what a shot did - only
/// that every route answers, that a refusal is a problem rather than a fault, and that a turn can
/// be walked from beginning to end over the wire.
/// </remarks>
public sealed class DirtsideEndpointTests
{
    private const string TokenHeader = "X-Game-Token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task WithTheFlagOffTheRoutesAreAbsentRatherThanDisabled()
    {
        using var factory = CreateFactory(dirtside: false);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/dirtside/games", new CreateDirtsideGameRequest("Ridge 9"));
        using var status = await client.GetAsync(new Uri("/api/dirtside/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
    }

    [Fact]
    public async Task CreatingAGameHandsBackItsTokenOnceAndTheSnapshotNeverCarriesIt()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/dirtside/games", new CreateDirtsideGameRequest("Ridge 9"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Matches("^[0-9a-f]{64}$", body.GetProperty("token").GetString());
        Assert.False(body.GetProperty("snapshot").TryGetProperty("token", out _));
    }

    [Fact]
    public async Task AWholeTurnCanBeWalkedOverHttp()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await TableWithTwoPlatoons(client);

        var opened = await Post(client, $"/api/dirtside/games/{game}/turns", new { });
        Assert.Equal("ChoosingFirstActivator", opened!.Phase);

        var chosen = await Post(client, $"/api/dirtside/games/{game}/turns/current/first-activator",
            new ChooseDirtsideFirstActivatorRequest("blue", true));
        Assert.Equal("blue", chosen!.ActiveSide);

        var activated = await Post(client, $"/api/dirtside/games/{game}/activations", new BeginDirtsideActivationRequest("blue", "alpha"));
        Assert.Equal("alpha", activated!.ActivatingUnitId);
        Assert.Contains("alpha-1", activated.ElementsStillToChoose);

        // One vehicle shoots; the other moves and then puts its sensors up. Every element has then
        // said what it is doing, which is what lets the activation close.
        var fired = await Post(client, $"/api/dirtside/games/{game}/activations/current/fire",
            new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close"));
        Assert.NotEmpty(fired!.Log);
        Assert.True(Element(fired, "alpha", "alpha-1").HasTakenCombatAction);

        var moved = await Post(client, $"/api/dirtside/games/{game}/activations/current/moves",
            new MoveDirtsideElementRequest("alpha-2", OverHalfItsMovement: true));
        Assert.True(Element(moved!, "alpha", "alpha-2").MovedOverHalf);

        var sensing = await Post(client, $"/api/dirtside/games/{game}/activations/current/sensors",
            new DirtsideSensorsRequest("alpha-2", Live: true));
        Assert.True(Element(sensing!, "alpha", "alpha-2").AreaDefenceSensorsLive);
        Assert.True(sensing!.CanEndActivation);

        var closed = await Post(client, $"/api/dirtside/games/{game}/activations/current/end", new { });
        Assert.Null(closed!.ActivatingUnitId);

        // Red cannot pass with as many unactivated as blue has left, and the refusal is a problem.
        using var pass = await client.PostAsJsonAsync($"/api/dirtside/games/{game}/turns/current/pass", new DirtsidePassRequest("red"));
        Assert.Equal(HttpStatusCode.BadRequest, pass.StatusCode);

        await Post(client, $"/api/dirtside/games/{game}/activations", new BeginDirtsideActivationRequest("red", "bravo"));
        await Post(client, $"/api/dirtside/games/{game}/activations/current/stand-down", new DirtsideStandDownRequest("bravo-1"));
        await Post(client, $"/api/dirtside/games/{game}/activations/current/stand-down", new DirtsideStandDownRequest("bravo-2"));
        await Post(client, $"/api/dirtside/games/{game}/activations/current/end", new { });

        var ended = await Post(client, $"/api/dirtside/games/{game}/turns/current/end", new { });
        Assert.Equal("TurnEnded", ended!.Phase);

        // And the whole thing reads back by the token.
        var read = await client.GetFromJsonAsync<DirtsideSnapshotDto>($"/api/dirtside/games/{game}", JsonOptions);
        Assert.Equal(ended.Version, read!.Version);
    }

    [Fact]
    public async Task AWholeAssaultCanBeFoughtOverHttp()
    {
        // The dice are real, so the assault is walked with whatever they give: a launch that goes
        // in or does not, a defender that stands or does not. Each answer says which, and every
        // route answers with a snapshot rather than a fault. What is asserted is the shape of each
        // step and that the sequence refuses a step out of its turn.
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await TableWithTwoPlatoons(client);
        await Post(client, $"/api/dirtside/games/{game}/turns", new { });
        await Post(client, $"/api/dirtside/games/{game}/turns/current/first-activator", new ChooseDirtsideFirstActivatorRequest("blue", true));
        await Post(client, $"/api/dirtside/games/{game}/activations", new BeginDirtsideActivationRequest("blue", "alpha"));

        // A round before a launch is out of sequence, and the refusal is a problem.
        using var early = await client.PostAsJsonAsync($"/api/dirtside/games/{game}/assaults/round", new { });
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);

        var launched = await Post(client, $"/api/dirtside/games/{game}/assaults/launch",
            new LaunchDirtsideAssaultRequest("bravo", ["alpha-1", "alpha-2"], 0, new DirtsideValidityDto("All", "FaceValue")));
        Assert.True(Element(launched!, "alpha", "alpha-1").HasTakenCombatAction);
        Assert.True(Element(launched!, "alpha", "alpha-2").HasTakenCombatAction);

        if (launched!.Assault is null)
        {
            // The troops would not go. The combat action is spent, the log says so, and there is
            // nothing more to fight.
            Assert.Contains(launched.Log, entry => entry.Contains("would not go", StringComparison.Ordinal));
            return;
        }

        Assert.Equal("AwaitingDefender", launched.Assault.Stage);
        Assert.True(launched.Units.Single(unit => unit.Id == "bravo").HasActivated);

        var stood = await Post(client, $"/api/dirtside/games/{game}/assaults/stand",
            new DirtsideAssaultStandRequest(["bravo-1", "bravo-2"], 0, new DirtsideValidityDto("All", "FaceValue")));
        Assert.NotNull(stood!.Assault);

        if (stood.Assault.Stage == "AwaitingFollowThrough")
        {
            // The defender gave way. Its marker is worse for it, and the attacker may test to go on.
            var through = await Post(client, $"/api/dirtside/games/{game}/assaults/follow-through", new DirtsideFollowThroughRequest(0));
            Assert.Null(through!.Assault);
            return;
        }

        Assert.Equal("AwaitingRound", stood.Assault.Stage);

        var fought = await Post(client, $"/api/dirtside/games/{game}/assaults/round", new { });
        Assert.Equal("AwaitingAftermath", fought!.Assault!.Stage);
        Assert.Contains(fought.Log, entry => entry.Contains("Round 1", StringComparison.Ordinal));

        var settled = await Post(client, $"/api/dirtside/games/{game}/assaults/aftermath", new DirtsideAssaultAftermathRequest(1, 3));
        Assert.True(settled!.Assault is null || settled.Assault.Stage is "AwaitingRound" or "AwaitingFollowThrough");
    }

    [Fact]
    public async Task RecoveringSystemsIsRefusedWhenNothingIsDown()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await TableWithTwoPlatoons(client);
        await Post(client, $"/api/dirtside/games/{game}/turns", new { });
        await Post(client, $"/api/dirtside/games/{game}/turns/current/first-activator", new ChooseDirtsideFirstActivatorRequest("blue", true));
        var activated = await Post(client, $"/api/dirtside/games/{game}/activations", new BeginDirtsideActivationRequest("blue", "alpha"));

        // The snapshot says the crew could not try, in the words the route refuses with.
        var alphaOne = Element(activated!, "alpha", "alpha-1");
        Assert.False(alphaOne.CanRecoverSystems);

        using var response = await client.PostAsJsonAsync(
            $"/api/dirtside/games/{game}/activations/current/recover-systems",
            new DirtsideRecoverSystemsRequest("alpha-1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(alphaOne.WhyItCannotRecoverSystems, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TheAssaultRoutesNeedTheToken()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await Create(client);
        client.DefaultRequestHeaders.Remove(TokenHeader);

        foreach (var path in new[] { "assaults/launch", "assaults/stand", "assaults/round", "assaults/aftermath", "assaults/follow-through", "activations/current/recover-systems" })
        {
            using var response = await client.PostAsJsonAsync($"/api/dirtside/games/{game}/{path}", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ARefusedCommandComesBackAsAProblem()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await TableWithTwoPlatoons(client);
        await Post(client, $"/api/dirtside/games/{game}/turns", new { });
        await Post(client, $"/api/dirtside/games/{game}/turns/current/first-activator", new ChooseDirtsideFirstActivatorRequest("blue", true));

        using var response = await client.PostAsJsonAsync(
            $"/api/dirtside/games/{game}/activations",
            new BeginDirtsideActivationRequest("red", "bravo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task AKindThatIsNotOnTheConfidenceTableIsRefused()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/dirtside/games/{game}/units",
            Platoon("alpha", "Alpha Troop", "blue") with { Kind = "Cavalry" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AGameNobodyStartedIsNotFound()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TokenHeader, new string('0', 64));

        using var response = await client.GetAsync(new Uri($"/api/dirtside/games/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutTheTokenAGameCannotBeReadOrMoved()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        client.DefaultRequestHeaders.Remove(TokenHeader);
        using var read = await client.GetAsync(new Uri($"/api/dirtside/games/{game}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);

        using var move = await client.PostAsJsonAsync($"/api/dirtside/games/{game}/turns", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, move.StatusCode);
    }

    [Fact]
    public async Task TheWrongTokenIsRefused()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        client.DefaultRequestHeaders.Remove(TokenHeader);
        client.DefaultRequestHeaders.Add(TokenHeader, new string('f', 64));
        using var read = await client.GetAsync(new Uri($"/api/dirtside/games/{game}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        using var move = await client.PostAsJsonAsync($"/api/dirtside/games/{game}/turns", new { });
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);
    }

    [Fact]
    public async Task TheStatusRouteStaysOpen()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();

        using var status = await client.GetAsync(new Uri("/api/dirtside/status", UriKind.Relative));

        status.EnsureSuccessStatusCode();
    }

    /// <summary>Starts a game and leaves its token on the client for everything after.</summary>
    private static async Task<Guid> Create(HttpClient client)
    {
        var created = await (await client.PostAsJsonAsync("/api/dirtside/games", new CreateDirtsideGameRequest("Ridge 9")))
            .Content.ReadFromJsonAsync<DirtsideGameCreatedResponse>(JsonOptions);
        Assert.NotNull(created);
        client.DefaultRequestHeaders.Remove(TokenHeader);
        client.DefaultRequestHeaders.Add(TokenHeader, created.Token);
        return created.GameId;
    }

    private static async Task<Guid> TableWithTwoPlatoons(HttpClient client)
    {
        var game = await Create(client);
        await Post(client, $"/api/dirtside/games/{game}/units", Platoon("alpha", "Alpha Troop", "blue"));
        await Post(client, $"/api/dirtside/games/{game}/units", Platoon("bravo", "Bravo Troop", "red"));
        return game;
    }

    private static async Task<DirtsideSnapshotDto?> Post(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DirtsideSnapshotDto>(JsonOptions);
    }

    private static DirtsideElementStateDto Element(DirtsideSnapshotDto snapshot, string unit, string element) =>
        snapshot.Units.Single(u => u.Id == unit).Elements.Single(e => e.Id == element);

    /// <summary>
    /// Two vehicles with a gun that can hurt anything at any range, a die and a leadership on the
    /// command marker, and the two numbers an assault reads off each card. Every number is invented.
    /// </summary>
    private static AddDirtsidePlatoonRequest Platoon(string id, string name, string side) => new(
        id,
        name,
        side,
        "Armour",
        IsCybertank: false,
        Elements:
        [
            new DirtsideElementDto($"{id}-1", $"{name} One", "Basic", 3, 3, 12, [MainGun], AssaultChits: 3, KillThreshold: 4),
            new DirtsideElementDto($"{id}-2", $"{name} Two", "Basic", 3, 3, 12, [MainGun], AssaultChits: 3, KillThreshold: 4),
        ],
        QualityDie: "D8",
        LeadershipValue: 2);

    private static DirtsideWeaponDto MainGun { get; } = new(
        "Main Gun",
        ChitCount: 3,
        Close: new DirtsideValidityDto("All", "FaceValue"),
        Medium: new DirtsideValidityDto("All", "FaceValue"),
        Long: new DirtsideValidityDto("All", "FaceValue"));

    private static WebApplicationFactory<Program> CreateFactory(bool dirtside) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Features:Dirtside", dirtside ? "true" : "false");
        });
}
