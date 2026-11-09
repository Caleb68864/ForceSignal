using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The turn's events, in the order the server resolved them, each tied to its log sentence.
/// </summary>
/// <remarks>
/// Turn playback shows a turn as it happened. Salvo strikes, point defence, threshold checks and
/// the systems they took used to reach a client only as English in the battle log, and nothing
/// placed them among the shots - so a client could show them only by parsing that English.
/// </remarks>
public sealed class InMemoryMatchServiceTurnEventsTests
{
    [Fact]
    public void EventsComeInTheOrderTheTurnWasResolved()
    {
        var table = Table.Build();
        var turn = table.PlayTurn();

        var kinds = turn.TurnEvents!.Select(e => e.Kind).ToList();
        int First(string kind) => kinds.IndexOf(kind);

        // Fighters fly before the ships move; the salvo strikes as movement ends, before any shot;
        // the kill comes straight after the shot that did it; point defence meets the strike before
        // the strike's own dice.
        Assert.True(First(TurnEventKinds.FighterFlew) < First(TurnEventKinds.MovementResolved));
        Assert.True(First(TurnEventKinds.MovementResolved) < First(TurnEventKinds.SalvoStruck));
        Assert.True(First(TurnEventKinds.SalvoStruck) < First(TurnEventKinds.WeaponFired));
        var kill = turn.TurnEvents!.Single(e => e.Kind == TurnEventKinds.ShipDestroyed && e.TargetShipId == table.PicketId);
        var killIndex = turn.TurnEvents!.ToList().IndexOf(kill);
        Assert.Equal(TurnEventKinds.WeaponFired, kinds[killIndex - 1]);
        Assert.Equal(table.PicketId, turn.TurnEvents![killIndex - 1].TargetShipId);
        var defence = First(TurnEventKinds.PointDefenseFired);
        Assert.True(defence > killIndex);
        Assert.Contains(kinds[defence + 1], new[] { TurnEventKinds.WeaponFired, TurnEventKinds.FighterStrikeStopped });
    }

    [Fact]
    public void EveryEventPointsAtTheLogSentenceThatDescribesIt()
    {
        var turn = Table.Build().PlayTurn();
        var expected = new Dictionary<string, string[]>
        {
            [TurnEventKinds.FighterFlew] = ["Fighters"],
            [TurnEventKinds.MovementResolved] = ["Movement"],
            [TurnEventKinds.SalvoStruck] = ["Ordnance"],
            [TurnEventKinds.SalvoWasted] = ["Ordnance"],
            [TurnEventKinds.PointDefenseFired] = ["PointDefense"],
            [TurnEventKinds.FighterStrikeStopped] = ["Fire"],
            [TurnEventKinds.WeaponFired] = ["Fire"],
            [TurnEventKinds.ThresholdChecked] = ["Threshold"],
            [TurnEventKinds.ShipDestroyed] = ["Fire", "Ordnance"],
        };

        Assert.NotEmpty(turn.TurnEvents!);
        foreach (var turnEvent in turn.TurnEvents!)
        {
            var entry = turn.MatchLog.Single(log => log.Sequence == turnEvent.Sequence);
            Assert.Contains(entry.Category, expected[turnEvent.Kind]);
            Assert.Equal(turn.TurnNumber, turnEvent.TurnNumber);
        }
    }

    [Fact]
    public void EveryShotHasExactlyOneEvent_InOrder()
    {
        var turn = Table.Build().PlayTurn();

        var shots = turn.TurnEvents!.Where(e => e.Kind == TurnEventKinds.WeaponFired).ToList();
        Assert.Equal(Enumerable.Range(0, turn.FiringResults.Count), shots.Select(e => e.FiringResultIndex!.Value));
        for (var index = 0; index < shots.Count; index++)
        {
            Assert.Equal(turn.FiringResults[index].AttackerShipId, shots[index].ActorShipId);
            Assert.Equal(turn.FiringResults[index].TargetShipId, shots[index].TargetShipId);
            Assert.NotNull(shots[index].From);
            Assert.NotNull(shots[index].To);
        }
    }

    [Fact]
    public void TheSalvoAndPointDefenceCarryTheirNumbers()
    {
        var turn = Table.Build().PlayTurn();

        var salvo = turn.TurnEvents!.Single(e => e.Kind == TurnEventKinds.SalvoStruck);
        Assert.NotNull(salvo.Launched);
        Assert.NotNull(salvo.Arriving);
        Assert.NotNull(salvo.Surviving);
        // Boxes crossed off never exceed what the dice scored.
        Assert.True((salvo.ArmorApplied ?? 0) + (salvo.HullApplied ?? 0) <= salvo.Damage);
        var defence = turn.TurnEvents!.Single(e => e.Kind == TurnEventKinds.PointDefenseFired);
        Assert.NotEmpty(defence.Rolls!);
        Assert.NotNull(defence.Kills);
    }

    [Fact]
    public void TheTurnIsKeptWholeOnceTheNextOneStarts()
    {
        var table = Table.Build();
        var played = table.PlayTurn();

        var next = table.Service.AdvanceTurn(table.MatchId, table.OwnerToken);

        Assert.Equal(2, next.TurnNumber);
        // Only what has happened in turn two so far, which is nothing.
        Assert.Empty(next.TurnEvents!);
        Assert.NotNull(next.LastTurn);
        Assert.Equal(1, next.LastTurn!.TurnNumber);
        Assert.Equal(Describe(played.TurnEvents!), Describe(next.LastTurn.Events));
        Assert.Equal(played.FiringResults.Count, next.LastTurn.FiringResults.Count);
        Assert.Equal(played.MovementResults.Select(m => m.ShipId), next.LastTurn.MovementResults.Select(m => m.ShipId));
    }

