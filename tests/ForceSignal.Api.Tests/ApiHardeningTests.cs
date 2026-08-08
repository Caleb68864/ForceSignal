using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Covers the transport boundary's guards: the headers every response carries, what an oversized
/// or deeply-nested snapshot file gets back, how a missing or blank participant token is answered,
/// and the budget on the two endpoints that turn a room code into a match.
/// </summary>
public sealed class ApiHardeningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task EveryResponse_CarriesTheBaselineBrowserSecurityHeaders()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        // The API answers with JSON and never a page, so nothing it returns should be framable.
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task ProblemResponses_AlsoCarryTheSecurityHeaders()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/matches/by-code/NOPE-NOPE-NOPE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task BlankParticipantTokenHeader_IsRefusedAsIfItWereMissing()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Blank Token", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{created.MatchId}/participants/me/ready")
        {
            Content = JsonContent.Create(new ReadyRequest(true)),
        };
        request.Headers.TryAddWithoutValidation("X-Participant-Token", "   ");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingParticipantTokenHeader_IsRefused()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "No Token", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        using var response = await client.PostAsJsonAsync(
            $"/api/matches/{created.MatchId}/participants/me/ready",
            new ReadyRequest(true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeeplyNestedSnapshotFile_IsRefusedAsProblemJsonRatherThanRecursedInto()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Well-formed JSON, but nested far past anything a saved match is.
        var payload = new StringBuilder();
        const int depth = 400;
        payload.Append("{\"snapshot\":");
        for (var i = 0; i < depth; i++)
        {
            payload.Append("{\"a\":");
        }

        payload.Append('1');
        payload.Append('}', depth);
        payload.Append('}');

        using var response = await client.PostAsync("/api/matches/restore",
            new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OversizedSnapshotFile_IsRefusedBeforeItIsParsed()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Twelve megabytes of valid JSON string, past the eight-megabyte ceiling.
        var payload = "{\"name\":\"" + new string('x', 12 * 1024 * 1024) + "\"}";

        using var response = await client.PostAsync("/api/matches/restore",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("larger than", problem.GetProperty("detail").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RoomCodeLookup_IsBudgetedSoCodesCannotBeWalkedQuickly()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var response = await client.GetAsync($"/api/matches/by-code/GUESS-{attempt:D3}-CODE");
            statuses.Add(response.StatusCode);
        }

        // The first tries answer normally; the budget cuts in well before forty.
        Assert.Contains(HttpStatusCode.NotFound, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task AGenuinePlayerMistypingTheirCode_IsNotLockedOut()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Typo Room", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        // Three fat-fingered attempts, then the real code, is well inside the budget.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var miss = await client.GetAsync($"/api/matches/by-code/WRONG-CODE-{attempt}");
            Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);
        }

        using var hit = await client.GetAsync($"/api/matches/by-code/{created.JoinCode}");
        hit.EnsureSuccessStatusCode();
        var identity = await hit.Content.ReadFromJsonAsync<MatchIdentityDto>(JsonOptions);
        Assert.Equal(created.MatchId, identity!.MatchId);
    }

    [Fact]
    public async Task TheMatchSnapshotIsOnlyReadableBySomeoneInTheMatch()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Private", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        // The snapshot is the whole game: positions, damage, every shot and its dice, the log, and
        // who is playing. The match id is not a secret - the room-code lookup hands it out and
        // every notification echoes it - so the id alone must not open it.
        using var anonymous = await client.GetAsync($"/api/matches/{created.MatchId}/snapshot");
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);

        // A token from a different match is no better.
        var other = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Red", "Elsewhere", 72, 48)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        using var wrongMatch = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{created.MatchId}/snapshot");
        wrongMatch.Headers.TryAddWithoutValidation("X-Participant-Token", other!.ParticipantToken);
        using var refused = await client.SendAsync(wrongMatch);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The player who is actually in the match reads it normally.
        using var mine = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{created.MatchId}/snapshot");
        mine.Headers.TryAddWithoutValidation("X-Participant-Token", created.ParticipantToken);
        using var allowed = await client.SendAsync(mine);
        allowed.EnsureSuccessStatusCode();
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
}
