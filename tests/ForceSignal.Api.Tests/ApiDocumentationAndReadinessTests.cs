using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ForceSignal.Api.Tests;

public sealed class ApiDocumentationAndReadinessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task DevelopmentHost_ExposesHealthReadinessAndApiDocumentation()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health");
        health.EnsureSuccessStatusCode();
        var healthBody = await health.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", healthBody.GetProperty("status").GetString());

        using var ready = await client.GetAsync("/ready");
        ready.EnsureSuccessStatusCode();
        var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", readyBody.GetProperty("status").GetString());
        Assert.Equal("Development", readyBody.GetProperty("environment").GetString());

        using var openApi = await client.GetAsync("/openapi/v1.json");
        openApi.EnsureSuccessStatusCode();
        var openApiBody = await openApi.Content.ReadAsStringAsync();
        Assert.Contains("\"/api/matches\"", openApiBody, StringComparison.Ordinal);

        using var scalar = await client.GetAsync("/scalar/v1");
        scalar.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ApiResponses_IncludeSecurityHeaders()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=()",
            Assert.Single(response.Headers.GetValues("Permissions-Policy")));
    }

    [Fact]
    public async Task ProductionHost_WithConfiguredOrigin_AllowsOnlyThatCorsOrigin()
    {
        var previousOrigin = Environment.GetEnvironmentVariable("Cors__AllowedOrigins__0");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:6297");

        try
        {
            using var factory = CreateFactory("Production");
            using var client = factory.CreateClient();

            using var ready = await client.GetAsync("/ready");
            ready.EnsureSuccessStatusCode();
            var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Production", readyBody.GetProperty("environment").GetString());
            Assert.Equal("configured", readyBody.GetProperty("cors").GetString());
            // With no database configured, readiness has to say so - that warning is the only thing
            // telling an operator their game will not survive a restart.
            Assert.Equal("in-memory", readyBody.GetProperty("persistence").GetString());
            Assert.Contains(
                readyBody.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString() ?? string.Empty),
                warning => warning.Contains("stored in memory", StringComparison.OrdinalIgnoreCase));

            using var allowed = await SendPreflight(client, "http://localhost:6297");
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
            Assert.True(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOrigins));
            Assert.Equal("http://localhost:6297", Assert.Single(allowedOrigins));

            using var denied = await SendPreflight(client, "http://example.invalid");
            Assert.Equal(HttpStatusCode.NoContent, denied.StatusCode);
            Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", previousOrigin);
        }
    }

    [Fact]
    public async Task ProductionHost_WithOriginsInOneVariable_ReadsThemRatherThanRefusingToStart()
    {
        // Cors__AllowedOrigins=a,b is the spelling an operator reaches for first, and it is what a
        // container platform's single-value setting form forces. The environment provider turns the
        // "__" into ":" before anything reads it, so this is exactly the key that arrives; the
        // indexed form the compose file uses is tested above. Both have to work, because the API
        // fails fast outside Development when it finds no origins, and failing fast over a setting
        // that was supplied is the worst version of that.
        var previousOrigin = Environment.GetEnvironmentVariable("Cors__AllowedOrigins");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins", "http://localhost:6297, http://board.invalid");

        try
        {
            using var factory = CreateFactory("Production");
            using var client = factory.CreateClient();

            using var ready = await client.GetAsync("/ready");
            ready.EnsureSuccessStatusCode();
            var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("configured", readyBody.GetProperty("cors").GetString());

            using var allowed = await SendPreflight(client, "http://board.invalid");
            Assert.True(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOrigins));
            Assert.Equal("http://board.invalid", Assert.Single(allowedOrigins));

            using var denied = await SendPreflight(client, "http://example.invalid");
            Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins", previousOrigin);
        }
    }

    [Fact]
    public async Task ProductionHost_WithTheDocumentedWebOriginVariable_StartsRatherThanRefusing()
    {
        // FORCESIGNAL_WEB_ORIGIN is the name .env.example and the README tell an operator to set,
        // and compose interpolates it into Cors__AllowedOrigins__0 - but an API started without
        // compose sees only the name the operator set. Every other documented FORCESIGNAL_ name is
        // read directly by the code as well: FORCESIGNAL_MATCH_DB and the two feature flags both
        // are. CORS instead read FORCESIGNAL_CORS_ALLOWED_ORIGINS, a name that appears in no
        // document, no compose file and no test - so the one variable the docs name did nothing, and
        // the API refused to start citing a setting that had been supplied.
        var previous = Environment.GetEnvironmentVariable("FORCESIGNAL_WEB_ORIGIN");
        Environment.SetEnvironmentVariable("FORCESIGNAL_WEB_ORIGIN", "http://board.invalid");

        try
        {
            using var factory = CreateFactory("Production");
            using var client = factory.CreateClient();

            using var ready = await client.GetAsync("/ready");
            ready.EnsureSuccessStatusCode();
            var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("configured", readyBody.GetProperty("cors").GetString());

            using var allowed = await SendPreflight(client, "http://board.invalid");
            Assert.True(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOrigins));
            Assert.Equal("http://board.invalid", Assert.Single(allowedOrigins));

            using var denied = await SendPreflight(client, "http://example.invalid");
            Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORCESIGNAL_WEB_ORIGIN", previous);
        }
    }

    [Fact]
    public async Task StarGruntReadiness_NamesTheMovesTheEngineHasAndTheApiDoesNot()
    {
        // Dirtside's warning names its slice boundary and StarGrunt's said only "in development",
        // which reads as "rough" rather than "these two moves are not here". StarGruntTurn plays and
        // tests both of them, so a table reading the module has every reason to look for them.
        using var factory = CreateFactory("Development", new Dictionary<string, string?> { ["Features:StarGrunt"] = "true" });
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/ready");
        ready.EnsureSuccessStatusCode();
        var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
        var warning = Assert.Single(
            readyBody.GetProperty("warnings").EnumerateArray().Select(entry => entry.GetString() ?? string.Empty),
            entry => entry.StartsWith("StarGrunt ground combat is enabled", StringComparison.Ordinal));

        Assert.Contains("transferring an activation to a subordinate and reaction fire are not yet reachable", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirtsideReadiness_NamesOnlyWhatIsStillUnreachable()
    {
        // The warning is a to-do list an operator reads before a game. Close assault and
        // systems-down recovery reached the table, so it must stop naming them and keep naming what
        // has not: opportunity fire and indirect fire.
        //
        // Area-defence interception came off this row and got one of its own, because it stopped
        // being the same kind of unfinished. The other two are work nobody has done; interception is
        // everything this app can build, built and reachable, stopped at a question only a rulebook
        // answers. Listing it as "not yet reachable" would have been wrong in both directions - the
        // route answers, and it will never do more until somebody writes the rules down.
        using var factory = CreateFactory("Development", new Dictionary<string, string?> { ["Features:Dirtside"] = "true" });
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/ready");
        ready.EnsureSuccessStatusCode();
        var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
        var warning = Assert.Single(
            readyBody.GetProperty("warnings").EnumerateArray().Select(entry => entry.GetString() ?? string.Empty),
            entry => entry.StartsWith("Dirtside ground combat is enabled", StringComparison.Ordinal));

        Assert.Contains("close assault", warning[..warning.IndexOf(';', StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("systems-down recovery", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opportunity fire and indirect fire are not yet reachable", warning, StringComparison.Ordinal);

        // And it is no longer named on this row at all, rather than named twice in two different
        // tenses - which is how the removed /status route's "playable" came to contradict /ready.
        Assert.DoesNotContain("interception", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirtsideReadiness_SaysInterceptionIsRefusedAndWhichRulesAreMissing()
    {
        // The row interception got to itself. An operator reading this before a game has to learn
        // three things that are all true at once and easy to state as one false thing: the route
        // answers, it always refuses, and the reason is a rulebook question rather than a bug or a
        // backlog item. The half a table CAN buy - sensors on, reach entered, eligibility reported -
        // is named too, because that half cost somebody a combat action.
        using var factory = CreateFactory("Development", new Dictionary<string, string?> { ["Features:Dirtside"] = "true" });
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/ready");
        ready.EnsureSuccessStatusCode();
        var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
        var warning = Assert.Single(
            readyBody.GetProperty("warnings").EnumerateArray().Select(entry => entry.GetString() ?? string.Empty),
            entry => entry.StartsWith("Dirtside area-defence interception", StringComparison.Ordinal));

        Assert.Contains("always", warning, StringComparison.Ordinal);
        Assert.Contains("refused", warning, StringComparison.Ordinal);
        Assert.Contains("/api/dirtside/games/{id}/interceptions", warning, StringComparison.Ordinal);
        Assert.Contains("areaDefenceReach", warning, StringComparison.Ordinal);
        Assert.Contains("canIntercept", warning, StringComparison.Ordinal);
        Assert.Contains("will not invent them", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirtsideReadiness_SaysTheFallbackChitPotIsAGuess()
    {
        // The one number this engine invents. Every other number a Dirtside game runs on came off a
        // record card its owner filled in; the fallback pot's special counts did not, and a table
        // playing on them is playing on our guess about the most sensitive input in the damage
        // model. Readiness is where the operator finds out before the first shot rather than after
        // an argument about one, so the warning has to say "guess" in as many words.
        using var factory = CreateFactory("Development", new Dictionary<string, string?> { ["Features:Dirtside"] = "true" });
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/ready");
        ready.EnsureSuccessStatusCode();
        var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
        var warning = Assert.Single(
            readyBody.GetProperty("warnings").EnumerateArray().Select(entry => entry.GetString() ?? string.Empty),
            entry => entry.Contains("chit pot", StringComparison.Ordinal));

        Assert.Contains("are a guess, not a published distribution", warning, StringComparison.Ordinal);
        Assert.Contains("chitPot", warning, StringComparison.Ordinal);
        Assert.Contains("kept for one release", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateMatch_ReturnsJoinDetailsForHost()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/matches",
            new CreateMatchRequest("Smoke Admiral", "Integration Smoke", 72, 48, Rules: TestRules.Invented));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MatchCreatedResponse>();

        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.MatchId);
        Assert.NotEqual(Guid.Empty, body.ParticipantId);
        Assert.False(string.IsNullOrWhiteSpace(body.ParticipantToken));
        Assert.Matches("^[A-Z0-9-]+$", body.JoinCode);
    }

    [Fact]
    public async Task FighterOperations_UpdateSnapshotAndMatchLog()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/matches",
            new CreateMatchRequest("Carrier Boss", "Fighter Ops Smoke", 72, 48, Rules: TestRules.Invented));
        var session = await createResponse.Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(session);

        using var fleetResponse = await client.PostAsJsonAsync(
            $"/api/matches/{session.MatchId}/fleets",
            new CreateFleetRequest(session.ParticipantToken, "Carrier Group", "Test", "#47f1ff"));
        var fleetSnapshot = await fleetResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var fleet = Assert.Single(fleetSnapshot!.Fleets);

        using var carrierResponse = await client.PostAsJsonAsync(
            $"/api/fleets/{fleet.Id}/ships",
            // A carrier needs a bay before it can put anything in the air.
            new CreateShipRequest(session.ParticipantToken, "Home Plate", "Carrier", 4, 6, 1, 14, 4, IconKey: "carrier", FighterBays: 2));
        var carrierSnapshot = await carrierResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var carrier = carrierSnapshot!.Ships.Single(ship => ship.IconKey == "carrier");

        using var fighterResponse = await client.PostAsJsonAsync(
            $"/api/fleets/{fleet.Id}/ships",
            new CreateShipRequest(
                session.ParticipantToken,
                "Alpha Wing",
                "Fighter Group",
                6,
                12,
                1,
                6,
                0,
                IconKey: "fighter-group",
                FighterEnduranceMax: 6,
                FighterMaxRange: 24,
                HomeCarrierShipId: carrier.Id));
        var fighterSnapshot = await fighterResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var fighter = fighterSnapshot!.Ships.Single(ship => ship.IconKey == "fighter-group");

        using var opsResponse = await client.PostAsJsonAsync(
            $"/api/ships/{fighter.Id}/fighter-ops",
            new UpdateFighterOperationsRequest(session.ParticipantToken, "Airborne", 1, 6, 24, carrier.Id));
        opsResponse.EnsureSuccessStatusCode();
        var opsSnapshot = await opsResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var updated = opsSnapshot!.Ships.Single(ship => ship.Id == fighter.Id);

        Assert.Equal("Airborne", updated.FighterStatus);
        Assert.Equal(1, updated.FighterEnduranceUsed);
        Assert.Equal(6, updated.FighterEnduranceMax);
        Assert.Equal(24, updated.FighterMaxRange);
        Assert.Equal(carrier.Id, updated.HomeCarrierShipId);
        Assert.Contains(opsSnapshot.MatchLog, entry => entry.Category == "Fighters" && entry.Message.Contains("Alpha Wing", StringComparison.Ordinal));

        using var markerResponse = await client.PostAsJsonAsync(
            $"/api/matches/{session.MatchId}/ordnance",
            // The launching ship sits at the table origin here, so the point of aim has to be
            // inside the 24 a standard salvo can throw.
            new CreateOrdnanceMarkerRequest(session.ParticipantToken, "Alpha Salvo", "Missile", fighter.Id, carrier.Id, 12, 16, 1, 12, 2, 3, 24));
        markerResponse.EnsureSuccessStatusCode();
        var markerSnapshot = await markerResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var marker = Assert.Single(markerSnapshot!.OrdnanceMarkers);

        Assert.Equal("Alpha Salvo", marker.Name);
        Assert.Equal("Missile", marker.MarkerType);
        Assert.Equal(fighter.Id, marker.SourceShipId);
        Assert.Equal("Active", marker.Status);
        Assert.Contains(markerSnapshot.MatchLog, entry => entry.Category == "Ordnance" && entry.Message.Contains("Alpha Salvo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FighterAndOrdnanceOperations_EnforcePlayableOwnershipBoundaries()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/matches",
            new CreateMatchRequest("Blue", "Ownership Smoke", 72, 48, Rules: TestRules.Invented));
        var blueSession = await createResponse.Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(blueSession);

        using var redJoinResponse = await client.PostAsJsonAsync(
            "/api/matches/join",
            new JoinMatchRequest(blueSession.JoinCode, "Red"));
        var redSession = await redJoinResponse.Content.ReadFromJsonAsync<MatchJoinedResponse>();
        Assert.NotNull(redSession);

        using var blueFleetResponse = await client.PostAsJsonAsync(
            $"/api/matches/{blueSession.MatchId}/fleets",
            new CreateFleetRequest(blueSession.ParticipantToken, "Blue Fleet", null, "#47f1ff"));
        var blueFleetSnapshot = await blueFleetResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var blueFleet = Assert.Single(blueFleetSnapshot!.Fleets);

        using var redFleetResponse = await client.PostAsJsonAsync(
            $"/api/matches/{blueSession.MatchId}/fleets",
            new CreateFleetRequest(redSession.ParticipantToken, "Red Fleet", null, "#ff5f6d"));
        var redFleetSnapshot = await redFleetResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var redFleet = redFleetSnapshot!.Fleets.Single(fleet => fleet.Name == "Red Fleet");

        using var carrierResponse = await client.PostAsJsonAsync(
            $"/api/fleets/{blueFleet.Id}/ships",
            new CreateShipRequest(blueSession.ParticipantToken, "Blue Carrier", "Carrier", 4, 6, 1, 14, 4, StartX: 12, StartY: 12, IconKey: "carrier"));
        var carrierSnapshot = await carrierResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var carrier = carrierSnapshot!.Ships.Single(ship => ship.Name == "Blue Carrier");

        using var fighterResponse = await client.PostAsJsonAsync(
            $"/api/fleets/{blueFleet.Id}/ships",
            new CreateShipRequest(blueSession.ParticipantToken, "Blue Wing", "Fighter Group", 6, 12, 1, 6, 0, StartX: 18, StartY: 12, IconKey: "fighter-group", HomeCarrierShipId: carrier.Id));
        var fighterSnapshot = await fighterResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var fighter = fighterSnapshot!.Ships.Single(ship => ship.Name == "Blue Wing");

        using var redShipResponse = await client.PostAsJsonAsync(
            $"/api/fleets/{redFleet.Id}/ships",
            new CreateShipRequest(redSession.ParticipantToken, "Red Cruiser", "Cruiser", 4, 8, 7, 12, 3, StartX: 54, StartY: 36, IconKey: "cruiser"));
        var redShipSnapshot = await redShipResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var redShip = redShipSnapshot!.Ships.Single(ship => ship.Name == "Red Cruiser");

        using var nonFighterOps = await client.PostAsJsonAsync(
            $"/api/ships/{redShip.Id}/fighter-ops",
            new UpdateFighterOperationsRequest(redSession.ParticipantToken, "Airborne", 0, 6, 24, carrier.Id));
        Assert.Equal(HttpStatusCode.BadRequest, nonFighterOps.StatusCode);

        using var invalidHomeCarrier = await client.PostAsJsonAsync(
            $"/api/ships/{fighter.Id}/fighter-ops",
            new UpdateFighterOperationsRequest(blueSession.ParticipantToken, "Airborne", 0, 6, 24, redShip.Id));
        Assert.Equal(HttpStatusCode.BadRequest, invalidHomeCarrier.StatusCode);

        using var markerResponse = await client.PostAsJsonAsync(
            $"/api/matches/{blueSession.MatchId}/ordnance",
            new CreateOrdnanceMarkerRequest(blueSession.ParticipantToken, "Blue Salvo", "Missile", fighter.Id, redShip.Id, 18, 12, 1, 12, 2, 3, 24));
        markerResponse.EnsureSuccessStatusCode();
        var markerSnapshot = await markerResponse.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var marker = Assert.Single(markerSnapshot!.OrdnanceMarkers);

        using var redMarkerEdit = await client.PostAsJsonAsync(
            $"/api/ordnance/{marker.Id}",
            new UpdateOrdnanceMarkerRequest(redSession.ParticipantToken, "Captured Salvo", "Missile", redShip.Id, 20, 12, 1, 12, 1, 3, 24));
        Assert.Equal(HttpStatusCode.Forbidden, redMarkerEdit.StatusCode);

        using var blueMarkerEdit = await client.PostAsJsonAsync(
            $"/api/ordnance/{marker.Id}",
            new UpdateOrdnanceMarkerRequest(blueSession.ParticipantToken, "Blue Salvo", "Missile", redShip.Id, 20, 12, 1, 12, 1, 3, 24));
        blueMarkerEdit.EnsureSuccessStatusCode();
        var updatedSnapshot = await blueMarkerEdit.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var updatedMarker = Assert.Single(updatedSnapshot!.OrdnanceMarkers);
        Assert.Equal(20, updatedMarker.PositionX);
        Assert.Equal(1, updatedMarker.EnduranceRemaining);
    }

    [Fact]
    public async Task JoinUnknownMatch_ReturnsProblemDetails()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/matches/join",
            new JoinMatchRequest("NOPE0", "Late Admiral"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Request could not be completed", problem.GetProperty("title").GetString());
        Assert.Contains("not found", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        Dictionary<string, string?>? settings = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    if (settings is not null)
                    {
                        configuration.AddInMemoryCollection(settings);
                    }
                });
            });

    private static Task<HttpResponseMessage> SendPreflight(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/matches");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return client.SendAsync(request);
    }
}
