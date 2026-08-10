using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Contracts.Ground;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Covers the StarGrunt routes, and the promise the feature flag makes about them.
/// </summary>
public sealed class StarGruntEndpointTests
{
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
        using var status = await client.GetAsync(new Uri("/api/stargrunt/status", UriKind.Relative));

        // Absent, not merely refused. This is the flag's whole promise: a half-built engine cannot
        // get in the way of a Full Thrust game someone turned up to play.
        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
    }

    [Fact]
    public async Task AGameCanBePlayedOverHttp()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/stargrunt/games", new CreateStarGruntGameRequest("Hill 43")))
            .Content.ReadFromJsonAsync<StarGruntGameCreatedResponse>(JsonOptions);
        Assert.NotNull(created);
        var game = created.GameId;

        await Post(client, $"/api/stargrunt/games/{game}/units", Squad("alpha", "Alpha Squad", "blue"));
        await Post(client, $"/api/stargrunt/games/{game}/units", Squad("bravo", "Bravo Squad", "red"));
        await Post(client, $"/api/stargrunt/games/{game}/turns/begin", new { });
        await Post(client, $"/api/stargrunt/games/{game}/turns/current/first-activator", new ChooseFirstActivatorRequest("blue", true));
        await Post(client, $"/api/stargrunt/games/{game}/activations", new BeginStarGruntActivationRequest("blue", "alpha"));

        var fired = await Post(client, $"/api/stargrunt/games/{game}/activations/current/fire", new StarGruntFireRequest(
            "alpha", "bravo", "Rifles", 10, [8], 9, "Soft"));

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
        var created = await (await client.PostAsJsonAsync("/api/stargrunt/games", new CreateStarGruntGameRequest("Hill 43")))
            .Content.ReadFromJsonAsync<StarGruntGameCreatedResponse>(JsonOptions);

        using var response = await client.PostAsJsonAsync(
            $"/api/stargrunt/games/{created!.GameId}/units",
            new AddStarGruntUnitRequest("alpha", "Alpha", "blue", "Squad", 7, 2, [], []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AGameNobodyStartedIsNotFound()
    {
        using var factory = CreateFactory(starGrunt: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/stargrunt/games/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> TableWithTwoSquads(HttpClient client)
    {
        var created = await (await client.PostAsJsonAsync("/api/stargrunt/games", new CreateStarGruntGameRequest("Hill 43")))
            .Content.ReadFromJsonAsync<StarGruntGameCreatedResponse>(JsonOptions);
        await Post(client, $"/api/stargrunt/games/{created!.GameId}/units", Squad("alpha", "Alpha Squad", "blue"));
        await Post(client, $"/api/stargrunt/games/{created.GameId}/units", Squad("bravo", "Bravo Squad", "red"));
        return created.GameId;
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
