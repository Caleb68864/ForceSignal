using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Presence through the real hub: the real host, the real SignalR pipeline and a real SignalR client
/// over the test server, rather than the hub class handed fakes.
/// </summary>
/// <remarks>
/// <see cref="MatchHubPresenceTests"/> calls the hub's methods directly, so it cannot see what the
/// pipeline does around them: that a connection's <c>Context.Items</c> really lasts from its join to
/// its disconnect, that <c>OnDisconnectedAsync</c> really runs when a client goes, and that a group
/// push really reaches the other device. The reconnect is the case that went wrong: SignalR opens the
/// new connection before the server drops the old one, so the old one's disconnect arrives last, and
/// it used to mark a player who was online as gone.
/// </remarks>
public sealed class MatchHubEndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    // Every presence change the hub hands the match service, once the service has applied it. A
    // disconnect that changes nothing sends no push, so this is the only way to know the server has
    // finished with a stopped connection - rather than sleeping and hoping it has.
    private readonly Channel<bool> _presenceApplied = Channel.CreateUnbounded<bool>();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<HubConnection> _connections = [];

    public MatchHubEndToEndTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                var real = services.Single(d => d.ServiceType == typeof(IMatchService));
                services.Remove(real);
                services.AddSingleton(sp => PresenceObserver.Wrap(
                    (IMatchService)real.ImplementationFactory!(sp), _presenceApplied.Writer));
            });
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
    public async Task TheOtherDeviceHearsAPlayerArriveAndLeave()
    {
        using var client = _factory.CreateClient();
        var blue = await Create(client);
        var red = await Join(client, blue.JoinCode);

        var blueLink = await Connect();
        var bluePushes = Pushes(blueLink);
        await blueLink.InvokeAsync("JoinMatchGroup", blue.MatchId.ToString(), blue.ParticipantToken);
        await NextPush(bluePushes);

        var redLink = await Connect();
        await redLink.InvokeAsync("JoinMatchGroup", red.MatchId.ToString(), red.ParticipantToken);

        // Blue's device is told, by the server, over its own connection.
        await NextPush(bluePushes);
        Assert.True(await IsConnected(client, blue, red.ParticipantId));

        await redLink.StopAsync();

        await NextPush(bluePushes);
        Assert.False(await IsConnected(client, blue, red.ParticipantId));
        Assert.True(await IsConnected(client, blue, blue.ParticipantId));
    }

    [Fact]
    public async Task AReconnectWhoseOldConnectionDropsLast_LeavesThePlayerConnected()
    {
        using var client = _factory.CreateClient();
        var blue = await Create(client);
        var red = await Join(client, blue.JoinCode);

        var redBefore = await Connect();
        await redBefore.InvokeAsync("JoinMatchGroup", red.MatchId.ToString(), red.ParticipantToken);

        // The reconnect: a new connection for the same seat, joined while the old one is still open.
        var redAfter = await Connect();
        await redAfter.InvokeAsync("JoinMatchGroup", red.MatchId.ToString(), red.ParticipantToken);
        Assert.NotEqual(redBefore.ConnectionId, redAfter.ConnectionId);
        Assert.True(await NextPresence(), "the first join");
        Assert.True(await NextPresence(), "the reconnect's join");

        // And then the old one goes, last - the server timing it out, in the real case.
        await redBefore.StopAsync();
        Assert.False(await NextPresence(), "the old connection's disconnect");

        Assert.True(await IsConnected(client, blue, red.ParticipantId));

        // The new connection is the live one, so when it goes, so does the player.
        await redAfter.StopAsync();
        Assert.False(await NextPresence(), "the new connection's disconnect");
        Assert.False(await IsConnected(client, blue, red.ParticipantId));
    }

    [Fact]
    public async Task AJoinWithTheWrongToken_IsRefusedOverTheWire()
    {
        using var client = _factory.CreateClient();
        var blue = await Create(client);

        var link = await Connect();

        var refusal = await Assert.ThrowsAsync<HubException>(() =>
            link.InvokeAsync("JoinMatchGroup", blue.MatchId.ToString(), "not-the-token"));
        Assert.Contains("valid participant token", refusal.Message, StringComparison.Ordinal);
        Assert.False(await IsConnected(client, blue, blue.ParticipantId));
    }

    private async Task<HubConnection> Connect()
    {
        var server = _factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/match"), options =>
            {
                // Long polling is plain HTTP, which the test server carries through its own handler.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    private async Task<bool> NextPresence()
    {
        using var timeout = new CancellationTokenSource(Patience);
        return await _presenceApplied.Reader.ReadAsync(timeout.Token);
    }

    /// <summary>Every presence push this connection hears.</summary>
    private static Channel<Guid> Pushes(HubConnection connection)
    {
        var pushes = Channel.CreateUnbounded<Guid>();
        connection.On<Guid, long, string>("MatchSnapshotChanged", (matchId, _, reason) =>
        {
            if (reason == "ParticipantConnectionChanged")
            {
                pushes.Writer.TryWrite(matchId);
            }
        });
        return pushes;
    }

    private static async Task NextPush(Channel<Guid> pushes)
    {
        using var timeout = new CancellationTokenSource(Patience);
        await pushes.Reader.ReadAsync(timeout.Token);
    }

    private static async Task<MatchCreatedResponse> Create(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/matches", new CreateMatchRequest("Blue", "Two Devices", 72, 48, Rules: TestRules.Invented));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchCreatedResponse>())!;
    }

    private static async Task<MatchJoinedResponse> Join(HttpClient client, string joinCode)
    {
        using var response = await client.PostAsJsonAsync("/api/matches/join", new JoinMatchRequest(joinCode, "Red"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchJoinedResponse>())!;
    }

    private static async Task<bool> IsConnected(HttpClient client, MatchCreatedResponse reader, Guid participantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{reader.MatchId}/snapshot");
        request.Headers.Add("X-Participant-Token", reader.ParticipantToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        // Read as JSON rather than as the DTO: only the seats matter here, and the rest of the
        // snapshot needs the API's own converters to read back.
        var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>();
        return snapshot.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == participantId)
            .GetProperty("isConnected").GetBoolean();
    }

    /// <summary>
    /// The real match service, passed through untouched, reporting each presence change once it has
    /// been applied.
    /// </summary>
    public class PresenceObserver : DispatchProxy
    {
        private IMatchService _inner = null!;
        private ChannelWriter<bool> _applied = null!;

        public static IMatchService Wrap(IMatchService inner, ChannelWriter<bool> applied)
        {
            var proxy = Create<IMatchService, PresenceObserver>();
            var observer = (PresenceObserver)(object)proxy;
            observer._inner = inner;
            observer._applied = applied;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                return targetMethod!.Invoke(_inner, args);
            }
            catch (TargetInvocationException thrown) when (thrown.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(thrown.InnerException);
                throw;
            }
            finally
            {
                if (targetMethod!.Name == nameof(IMatchService.SetParticipantConnection))
                {
                    _applied.TryWrite((bool)args![2]!);
                }
            }
        }
    }
}
