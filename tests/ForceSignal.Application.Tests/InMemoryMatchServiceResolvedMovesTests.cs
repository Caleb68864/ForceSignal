using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Once movement resolves, the snapshot carries every move as it was actually flown.
/// </summary>
/// <remarks>
/// The client used to walk each revealed order's legs backwards from where the ship ended up. That
/// drew a hull the table edge had stopped from the wrong place, drew a move for a ship wrecked
/// before it could make one, and drew nothing at all for a ship that drifted with no order. Turn
/// playback cannot show a move it cannot see, so these are the server's to say.
/// </remarks>
public sealed class InMemoryMatchServiceResolvedMovesTests
{
    [Fact]
    public void EveryShipThatMovedHasItsPath_AndNoOtherShipDoes()
    {
        var table = Table.Build();
        var firing = table.ResolveMovement();

        // Exactly the ships whose position changed: everyone but the wreck moves on this table.
        var moved = table.Before.Ships
            .Where(before => firing.Ships.Single(after => after.Id == before.Id) is var after
                && (after.PositionX != before.PositionX || after.PositionY != before.PositionY))
            .Select(ship => ship.Id)
            .ToHashSet();
        Assert.Equal(moved, firing.MovementResults.Select(result => result.ShipId).ToHashSet());

        foreach (var result in firing.MovementResults)
        {
            var before = table.Before.Ships.Single(ship => ship.Id == result.ShipId);
            var after = firing.Ships.Single(ship => ship.Id == result.ShipId);
            Assert.NotNull(result.Path);
            Assert.Equal(new TablePointDto(before.PositionX, before.PositionY), result.Path![0]);
            Assert.Equal(new TablePointDto(after.PositionX, after.PositionY), result.Path[^1]);
            Assert.Equal(result.Segments!.Count + 1, result.Path.Count);
        }
    }

    [Fact]
    public void AShipWithNoOrderIsShownDrifting()
    {
        var table = Table.Build();
        var firing = table.ResolveMovement();

        var drift = firing.MovementResults.Single(result => result.ShipId == table.DrifterId);
        Assert.True(drift.Drifted);
        Assert.Equal([new TablePointDto(10, 10), new TablePointDto(10, 14)], drift.Path);
    }

    [Fact]
    public void AShipWreckedBeforeMovementHasNoMove()
    {
        var table = Table.Build();
        var firing = table.ResolveMovement();

        Assert.DoesNotContain(firing.MovementResults, result => result.ShipId == table.WreckId);
        // Its order was revealed, and during movement its plan was on the snapshot like any other.
        Assert.Contains(table.Revealed.MovementResults, result => result.ShipId == table.WreckId);
    }

    [Fact]
    public void AHullTheEdgeStoppedStartsWhereItReallyStarted()
    {
        var table = Table.Build();
        var firing = table.ResolveMovement();

        var runner = firing.MovementResults.Single(result => result.ShipId == table.RunnerId);
        Assert.True(runner.Clamped);
        Assert.Equal(new TablePointDto(66, 8), runner.Path![0]);
        Assert.Equal(new TablePointDto(72, 8), runner.Path[^1]);
    }

    [Fact]
    public void BeforeMovementResolves_TheResultsAreOnlyPlans()
    {
        var table = Table.Build();

        Assert.Equal("Movement", table.Revealed.Phase);
        Assert.All(table.Revealed.MovementResults, result => Assert.Null(result.Path));
    }

    [Fact]
    public void TheMovesAreGoneOnceTheTurnEnds()
    {
        var table = Table.Build();
        table.ResolveMovement();

        var next = table.Service.AdvanceTurn(table.MatchId, table.OwnerToken);

        Assert.Equal("OrderEntry", next.Phase);
        Assert.Empty(next.MovementResults);
    }

    [Fact]
    public void TheMovesSurviveARestart()
    {
        var store = new MemoryStore();
        var table = Table.Build(store);
        var firing = table.ResolveMovement();

        var after = new InMemoryMatchService(null, store, loadPersisted: true).GetSnapshot(table.MatchId);

        Assert.Equal(
            firing.MovementResults.Select(result => (result.ShipId, result.Drifted, result.Clamped, Path: string.Join(';', result.Path!))),
            after.MovementResults.Select(result => (result.ShipId, result.Drifted, result.Clamped, Path: string.Join(';', result.Path!))));
    }

