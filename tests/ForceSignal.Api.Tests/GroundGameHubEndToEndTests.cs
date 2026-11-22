using System.Net.Http.Json;
using System.Threading.Channels;
using ForceSignal.Contracts.Ground;
using ForceSignal.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace ForceSignal.Api.Tests;

/// <summary>
/// A ground game followed from a second device. The game's token is the whole credential - the
/// share code carries it - so following a game takes the token, a wrong one is refused over the
/// wire, and a change made through any route is pushed to every device following that game and no
/// other.
/// </summary>
public sealed class GroundGameHubEndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<HubConnection> _connections = [];

    public GroundGameHubEndToEndTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Features:StarGrunt", "true");
            builder.UseSetting("Features:Dirtside", "true");
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task AChangeToADirtsideGameIsPushedToTheOtherDeviceFollowingIt()
    {
        using var client = _factory.CreateClient();
        var game = await CreateDirtside(client, "Ridge 9");
        var other = await CreateDirtside(client, "Elsewhere");

        var link = await Connect();
        var pushes = Pushes(link);
        await link.InvokeAsync("JoinGroundGame", "dirtside", game.GameId.ToString(), game.Token);

        // A change to a game nobody here follows is not heard.
        await Post(client, $"/api/dirtside/games/{other.GameId}/units", other.Token,
            Platoon("alpha"));
        var added = await Post(client, $"/api/dirtside/games/{game.GameId}/units", game.Token,
            Platoon("alpha"));

        var push = await NextPush(pushes);
        Assert.Equal(("dirtside", game.GameId), (push.Engine, push.GameId));
        Assert.True(push.Version >= 2, "the version the change produced");
        Assert.True(added.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AChangeToAStarGruntGameIsPushedToo()
    {
        using var client = _factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/stargrunt/games", StarGruntTestProfile.CreateGame("Hill 43"));
        var game = (await created.Content.ReadFromJsonAsync<StarGruntGameCreatedResponse>())!;

        var link = await Connect();
        var pushes = Pushes(link);
        await link.InvokeAsync("JoinGroundGame", "stargrunt", game.GameId.ToString(), game.Token);

        await Post(client, $"/api/stargrunt/games/{game.GameId}/units", game.Token, new AddStarGruntUnitRequest("alpha", "Alpha", "blue", "Squad", 8, 2, [new StarGruntFigureDto(6)], [new StarGruntWeaponDto("Rifles", 10)]));

        var push = await NextPush(pushes);
        Assert.Equal(("stargrunt", game.GameId), (push.Engine, push.GameId));
    }

    [Fact]
    public async Task FollowingAGameTakesItsToken()
    {
        using var client = _factory.CreateClient();
        var game = await CreateDirtside(client, "Ridge 9");
        var link = await Connect();

        var wrong = await Assert.ThrowsAsync<HubException>(() =>
            link.InvokeAsync("JoinGroundGame", "dirtside", game.GameId.ToString(), new string('0', 64)));
        Assert.Contains("game code", wrong.Message, StringComparison.Ordinal);

        // Nor does the right token for one engine open a game id in the other.
        await Assert.ThrowsAsync<HubException>(() =>
            link.InvokeAsync("JoinGroundGame", "stargrunt", game.GameId.ToString(), game.Token));
        await Assert.ThrowsAsync<HubException>(() =>
            link.InvokeAsync("JoinGroundGame", "fullthrust", game.GameId.ToString(), game.Token));
    }

    [Fact]
    public async Task ARefusedCommandPushesNothing()
    {
        using var client = _factory.CreateClient();
        var game = await CreateDirtside(client, "Ridge 9");
        var link = await Connect();
        var pushes = Pushes(link);
        await link.InvokeAsync("JoinGroundGame", "dirtside", game.GameId.ToString(), game.Token);

        // Nothing is on the table, so activating anything is refused - and nothing changed.
        var refused = await Post(client, $"/api/dirtside/games/{game.GameId}/activations", game.Token,
            new BeginDirtsideActivationRequest("blue", "alpha"));
        Assert.False(refused.IsSuccessStatusCode);
        await Post(client, $"/api/dirtside/games/{game.GameId}/units", game.Token,
            Platoon("alpha"));

        // The first push heard is the platoon's, not the refusal's.
        var push = await NextPush(pushes);
        Assert.Equal(2, push.Version);
    }

    private async Task<HubConnection> Connect()
    {
        var server = _factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/ground"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    private static Channel<(string Engine, Guid GameId, long Version)> Pushes(HubConnection connection)
    {
        var pushes = Channel.CreateUnbounded<(string, Guid, long)>();
        connection.On<string, Guid, long>("GroundGameChanged", (engine, gameId, version) =>
            pushes.Writer.TryWrite((engine, gameId, version)));
        return pushes;
    }

    private static async Task<(string Engine, Guid GameId, long Version)> NextPush(
        Channel<(string Engine, Guid GameId, long Version)> pushes)
    {
        using var timeout = new CancellationTokenSource(Patience);
        return await pushes.Reader.ReadAsync(timeout.Token);
    }

    private static AddDirtsidePlatoonRequest Platoon(string id) => new(
        id,
        "Alpha",
        "blue",
        "Armour",
        IsCybertank: false,
        Elements:
        [
            new DirtsideElementDto($"{id}-1", "Alpha One", "Basic", 3, 3, 12,
                [new DirtsideWeaponDto("Main Gun", ChitCount: 3, Close: new DirtsideValidityDto("All", "FaceValue"), Medium: new DirtsideValidityDto("All", "FaceValue"), Long: new DirtsideValidityDto("All", "FaceValue"))]),
        ]);

    private static async Task<DirtsideGameCreatedResponse> CreateDirtside(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/dirtside/games", DirtsideTestProfile.CreateGame(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DirtsideGameCreatedResponse>())!;
    }

    private static async Task<HttpResponseMessage> Post<T>(HttpClient client, string path, string token, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Game-Token", token);
        return await client.SendAsync(request);
    }
}
