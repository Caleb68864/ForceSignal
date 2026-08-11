using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForceSignal.Api.Tests;

/// <summary>
/// The rules profile crossing the wire. ForceSignal ships no rules numbers, so a match is played
/// against a profile its players post, and it is refused until that profile is complete.
/// </summary>
public sealed class RulesProfileEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AMatchOpensWithNothingToPlayAgainstUntilAProfileIsPosted()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var created = await CreateMatch(client);

        client.DefaultRequestHeaders.Add("X-Participant-Token", created.ParticipantToken);
        var snapshot = await client.GetFromJsonAsync<JsonElement>(
            $"/api/matches/{created.MatchId}/snapshot", JsonOptions);

        // Blank rather than a helpful default, all the way out to the wire.
        Assert.Equal(string.Empty, snapshot.GetProperty("rules").GetProperty("name").GetString());
        Assert.Equal(0, snapshot.GetProperty("rules").GetProperty("dieFaces").GetInt32());
    }

    [Fact]
    public async Task AnIncompleteProfileComesBackRefusedWithWhatIsMissing()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var created = await CreateMatch(client);

        var response = await Post(client, created, """{"name":"Half Done","dieFaces":6}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not complete enough", body, StringComparison.Ordinal);
        Assert.Contains("beam die scores", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProfileThatLeavesOutTheTablesItDoesNotUseStillRoundTrips()
    {
        // A body with no turnaround or point-defence table deserialises those as a *default*
        // ImmutableArray, which is not an empty one - it throws the moment anything enumerates or
        // serialises it. So a perfectly reasonable profile was accepted and then blew up on the way
        // back out, which is a miserable way to find out. Every profile is normalised on the way in.
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var created = await CreateMatch(client);

        var response = await Post(client, created, """
            {
              "name": "Kitchen Table Set",
              "dieFaces": 8,
              "beamDamage": [{ "dieFace": 8, "screenLevel": 0, "damage": 2 }],
              "beamRangeBandWidth": 10,
              "maxScreenLevel": 2,
              "thresholdRows": "FixedRows",
              "thresholdRowCount": 3,
              "fighterMoveAllowance": 15
            }
            """);

        response.EnsureSuccessStatusCode();
        var snapshot = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        var rules = snapshot.GetProperty("rules");

        Assert.Equal("Kitchen Table Set", rules.GetProperty("name").GetString());
        Assert.Equal(0, rules.GetProperty("turnaround").GetArrayLength());
        Assert.Equal(0, rules.GetProperty("pointDefenseKills").GetArrayLength());
    }

    [Fact]
    public async Task TheLogReportsTheNumbersBackRatherThanDescribingAnyRules()
    {
        // The match log is a record of what this match is played against. It reports the profile's
        // own numbers so it can never drift into being a paraphrase of somebody's rulebook.
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var created = await CreateMatch(client);

        var response = await Post(client, created, """
            {
              "name": "Kitchen Table Set",
              "dieFaces": 8,
              "beamDamage": [{ "dieFace": 8, "screenLevel": 0, "damage": 2 }],
              "beamRangeBandWidth": 10,
              "maxScreenLevel": 2,
              "thresholdRows": "FixedRows",
              "thresholdRowCount": 3,
              "fighterMoveAllowance": 15
            }
            """);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Playing against", body, StringComparison.Ordinal);
        Assert.Contains("Kitchen Table Set", body, StringComparison.Ordinal);
        Assert.Contains("d8", body, StringComparison.Ordinal);
        Assert.Contains("hulls in 3 rows", body, StringComparison.Ordinal);
    }

    private static async Task<MatchCreatedResponse> CreateMatch(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/matches", new CreateMatchRequest("Blue", "Profile Endpoint", 72, 48), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchCreatedResponse>(JsonOptions))!;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, MatchCreatedResponse created, string rulesJson) =>
        client.PostAsync(
            $"/api/matches/{created.MatchId}/rules-profile",
            new StringContent(
                $$"""{"participantToken":"{{created.ParticipantToken}}","rules":{{rulesJson}}}""",
                Encoding.UTF8,
                "application/json"));

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
}
