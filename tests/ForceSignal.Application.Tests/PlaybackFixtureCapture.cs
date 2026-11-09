using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Plays one busy turn through the real service and writes the snapshots the web client's playback
/// tests read, so those tests run against what the server actually sends rather than a hand-written
/// guess at it.
/// </summary>
/// <remarks>
/// Off unless <c>FORCESIGNAL_WRITE_PLAYBACK_FIXTURES</c> names the directory to write into, because
/// ids and timestamps are fresh on every run and a test that rewrote checked-in files on every
/// <c>dotnet test</c> would leave every working tree dirty. Run it on purpose when the snapshot
/// contract changes:
/// <code>FORCESIGNAL_WRITE_PLAYBACK_FIXTURES=$PWD/src/ForceSignal.Web/src/lib/playback/fixtures dotnet test --filter PlaybackFixtureCapture</code>
/// The turn is chosen to put every case playback has to cope with on the table at once: a multi-leg
/// helm, a hull run into the table edge, a ship with no order, a ship wrecked after its order was
/// revealed, a fighter flight met by point defence, a salvo that strikes at the end of movement,
/// every weapon kind, a threshold check and a kill. The rules are the invented test set.
/// </remarks>
public sealed class PlaybackFixtureCapture
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        // The API's own setting (Program.cs): enums travel as names.
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    [Fact]
    public void CaptureBusyTurn()
    {
        var directory = Environment.GetEnvironmentVariable("FORCESIGNAL_WRITE_PLAYBACK_FIXTURES");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        void Write(string name, MatchSnapshotDto snapshot) =>
            File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(snapshot, WireJson) + "\n");

        // Seeded rather than scripted: the initiative die-off re-rolls ties, so a constant die never
        // settles it. The seed makes every run roll the same faces.
        var random = new Random(20261109);
        var service = new InMemoryMatchService(faces => random.Next(1, Math.Max(1, faces) + 1));
        var owner = service.CreateMatch(new CreateMatchRequest("Blue Admiral", "Playback Fixture", 72, 48, Rules: TestRules.Invented));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red Admiral"));
        var blue = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null, "#3b82f6")).Fleets.Single(f => f.OwnerParticipantId == owner.ParticipantId);
        var red = service.CreateFleet(owner.MatchId, new CreateFleetRequest(opponent.ParticipantToken, "Red", null, "#ef4444")).Fleets.Single(f => f.OwnerParticipantId == opponent.ParticipantId);

        Guid beam = Guid.NewGuid(), torpedo = Guid.NewGuid(), runnerBeam = Guid.NewGuid(), fighterAttack = Guid.NewGuid();
        Guid redBeam = Guid.NewGuid(), needle = Guid.NewGuid();
        var all = FiringArcs.Firable.ToArray();

        Guid Ship(string token, Guid fleetId, CreateShipRequest request) =>
            service.CreateShip(fleetId, request with { ParticipantToken = token }).Ships.Single(s => s.Name == request.Name).Id;

        var lance = Ship(owner.ParticipantToken, blue.Id, new CreateShipRequest("", "Lance", "Cruiser", 6, 6, 3, 14, 2,
            StartX: 18, StartY: 24, ScreenRating: 1, FireControlMax: 2,
            Weapons: [new WeaponMountDto(beam, "Battery A", 6, 30, all), new WeaponMountDto(torpedo, "Launcher", 1, 40, all, Kind: WeaponKind.PulseTorpedo)]));
        var runner = Ship(owner.ParticipantToken, blue.Id, new CreateShipRequest("", "Edge Runner", "Frigate", 4, 8, 3, 6, 0,
            StartX: 66, StartY: 8, FireControlMax: 1,
            Weapons: [new WeaponMountDto(runnerBeam, "Battery B", 2, 48, all)]));
        var drifter = Ship(owner.ParticipantToken, blue.Id, new CreateShipRequest("", "Drifter", "Escort", 2, 4, 6, 4, 0, StartX: 10, StartY: 10));
        var hawk = Ship(owner.ParticipantToken, blue.Id, new CreateShipRequest("", "Hawk Flight", "Fighter Group", 6, 0, 3, 6, 0,
            StartX: 28, StartY: 30, IconKey: "fighter-group", FighterEnduranceMax: 6, FighterMaxRange: 36, FighterStatus: "Airborne", FireControlMax: 1,
            Weapons: [new WeaponMountDto(fighterAttack, "Fighter Attack", 6, 6, [FiringArc.Fore])]));

        var bulwark = Ship(opponent.ParticipantToken, red.Id, new CreateShipRequest("", "Bulwark", "Cruiser", 4, 4, 9, 20, 4,
            StartX: 46, StartY: 24, ScreenRating: 1, PointDefenseSystems: 2, FireControlMax: 2,
            Weapons: [new WeaponMountDto(redBeam, "Battery R", 3, 30, all)]));
        var picket = Ship(opponent.ParticipantToken, red.Id, new CreateShipRequest("", "Picket", "Escort", 4, 2, 9, 1, 0, StartX: 42, StartY: 34));
        var needler = Ship(opponent.ParticipantToken, red.Id, new CreateShipRequest("", "Needler", "Destroyer", 4, 3, 9, 6, 0,
            StartX: 28, StartY: 22, FireControlMax: 1,
            Weapons: [new WeaponMountDto(needle, "Needle", 1, 7, all, Kind: WeaponKind.NeedleBeam)]));
        var wreck = Ship(opponent.ParticipantToken, red.Id, new CreateShipRequest("", "Wreck", "Escort", 4, 4, 9, 2, 0, StartX: 60, StartY: 40));

        service.SetReady(owner.MatchId, owner.ParticipantToken, true);
        service.SetReady(owner.MatchId, opponent.ParticipantToken, true);
        Write("01-turn1-order-entry.json", service.GetSnapshot(owner.MatchId));

        // A salvo aimed where the Bulwark will be once it has moved: it strikes at the end of
        // movement, before anyone fires, and is reported only in the log.
        service.CreateOrdnanceMarker(owner.MatchId, new CreateOrdnanceMarkerRequest(
            owner.ParticipantToken, "Salvo One", "Salvo", lance, bulwark, 42, 24, Course: 9, Speed: 0, EnduranceRemaining: 1, AttackDice: 0, MaxRange: 24));

        // The flight flies before movement resolves and leaves no movement record behind.
        service.MoveFighterGroup(owner.MatchId, new MoveFighterGroupRequest(owner.ParticipantToken, hawk, 38, 26));

        var orders = new (string Token, Guid Ship, MovementOrder Order)[]
        {
            (owner.ParticipantToken, lance, new MovementOrder(0, 2, TurnDirection.None, [new TurnManeuver(TurnDirection.Starboard, 1), new TurnManeuver(TurnDirection.Port, 1)])),
            (owner.ParticipantToken, runner, new MovementOrder(0, 0, TurnDirection.None)),
            (opponent.ParticipantToken, bulwark, new MovementOrder(0, 0, TurnDirection.None)),
            (opponent.ParticipantToken, picket, new MovementOrder(0, 1, TurnDirection.Port)),
            (opponent.ParticipantToken, needler, new MovementOrder(-2, 0, TurnDirection.None)),
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

        // Wrecked by hand after its order is public: its order is discarded, but its result stays.
        service.UpdateShipDamage(wreck, new UpdateShipDamageRequest(opponent.ParticipantToken, 2, 0, 0, 0, 0));
        Write("02-turn1-movement-revealed.json", service.GetSnapshot(owner.MatchId));

        var firing = service.AdvanceTurn(owner.MatchId, owner.ParticipantToken);
        Write("03-turn1-firing-opened.json", firing);

        // Each side's shots, in the order that side wants them. Ships alternate by initiative; a
        // ship fires everything it has queued, then ceases fire and hands the turn over.
        var queues = new Dictionary<string, Queue<(Guid Ship, FireWeaponRequest[] Shots)>>
        {
            [owner.ParticipantToken] = new([
                (lance, [new(owner.ParticipantToken, lance, picket, beam, 0), new(owner.ParticipantToken, lance, bulwark, torpedo, 0)]),
                (hawk, [new(owner.ParticipantToken, hawk, bulwark, fighterAttack, 0)]),
                (runner, [new(owner.ParticipantToken, runner, picket, runnerBeam, 0)]),
            ]),
            [opponent.ParticipantToken] = new([
                (needler, [new(opponent.ParticipantToken, needler, lance, needle, 0, TargetSystem: ShipSystemKind.FireControl)]),
                (bulwark, [new(opponent.ParticipantToken, bulwark, lance, redBeam, 0)]),
            ]),
        };
        var participantTokens = new Dictionary<Guid, string> { [owner.ParticipantId] = owner.ParticipantToken, [opponent.ParticipantId] = opponent.ParticipantToken };

        var midWritten = false;
        while (firing.FiringParticipantId is { } turnId && queues[participantTokens[turnId]].TryDequeue(out var activation))
        {
            foreach (var shot in activation.Shots)
            {
                // Declare the range the map measures, as the console would.
                var attacker = firing.Ships.Single(s => s.Id == shot.AttackerShipId);
                var target = firing.Ships.Single(s => s.Id == shot.TargetShipId);
                if (attacker.IsDestroyed || target.IsDestroyed)
                {
                    continue;
                }

                var range = (int)Math.Max(1, Math.Round(Math.Sqrt(Math.Pow((double)(target.PositionX - attacker.PositionX), 2) + Math.Pow((double)(target.PositionY - attacker.PositionY), 2))));
                firing = service.FireWeapon(owner.MatchId, shot with { Range = range });
            }

            if (!firing.ActivatedShipIds.Contains(activation.Ship) && firing.FiringParticipantId == turnId)
            {
                firing = service.CeaseFire(owner.MatchId, new CeaseFireRequest(participantTokens[turnId], activation.Ship));
            }

            if (!midWritten)
            {
                Write("04-turn1-firing-mid.json", firing);
                midWritten = true;
            }
        }

        Write("05-turn1-firing-complete.json", service.GetSnapshot(owner.MatchId));
        Write("06-turn2-order-entry.json", service.AdvanceTurn(owner.MatchId, owner.ParticipantToken));
    }
}