    [Fact]
    public void TheMovesComeBackWithABackupRestoredMidFiring()
    {
        var table = Table.Build();
        var exported = table.ResolveMovement();

        var restored = new InMemoryMatchService().RestoreMatch(exported, DateTimeOffset.UtcNow).Snapshot;

        // Ids are reissued on restore, so the moves are matched up by ship name.
        string Describe(MatchSnapshotDto snapshot) => string.Join('|', snapshot.MovementResults
            .Select(result => $"{snapshot.Ships.Single(ship => ship.Id == result.ShipId).Name}:{result.Drifted}:{result.Clamped}:{string.Join(';', result.Path!)}")
            .Order());
        Assert.Equal(Describe(exported), Describe(restored));
        Assert.DoesNotContain(restored.MovementResults, result => restored.Ships.Single(ship => ship.Id == result.ShipId).Name == "Wreck");
    }

    private sealed record Table(
        InMemoryMatchService Service,
        Guid MatchId,
        string OwnerToken,
        Guid RunnerId,
        Guid DrifterId,
        Guid WreckId,
        MatchSnapshotDto Before,
        MatchSnapshotDto Revealed)
    {
        public static Table Build(IMatchStore? store = null)
        {
            var service = store is null ? new InMemoryMatchService(_ => 4) : new InMemoryMatchService(_ => 4, store, loadPersisted: true);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Moves", 72, 48, Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blue = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var red = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null)).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            Guid Ship(string token, Guid fleetId, string name, int velocity, int course, decimal x, decimal y) =>
                service.CreateShip(fleetId, new CreateShipRequest(token, name, "Escort", 4, velocity, course, 4, 0, StartX: x, StartY: y))
                    .Ships.Single(s => s.Name == name).Id;

            var lance = Ship(owner.ParticipantToken, blue.Id, "Lance", 6, 3, 18, 24);
            var runner = Ship(owner.ParticipantToken, blue.Id, "Edge Runner", 8, 3, 66, 8);
            var drifter = Ship(owner.ParticipantToken, blue.Id, "Drifter", 4, 6, 10, 10);
            var bulwark = Ship(opponent.ParticipantToken, red.Id, "Bulwark", 4, 9, 46, 24);
            var wreck = Ship(opponent.ParticipantToken, red.Id, "Wreck", 4, 9, 60, 40);

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            var before = service.GetSnapshot(owner.MatchId);

            var orders = new (string Token, Guid Ship, MovementOrder Order)[]
            {
                (owner.ParticipantToken, lance, new MovementOrder(0, 2, TurnDirection.None, [new TurnManeuver(TurnDirection.Starboard, 1), new TurnManeuver(TurnDirection.Port, 1)])),
                (owner.ParticipantToken, runner, new MovementOrder(0, 0, TurnDirection.None)),
                (opponent.ParticipantToken, bulwark, new MovementOrder(0, 0, TurnDirection.None)),
                (opponent.ParticipantToken, wreck, new MovementOrder(0, 0, TurnDirection.None)),
            };
            foreach (var (token, ship, order) in orders)
            {
                service.CommitOrder(owner.MatchId, new CommitOrderRequest(token, ship, order, $"salt-{ship}"));
            }

            // The Drifter writes nothing and holds course.
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(owner.ParticipantToken));
            service.DeclareOrdersComplete(owner.MatchId, new DeclareOrdersCompleteRequest(opponent.ParticipantToken));
            foreach (var (token, ship, order) in orders)
            {
                service.RevealOrder(owner.MatchId, new RevealOrderRequest(token, ship, order, $"salt-{ship}"));
            }

            // Wrecked after its order is public: the order is discarded when movement resolves.
            var revealed = service.UpdateShipDamage(wreck, new UpdateShipDamageRequest(opponent.ParticipantToken, 4, 0, 0, 0, 0));

            return new Table(service, owner.MatchId, owner.ParticipantToken, runner, drifter, wreck, before, revealed);
        }

        public MatchSnapshotDto ResolveMovement() => Service.AdvanceTurn(MatchId, OwnerToken);
    }
}
