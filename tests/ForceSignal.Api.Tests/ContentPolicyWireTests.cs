using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForceSignal.Api.Tests;

/// <summary>
/// The content policy where it is hardest to see: <b>on the wire</b>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ContentPolicyTests</c> in the application tests calls the service in C#, so every number it
/// sends is one it wrote down. That cannot see a default parameter value on a request record,
/// because a C# caller who omits an argument is doing so knowingly. A JSON caller is not: a body
/// that simply has no <c>fireControlMax</c> key in it is a caller who entered nothing, and
/// System.Text.Json fills the gap from the record's own default. So a <c>= 1</c> in
/// <c>MatchContracts.cs</c> is a rules number this app ships, delivered to somebody who never asked
/// for it, and only a test that posts real bytes can see it.
/// </para>
/// <para>
/// Proven before the fix by posting the body below: the ship came back with
/// <c>fireControlMax: 1</c>. A ship that carries one fire control system can direct fire at one
/// target; a ship that carries none cannot direct fire at all. Both are states this engine plays,
/// and which one a ship is in is off the player's own design sheet.
/// </para>
/// <para>
/// A floor is still not an invention, and the first test below holds both halves side by side:
/// <c>hullMax</c> comes back as one from the same body, because a ship of no boxes is not a ship
/// and the service says so in its clamp. Fire control clamps from zero up, so it has no such
/// argument to make.
/// </para>
/// </remarks>
public sealed class ContentPolicyWireTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AShipPostedWithNoNumbersInTheBodyComesBackCarryingNone()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var table = await OpenTable(client);

        // Exactly the keys a caller who entered nothing sends, and no others. There is no
        // `fireControlMax` in here at all - that is the whole point of the body.
        var ship = await PostShip(client, table, """
            {
              "name": "Valiant",
              "className": "Cruiser",
              "thrustRating": 0,
              "initialVelocity": 0,
              "initialCourse": 1,
              "hullMax": 0,
              "armorMax": 0
            }
            """);

        // Reached-the-subject: the body was accepted and it is the ship that was asked for, so
        // there is something to read a number off.
        Assert.Equal("Valiant", ship.GetProperty("name").GetString());

        // The floor, which is argued for: a ship of no boxes is not a ship.
        Assert.Equal(1, ship.GetProperty("hullMax").GetInt32());

        // The rules number, which is not argued for and is now absent.
        Assert.Equal(0, ship.GetProperty("fireControlMax").GetInt32());
    }

    [Fact]
    public async Task AShipKeepsTheFireControlItDidEnter()
    {
        // The control that must be accepted. Zeroing the field outright - or clamping it to zero on
        // the way in - would satisfy the test above and break the game: this is a real number a
        // player enters off their design sheet, and it has to survive being entered. Without this,
        // "ships no rules numbers" reads as "ships no numbers".
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var table = await OpenTable(client);

        var ship = await PostShip(client, table, """
            {
              "name": "Valiant",
              "className": "Cruiser",
              "thrustRating": 4,
              "initialVelocity": 6,
              "initialCourse": 1,
              "hullMax": 12,
              "armorMax": 2,
              "fireControlMax": 3
            }
            """);

        Assert.Equal("Valiant", ship.GetProperty("name").GetString());
        Assert.Equal(12, ship.GetProperty("hullMax").GetInt32());
        Assert.Equal(3, ship.GetProperty("fireControlMax").GetInt32());
    }

    [Fact]
    public async Task EditingAShipWithoutNamingItsFireControlDoesNotGiveItOne()
    {
        // The same default sits on UpdateShipProfileRequest, and this is the worse of the two
        // doors: the ship already exists and already carries the player's own number, so the
        // invented one arrives as a silent *edit* of something they entered rather than as a value
        // on a new record.
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var table = await OpenTable(client);

        var created = await PostShip(client, table, """
            {
              "name": "Valiant",
              "className": "Cruiser",
              "thrustRating": 4,
              "initialVelocity": 6,
              "initialCourse": 1,
              "hullMax": 12,
              "armorMax": 2,
              "fireControlMax": 0
            }
            """);
        var shipId = created.GetProperty("id").GetString();

        // Reached-the-subject: the ship really did start out carrying none, so anything it has
        // afterwards was written by the edit.
        Assert.Equal(0, created.GetProperty("fireControlMax").GetInt32());

        var response = await client.PostAsync(
            $"/api/ships/{shipId}/profile",
            Body($$"""
                {
                  "participantToken": "{{table.ParticipantToken}}",
                  "name": "Valiant",
                  "className": "Cruiser",
                  "thrustRating": 4,
                  "currentVelocity": 6,
                  "currentCourse": 1,
                  "hullMax": 12,
                  "armorMax": 2
                }
                """));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        var edited = JsonDocument.Parse(body).RootElement
            .GetProperty("ships").EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == shipId);

        Assert.Equal(0, edited.GetProperty("fireControlMax").GetInt32());
    }

    private sealed record Table(Guid MatchId, string ParticipantToken, string FleetId);

    private static async Task<Table> OpenTable(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/matches",
            new CreateMatchRequest("Blue", "Content Policy Wire", 72, 48, TestRules.Invented),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<MatchCreatedResponse>(JsonOptions))!;

        var fleetResponse = await client.PostAsJsonAsync(
            $"/api/matches/{created.MatchId}/fleets",
            new CreateFleetRequest(created.ParticipantToken, "Home Watch", null),
            JsonOptions);
        fleetResponse.EnsureSuccessStatusCode();
        var snapshot = await fleetResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        return new Table(
            created.MatchId,
            created.ParticipantToken,
            snapshot.GetProperty("fleets")[0].GetProperty("id").GetString()!);
    }

    /// <summary>Posts a ship from a body holding only the keys the test wrote, plus the token.</summary>
    private static async Task<JsonElement> PostShip(HttpClient client, Table table, string shipJson)
    {
        var withToken = $$"""{"participantToken":"{{table.ParticipantToken}}",{{shipJson.Trim()[1..]}}""";
        var response = await client.PostAsync($"/api/fleets/{table.FleetId}/ships", Body(withToken));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        return JsonDocument.Parse(body).RootElement.GetProperty("ships").EnumerateArray().Last();
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
}
