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