    [Fact]
    public void EventsAndTheLastTurnSurviveARestart()
    {
        var store = new MemoryStore();
        var table = Table.Build(store);
        var played = table.PlayTurn();

        var reloaded = new InMemoryMatchService(null, store, loadPersisted: true).GetSnapshot(table.MatchId);
        Assert.Equal(Describe(played.TurnEvents!), Describe(reloaded.TurnEvents!));

        var next = table.Service.AdvanceTurn(table.MatchId, table.OwnerToken);
        var reloadedNext = new InMemoryMatchService(null, store, loadPersisted: true).GetSnapshot(table.MatchId);
        Assert.Equal(Describe(next.LastTurn!.Events), Describe(reloadedNext.LastTurn!.Events));
        Assert.Equal(next.LastTurn.FiringResults.Count, reloadedNext.LastTurn.FiringResults.Count);
    }

    private static string Describe(IEnumerable<TurnEventDto> events) =>
        string.Join('|', events.Select(e => $"{e.Sequence}:{e.Kind}:{e.ActorShipId}:{e.TargetShipId}:{e.FiringResultIndex}:{e.Kills}:{e.HullApplied}:{string.Join(',', e.LostSystems ?? [])}"));

    private sealed record Table(InMemoryMatchService Service, Guid MatchId, string OwnerToken, string OpponentToken,
        Guid LanceId, Guid HawkId, Guid PicketId, Guid BulwarkId, Guid BeamId, Guid StrikeId)
    {
        public static Table Build(IMatchStore? store = null)
        {
            // Every die a 6: the beam's dice all score, the initiative ties and falls to the host.
            var dice = new ScriptedDice { Fallback = 6 };
            var service = store is null ? new InMemoryMatchService(dice.Next) : new InMemoryMatchService(dice.Next, store, loadPersisted: true);
            var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Events", 72, 48, Rules: TestRules.Invented));
            var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
            var blue = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
            var red = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null)).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

            Guid beam = Guid.NewGuid(), strike = Guid.NewGuid();
            var lance = service.CreateShip(blue.Id, new CreateShipRequest(owner.ParticipantToken, "Lance", "Cruiser", 4, 0, 3, 14, 0,
                StartX: 18, StartY: 24, FireControlMax: 2, Weapons: [new WeaponMountDto(beam, "Battery", 6, 40, [.. FiringArcs.Firable])])).Ships.Single(s => s.Name == "Lance").Id;
            var hawk = service.CreateShip(blue.Id, new CreateShipRequest(owner.ParticipantToken, "Hawk Flight", "Fighter Group", 6, 0, 3, 6, 0,
                StartX: 30, StartY: 30, IconKey: "fighter-group", FighterEnduranceMax: 6, FighterMaxRange: 36, FighterStatus: "Airborne", FireControlMax: 1,
                Weapons: [new WeaponMountDto(strike, "Fighter Attack", 6, 6, [.. FiringArcs.Firable])])).Ships.Single(s => s.Name == "Hawk Flight").Id;
            var bulwark = service.CreateShip(red.Id, new CreateShipRequest(opponent.ParticipantToken, "Bulwark", "Cruiser", 4, 0, 9, 40, 4,
                StartX: 42, StartY: 24, PointDefenseSystems: 2)).Ships.Single(s => s.Name == "Bulwark").Id;
            var picket = service.CreateShip(red.Id, new CreateShipRequest(opponent.ParticipantToken, "Picket", "Escort", 4, 0, 9, 1, 0,
                StartX: 30, StartY: 24)).Ships.Single(s => s.Name == "Picket").Id;

            service.SetReady(owner.MatchId, owner.ParticipantToken, true);
            service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
            return new Table(service, owner.MatchId, owner.ParticipantToken, opponent.ParticipantToken, lance, hawk, picket, bulwark, beam, strike);
        }

        /// <summary>Flies the flight, launches a salvo, moves, then fires: a kill, and a fighter strike into point defence.</summary>
        public MatchSnapshotDto PlayTurn()
        {
            Service.CreateOrdnanceMarker(MatchId, new CreateOrdnanceMarkerRequest(
                OwnerToken, "Salvo One", "Salvo", LanceId, BulwarkId, 42, 24, Course: 9, Speed: 0, EnduranceRemaining: 1, AttackDice: 0, MaxRange: 24));
            Service.MoveFighterGroup(MatchId, new MoveFighterGroupRequest(OwnerToken, HawkId, 38, 24));
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OwnerToken));
            Service.DeclareOrdersComplete(MatchId, new DeclareOrdersCompleteRequest(OpponentToken));
            var firing = Service.AdvanceTurn(MatchId, OwnerToken);
            Assert.Equal("Firing", firing.Phase);

            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, LanceId, PicketId, BeamId, 12));
            Service.CeaseFire(MatchId, new CeaseFireRequest(OwnerToken, LanceId));
            Service.FireWeapon(MatchId, new FireWeaponRequest(OwnerToken, HawkId, BulwarkId, StrikeId, 4));
            return Service.CeaseFire(MatchId, new CeaseFireRequest(OwnerToken, HawkId));
        }
    }
}
