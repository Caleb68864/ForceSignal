using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ForceSignal.Api.Tests;

/// <summary>
/// A refused Full Thrust shot that still changed the match is pushed to the other device, through
/// the real host, the real fire endpoint and a real SignalR client.
/// </summary>
/// <remarks>
/// When nobody holds the firing initiative, the first shot of the phase rolls the die-off before the
/// shot itself is checked, and the roll stands even when the shot is then refused - so it is saved
/// as a change of its own. The endpoint used to push only after a shot that succeeded, so the other
/// device learnt who fires first only at the next push or refetch.
/// </remarks>
public sealed class FireRefusalPushTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly Queue<int> _faces = new();
    private readonly InMemoryMatchService _service;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<HubConnection> _connections = [];

    public FireRefusalPushTests()
    {
        // One service, held here as well as by the host, so the table can be laid out directly and
        // only the shot and the push go over the wire.
        _service = new InMemoryMatchService(_ => _faces.Count > 0 ? _faces.Dequeue() : 4);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(IMatchService)));
                services.AddSingleton<IMatchService>(_service);
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
    public async Task ARefusedShotThatRolledTheDieOff_IsPushedToTheOtherDevice()
    {
        using var client = _factory.CreateClient();
        var table = OpenFiringWithNobodyHoldingTheInitiative();

        var redLink = await Connect();
        var redPushes = Pushes(redLink);
        await redLink.InvokeAsync("JoinMatchGroup", table.MatchId.ToString(), table.RedToken);
        var before = _service.GetSnapshot(table.MatchId).Version;

        // Red wins the die-off, so Blue's shot is refused - after the roll.
        _faces.Enqueue(2);
        _faces.Enqueue(5);
        using var response = await client.PostAsJsonAsync(
            $"/api/matches/{table.MatchId}/turns/current/fire",
            new FireWeaponRequest(table.BlueToken, table.BlueShip, table.RedShip, table.BlueMount, 10));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("It is Red's turn to fire", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var after = _service.GetSnapshot(table.MatchId).Version;
        Assert.True(after > before, $"version {after} after the die-off, {before} before");

        // Red's device is told, over its own connection, of the version the roll made.
        using var timeout = new CancellationTokenSource(Patience);
        (long Version, string Reason) push;
        try
        {
            push = await redPushes.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("The refused shot changed the match, and the other device was never told.");
            throw;
        }

        Assert.Equal(after, push.Version);
        Assert.Equal("FiringInitiativeRolled", push.Reason);
    }

    [Fact]
    public async Task ARefusedShotThatChangedNothing_PushesNothing()
    {
        using var client = _factory.CreateClient();
        var table = OpenFiringWithNobodyHoldingTheInitiative();

        var redLink = await Connect();
        var redPushes = Pushes(redLink);
        await redLink.InvokeAsync("JoinMatchGroup", table.MatchId.ToString(), table.RedToken);
        var before = _service.GetSnapshot(table.MatchId).Version;

        // A shot from a ship that is not on the table is refused before anything is rolled.
        using var response = await client.PostAsJsonAsync(
            $"/api/matches/{table.MatchId}/turns/current/fire",
            new FireWeaponRequest(table.BlueToken, Guid.NewGuid(), table.RedShip, table.BlueMount, 10));
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(before, _service.GetSnapshot(table.MatchId).Version);

        // Then a change that is pushed, so the absence of a push before it is seen rather than
        // waited out: the first push red hears has to be this one.
        using var ready = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{table.MatchId}/participants/me/ready")
        {
            Content = JsonContent.Create(new ReadyRequest(true)),
        };
        ready.Headers.Add("X-Participant-Token", table.BlueToken);
        using var fence = await client.SendAsync(ready);
        fence.EnsureSuccessStatusCode();

        using var timeout = new CancellationTokenSource(Patience);
        var first = await redPushes.Reader.ReadAsync(timeout.Token);
        Assert.Equal("ParticipantReadyChanged", first.Reason);
    }

    /// <summary>
    /// Blue and Red, one ship each, bow to bow in the firing phase, restored so that nobody holds
    /// the initiative until the first shot asks.
    /// </summary>
    private Table OpenFiringWithNobodyHoldingTheInitiative()
    {
        var owner = _service.CreateMatch(new CreateMatchRequest("Blue", "Push Table", Rules: TestRules.Invented));
        var opponent = _service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var blueFleet = _service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null))
            .Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var redFleet = _service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null))
            .Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);
        Guid blueMount = Guid.NewGuid(), redMount = Guid.NewGuid();
        _service.CreateShip(blueFleet.Id, new CreateShipRequest(
            owner.ParticipantToken, "Blue Lead", "Cruiser", 4,
            InitialVelocity: 0, InitialCourse: 12, HullMax: 40, ArmorMax: 0, FireControlMax: 1, StartX: 20, StartY: 40,
            Weapons: [new WeaponMountDto(blueMount, "Lead Battery", 2, 36, [FiringArc.Fore])]));
        _service.CreateShip(redFleet.Id, new CreateShipRequest(
            opponent.ParticipantToken, "Red Lead", "Cruiser", 4,
            InitialVelocity: 0, InitialCourse: 6, HullMax: 40, ArmorMax: 0, FireControlMax: 1, StartX: 20, StartY: 20,
            Weapons: [new WeaponMountDto(redMount, "Red Battery", 2, 36, [FiringArc.Fore])]));
        _service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        _service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        _service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
        _service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
        _service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);

        // A snapshot from before turn order was exported, restored with no seat claimed.
        var restored = _service.RestoreMatch(
            _service.GetSnapshot(owner.MatchId) with { FiringParticipantId = null, ActivatedShipIds = [] }, null);
        var blue = _service.ClaimSeat(restored.MatchId, restored.Seats.Single(s => s.DisplayName == "Blue").ParticipantId, new ClaimSeatRequest("Blue", restored.JoinCode));
        var red = _service.ClaimSeat(restored.MatchId, restored.Seats.Single(s => s.DisplayName == "Red").ParticipantId, new ClaimSeatRequest("Red", restored.JoinCode));
        var ships = _service.GetSnapshot(restored.MatchId).Ships;
        return new Table(
            restored.MatchId,
            blue.ParticipantToken,
            red.ParticipantToken,
            ships.Single(s => s.Name == "Blue Lead").Id,
            ships.Single(s => s.Name == "Red Lead").Id,
            ships.Single(s => s.Name == "Blue Lead").Weapons.Single().Id);
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

    /// <summary>Every push this connection hears, other than presence.</summary>
    private static Channel<(long Version, string Reason)> Pushes(HubConnection connection)
    {
        var pushes = Channel.CreateUnbounded<(long, string)>();
        connection.On<Guid, long, string>("MatchSnapshotChanged", (_, version, reason) =>
        {
            if (reason != "ParticipantConnectionChanged")
            {
                pushes.Writer.TryWrite((version, reason));
            }
        });
        return pushes;
    }

    private sealed record Table(Guid MatchId, string BlueToken, string RedToken, Guid BlueShip, Guid RedShip, Guid BlueMount);
}
