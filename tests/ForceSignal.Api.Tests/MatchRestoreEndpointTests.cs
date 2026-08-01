using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForceSignal.Api.Tests;

public sealed class MatchRestoreEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task RestoreAndClaim_RebuildsTheMatchAndIssuesAWorkingToken()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Restore Endpoint", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);
        var fleetSnapshot = await (await client.PostAsJsonAsync($"/api/matches/{created.MatchId}/fleets",
            new CreateFleetRequest(created.ParticipantToken, "Blue Watch", null, "#47f1ff")))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var fleet = fleetSnapshot!.Fleets.Single();
        var withShip = await (await client.PostAsJsonAsync($"/api/fleets/{fleet.Id}/ships",
            new CreateShipRequest(created.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24)))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        Assert.NotNull(withShip);

        // Restore accepts the exported wrapper shape, with no participant token.
        var backup = new { savedAt = DateTimeOffset.UtcNow, snapshot = withShip };
        using var restoreResponse = await client.PostAsync("/api/matches/restore",
            new StringContent(JsonSerializer.Serialize(backup, JsonOptions), Encoding.UTF8, "application/json"));
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<MatchRestoredResponse>(JsonOptions);

        Assert.NotNull(restored);
        Assert.NotEqual(created.MatchId, restored.MatchId);
        var seat = Assert.Single(restored.Seats);
        Assert.False(seat.IsClaimed);
        Assert.Equal(1, seat.ShipCount);

        using var claimResponse = await client.PostAsJsonAsync(
            $"/api/matches/{restored.MatchId}/seats/{seat.ParticipantId}/claim",
            new ClaimSeatRequest(seat.DisplayName));
        claimResponse.EnsureSuccessStatusCode();
        var session = await claimResponse.Content.ReadFromJsonAsync<MatchJoinedResponse>();
        Assert.NotNull(session);

        // The issued token really commands the restored fleet. Ship ids are reissued on restore,
        // so address the restored ship, not the source one.
        using var damage = await client.PostAsJsonAsync($"/api/ships/{restored.Snapshot.Ships.Single().Id}/damage",
            new UpdateShipDamageRequest(session.ParticipantToken, 2, 0, 0, 0, 0));
        damage.EnsureSuccessStatusCode();

        // A second claim on the same seat is refused.
        using var secondClaim = await client.PostAsJsonAsync(
            $"/api/matches/{restored.MatchId}/seats/{seat.ParticipantId}/claim",
            new ClaimSeatRequest(seat.DisplayName));
        Assert.Equal(HttpStatusCode.BadRequest, secondClaim.StatusCode);
    }

    [Fact]
    public async Task RestoreAcceptsABareSnapshotAndExposesSeatsByRoomCode()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Bare Snapshot", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        var fleetSnapshot = await (await client.PostAsJsonAsync($"/api/matches/{created!.MatchId}/fleets",
            new CreateFleetRequest(created.ParticipantToken, "Blue Watch", null, "#47f1ff")))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var withShip = await (await client.PostAsJsonAsync($"/api/fleets/{fleetSnapshot!.Fleets.Single().Id}/ships",
            new CreateShipRequest(created.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24)))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);

        // A bare snapshot, with no wrapper, restores too.
        using var restoreResponse = await client.PostAsync("/api/matches/restore",
            new StringContent(JsonSerializer.Serialize(withShip, JsonOptions), Encoding.UTF8, "application/json"));
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<MatchRestoredResponse>(JsonOptions);

        // A second device only knows the room code: it must reach the seat list from that.
        var identity = await client.GetFromJsonAsync<MatchIdentityDto>($"/api/matches/by-code/{restored!.JoinCode}");
        Assert.NotNull(identity);
        Assert.Equal(restored.MatchId, identity.MatchId);
        Assert.True(identity.HasUnclaimedSeats);

        var seats = await client.GetFromJsonAsync<List<MatchSeatDto>>($"/api/matches/{identity.MatchId}/seats");
        Assert.Single(seats!);

        using var unknownCode = await client.GetAsync("/api/matches/by-code/NOPE-NOPE-NOPE");
        Assert.Equal(HttpStatusCode.NotFound, unknownCode.StatusCode);
    }

    [Theory]
    // Every rejected payload must answer with problem+json, including a file that is not JSON at
    // all - model binding would otherwise return a plain-text 400.
    [InlineData("{ not json at all", "could not be read")]
    [InlineData("", "could not be read")]
    [InlineData("{\"snapshot\":{\"ships\":[]}}", "no participants")]
    [InlineData("{\"participants\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"displayName\":\"A\",\"role\":\"Owner\"}],\"ships\":[]}", "no ships")]
    public async Task Restore_WithUnusablePayload_ReturnsProblemDetails(string payload, string expected)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/matches/restore",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(expected, problem.GetProperty("detail").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
}
