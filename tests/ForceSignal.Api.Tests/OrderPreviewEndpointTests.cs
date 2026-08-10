using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Covers the plotting preview over HTTP. The map reads these fields by name, so the wire shape is
/// part of the contract rather than an implementation detail of the serializer.
/// </summary>
public sealed class OrderPreviewEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task PreviewOrder_AnswersWithThePathAndLeavesTheMatchAlone()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var (matchId, token, shipId, _) = await SetUpMatch(client);

        var before = await GetSnapshot(client, matchId, token);

        using var response = await client.PostAsJsonAsync(
            $"/api/matches/{matchId}/turns/current/orders/preview",
            new PreviewOrderRequest(token, shipId, new MovementOrder(2, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Starboard, 2)])));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Named exactly as the client reads them.
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.False(body.GetProperty("runsOffTable").GetBoolean());
        Assert.Equal(8, body.GetProperty("endingVelocity").GetInt32());
        Assert.Equal(4, body.GetProperty("usableThrust").GetInt32());
        Assert.Equal(4, body.GetProperty("thrustSpent").GetInt32());

        var path = body.GetProperty("path").EnumerateArray().ToArray();
        Assert.True(path.Length >= 2);
        Assert.Equal(20m, path[0].GetProperty("x").GetDecimal());
        Assert.Equal(24m, path[0].GetProperty("y").GetDecimal());

        var segments = body.GetProperty("segments").EnumerateArray().ToArray();
        Assert.NotEmpty(segments);
        Assert.True(segments[0].TryGetProperty("course", out _));
        Assert.True(segments[0].TryGetProperty("distance", out _));
        Assert.Equal(segments.Length + 1, path.Length);

        // A preview is a question, not a move: nothing about the match changed.
        var after = await GetSnapshot(client, matchId, token);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.MatchLog.Count, after.MatchLog.Count);
    }

    [Fact]
    public async Task PreviewOrder_DescribesAnIllegalDraftWithoutFailingTheRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var (matchId, token, shipId, _) = await SetUpMatch(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/matches/{matchId}/turns/current/orders/preview",
            new PreviewOrderRequest(token, shipId, new MovementOrder(4, 0, TurnDirection.None, [new TurnManeuver(TurnDirection.Port, 3)])));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isValid").GetBoolean());
        Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("path").EnumerateArray());
    }

    [Fact]
    public async Task PreviewOrder_RefusesSomeoneElsesShip()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var (matchId, _, shipId, joinCode) = await SetUpMatch(client);

        var joined = await (await client.PostAsJsonAsync("/api/matches/join", new JoinMatchRequest(joinCode, "Red")))
            .Content.ReadFromJsonAsync<MatchJoinedResponse>(JsonOptions);

        using var response = await client.PostAsJsonAsync(
            $"/api/matches/{matchId}/turns/current/orders/preview",
            new PreviewOrderRequest(joined!.ParticipantToken, shipId, new MovementOrder(1, 0, TurnDirection.None)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<MatchSnapshotDto> GetSnapshot(HttpClient client, Guid matchId, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{matchId}/snapshot");
        request.Headers.Add("X-Participant-Token", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions))!;
    }

    private static async Task<(Guid MatchId, string Token, Guid ShipId, string JoinCode)> SetUpMatch(HttpClient client)
    {
        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Preview Endpoint", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>(JsonOptions);
        var fleet = (await (await client.PostAsJsonAsync($"/api/matches/{created!.MatchId}/fleets",
            new CreateFleetRequest(created.ParticipantToken, "Blue Watch", null, "#47f1ff")))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions))!.Fleets.Single();
        var ship = (await (await client.PostAsJsonAsync($"/api/fleets/{fleet.Id}/ships",
            new CreateShipRequest(created.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24)))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions))!.Ships.Single();

        return (created.MatchId, created.ParticipantToken, ship.Id, created.JoinCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
}
