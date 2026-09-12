using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Features;
using ForceSignal.Contracts.Ground;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ForceSignal.TestSupport;

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

        using var created = await client.PostAsJsonAsync("/api/dirtside/games", DirtsideTestProfile.CreateGame("Ridge 9"));

        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);

        // And the client is told so rather than having to discover it from a 404. This used to be
        // asserted through `GET /api/dirtside/status`, which was removed: it returned a compile-time
        // literal, so the only thing it could establish was that the route had been mapped, which is
        // what `/api/features` reports from the same FeatureFlags instance that decides the mapping.
        var flags = await client.GetFromJsonAsync<FeatureFlagsDto>("/api/features");
        Assert.False(flags!.Dirtside);
    }

    [Fact]
    public async Task CreatingAGameHandsBackItsTokenOnceAndTheSnapshotNeverCarriesIt()
    {
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/dirtside/games", DirtsideTestProfile.CreateGame("Ridge 9"));
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
    public async Task TheOnlyRouteOpenWithoutAGameTokenIsTheOneThatIssuesIt()
    {
        // This replaces `TheStatusRouteStaysOpen`. `GET /api/dirtside/status` is gone. It said
        // `"playable"`, a third hand-written description of a capability `/api/features` and the
        // `/ready` warnings already carry - and one that had already drifted, because `/ready` lists
        // opportunity fire, area-defence interception and indirect fire as unreachable in the same
        // engine this route called playable. It also returned identical bytes from an engine whose
        // store had fallen back to memory, so it could reassure an operator about a machine that was
        // losing every Dirtside game at the next restart.
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/dirtside/games", DirtsideTestProfile.CreateGame("Ridge 9"));
        created.EnsureSuccessStatusCode();

        var body = await created.Content.ReadFromJsonAsync<DirtsideGameCreatedResponse>(JsonOptions);
        Assert.NotNull(body);

        // Reading it back without the token it just issued is refused, so the create route really is
        // the only open door rather than one of several. 401 rather than 403: no header at all is a
        // missing credential, where a header that does not open this game is the 403 above.
        using var withoutToken = await client.GetAsync(new Uri($"/api/dirtside/games/{body.GameId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
    }

    [Fact]
    public async Task TheRemovedStatusRouteIsGoneWithTheFlagOnAsWellAsOff()
    {
        // The control on the removal. A 404 with the flag off proves nothing - every Dirtside route
        // 404s then - so this asks with the flag *on*, where a route that still existed would answer.
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();

        using var status = await client.GetAsync(new Uri("/api/dirtside/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

        var flags = await client.GetFromJsonAsync<FeatureFlagsDto>("/api/features");
        Assert.True(flags!.Dirtside);
    }

    /// <summary>Starts a game and leaves its token on the client for everything after.</summary>
    private static async Task<Guid> Create(HttpClient client)
    {
        var created = await (await client.PostAsJsonAsync("/api/dirtside/games", DirtsideTestProfile.CreateGame("Ridge 9")))
            .Content.ReadFromJsonAsync<DirtsideGameCreatedResponse>(JsonOptions);
        Assert.NotNull(created);
        client.DefaultRequestHeaders.Remove(TokenHeader);
        client.DefaultRequestHeaders.Add(TokenHeader, created.Token);
        return created.GameId;
    }

    [Fact]
    public async Task AnEligibleInterceptionIsReachableAndIsRefusedWithTheRulesThatAreMissing()
    {
        // The route exists in order to refuse, and this is the refusal. Every gate is passed on the
        // wire - the platoon is on the table, the vehicle is whole, it spent its combat action on
        // live sensors, and the game's profile carries a reach - and the answer is still a 400,
        // because what an interception rolls and what a success does to the shot are written in no
        // rulebook this app has been given.
        //
        // The alternative was to leave the feature unreachable, which is what it was: a checkbox on
        // the roster, a combat action that bought a flag, and nothing at the end of either. A table
        // that spends an action deserves to be able to find out what it bought.
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await TableWithTwoPlatoons(client);

        await Post(client, $"/api/dirtside/games/{game}/turns", new { });
        await Post(client, $"/api/dirtside/games/{game}/turns/current/first-activator",
            new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        await Post(client, $"/api/dirtside/games/{game}/activations", new BeginDirtsideActivationRequest("blue", "alpha"));

        var sensing = await Post(client, $"/api/dirtside/games/{game}/activations/current/sensors",
            new DirtsideSensorsRequest("alpha-1", Live: true));

        // Eligibility is a real yes, and it is the half of interception this app can answer.
        var eligible = Element(sensing!, "alpha", "alpha-1");
        Assert.True(eligible.AreaDefenceSensorsLive);
        Assert.True(eligible.CanIntercept);
        Assert.Null(eligible.WhyItCannotIntercept);

        using var intercepted = await client.PostAsJsonAsync(
            $"/api/dirtside/games/{game}/interceptions", new DirtsideInterceptRequest("alpha", "alpha-1"));

        Assert.Equal(HttpStatusCode.BadRequest, intercepted.StatusCode);
        var problem = await intercepted.Content.ReadAsStringAsync();

        // The four sentences, named. A refusal that said only "not implemented" would send the
        // reader to this repository's backlog; these send them to their own rulebook, which is the
        // only place the answer exists.
        Assert.Contains("will not guess", problem, StringComparison.Ordinal);
        Assert.Contains("when a defender may declare", problem, StringComparison.Ordinal);
        Assert.Contains("what a success does", problem, StringComparison.Ordinal);
        Assert.Contains("more than once a turn", problem, StringComparison.Ordinal);

        // And not the sequencing artefact it used to answer with. "No window is waiting for an
        // answer" reads as "wait, and one will", and none ever will.
        Assert.DoesNotContain("No window is waiting", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInterceptorWhoseTableNeverEnteredAReachIsNotEligibleAndIsToldWhichEntry()
    {
        // The content policy, on the one piece of interception that is a number. The reach is the
        // players' and this app ships none, so a game whose profile does not carry one does not get
        // a plausible distance - it gets the name of the entry, on the snapshot beside the vehicle
        // and in the refusal, in the same words.
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync(
                "/api/dirtside/games", DirtsideTestProfile.CreateGameWithNoAreaDefenceReach("Ridge 9")))
            .Content.ReadFromJsonAsync<DirtsideGameCreatedResponse>(JsonOptions);
        Assert.NotNull(created);
        client.DefaultRequestHeaders.Remove(TokenHeader);
        client.DefaultRequestHeaders.Add(TokenHeader, created.Token);
        var game = created.GameId;

        await Post(client, $"/api/dirtside/games/{game}/units", Platoon("alpha", "Alpha Troop", "blue"));
        await Post(client, $"/api/dirtside/games/{game}/units", Platoon("bravo", "Bravo Troop", "red"));
        await Post(client, $"/api/dirtside/games/{game}/turns", new { });
        await Post(client, $"/api/dirtside/games/{game}/turns/current/first-activator",
            new ChooseDirtsideFirstActivatorRequest("blue", TakeIt: true));
        await Post(client, $"/api/dirtside/games/{game}/activations", new BeginDirtsideActivationRequest("blue", "alpha"));

        var sensing = await Post(client, $"/api/dirtside/games/{game}/activations/current/sensors",
            new DirtsideSensorsRequest("alpha-1", Live: true));

        var vehicle = Element(sensing!, "alpha", "alpha-1");
        Assert.True(vehicle.AreaDefenceSensorsLive);
        Assert.False(vehicle.CanIntercept);
        Assert.Contains("reach", vehicle.WhyItCannotIntercept!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rules profile", vehicle.WhyItCannotIntercept, StringComparison.Ordinal);

        using var intercepted = await client.PostAsJsonAsync(
            $"/api/dirtside/games/{game}/interceptions", new DirtsideInterceptRequest("alpha", "alpha-1"));

        Assert.Equal(HttpStatusCode.BadRequest, intercepted.StatusCode);
        var problem = await intercepted.Content.ReadAsStringAsync();

        // The most specific true reason, not the deepest one. "Enter your reach" is something the
        // table can act on now; the four missing rules are not, and leading with them would bury the
        // one line of this refusal anybody can do anything about.
        Assert.Contains("how far an area-defence system reaches", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAreaDefenceReachComesBackOnTheProfileTheWayItWasEntered()
    {
        // The round trip, because a number a table types in and cannot read back is a number they
        // cannot check - and this one has no other reader that would show it was wrong.
        using var factory = CreateFactory(dirtside: true);
        using var client = factory.CreateClient();
        var game = await Create(client);

        var snapshot = await client.GetFromJsonAsync<DirtsideSnapshotDto>(
            $"/api/dirtside/games/{game}", JsonOptions);

        Assert.Equal(9, snapshot!.Profile!.AreaDefenceReach);
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
