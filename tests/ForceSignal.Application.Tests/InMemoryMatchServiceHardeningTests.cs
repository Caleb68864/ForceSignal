using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Covers the guards that keep a match usable when the input is hostile, mistaken, or simply much
/// larger than a game ever is: room-code strength, seat integrity in a hand-edited snapshot, and
/// the ceilings that stop one match consuming the whole machine.
/// </summary>
public sealed class InMemoryMatchServiceHardeningTests
{
    [Fact]
    public void JoinCodes_AreDrawnFromAWideEnoughSpaceToNotRepeatAcrossAFullEvening()
    {
        var service = new InMemoryMatchService();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 200; i++)
        {
            codes.Add(service.CreateMatch(new CreateMatchRequest("Blue", $"Match {i}")).JoinCode);
        }

        // Every code is unique by construction; the point of the check is that generating 200 of
        // them neither collides its way into a loop nor exhausts the word space.
        Assert.Equal(200, codes.Count);
        Assert.All(codes, code => Assert.Matches("^[A-Z0-9-]+$", code));
    }

    [Fact]
    public void JoinCode_IsRefusedWhenItIsNotTheOneThatWasIssued()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Wrong Code"));

        // A near miss on the real code is still a miss.
        var wrong = owner.JoinCode[..^1] + (owner.JoinCode[^1] == 'A' ? 'B' : 'A');
        Assert.Throws<InvalidOperationException>(() => service.JoinMatch(new JoinMatchRequest(wrong, "Red")));
    }

    [Fact]
    public void ParticipantToken_FromAnotherMatchIsRefused()
    {
        var service = new InMemoryMatchService();
        var blue = service.CreateMatch(new CreateMatchRequest("Blue", "Blue Match"));
        var red = service.CreateMatch(new CreateMatchRequest("Red", "Red Match"));

        // A valid token is still only valid for the match that issued it.
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.CreateFleet(blue.MatchId, new CreateFleetRequest(red.ParticipantToken, "Interloper", null)));
    }

    [Fact]
    public void RestoredMatch_AlwaysHasAnOwnerSoTheTurnCanStillBeAdvanced()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "No Owner"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));
        var exported = service.GetSnapshot(owner.MatchId);

        // A hand-edited snapshot where nobody is the owner would otherwise restore into a match no
        // one can advance.
        var ownerless = exported with
        {
            Participants = [.. exported.Participants.Select(p => p with { Role = "Player" })],
        };

        var restored = new InMemoryMatchService().RestoreMatch(ownerless, null);
        Assert.Contains(restored.Seats, seat => seat.Role == "Owner");
    }

    [Fact]
    public void RestoredMatch_GivesDuplicateSeatIdsDistinctSeats()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Twin Seats"));
        var opponent = service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "Red"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));
        var exported = service.GetSnapshot(owner.MatchId);

        var collided = exported with
        {
            Participants = [.. exported.Participants.Select(p => p with { Id = exported.Participants[0].Id })],
        };

        var restored = new InMemoryMatchService().RestoreMatch(collided, null);
        Assert.Equal(2, restored.Seats.Select(seat => seat.ParticipantId).Distinct().Count());
        Assert.NotEqual(Guid.Empty, opponent.ParticipantId);
    }

    [Fact]
    public void RestoredMatch_RefusesASnapshotCarryingMoreShipsThanAMatchTracks()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Too Many"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));
        var exported = service.GetSnapshot(owner.MatchId);

        var template = exported.Ships[0];
        var bloated = exported with
        {
            Ships = [.. Enumerable.Range(0, 500).Select(i => template with { Id = Guid.NewGuid(), Name = $"Hull {i}" })],
        };

        var refused = Assert.Throws<InvalidOperationException>(() => new InMemoryMatchService().RestoreMatch(bloated, null));
        Assert.Contains("ships", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisplayText_IsTruncatedRatherThanStoredAtAnyLength()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", new string('N', 5000)));
        var snapshot = service.CreateFleet(owner.MatchId, new CreateFleetRequest(
            owner.ParticipantToken,
            new string('F', 5000),
            new string('X', 5000)));

        Assert.True(snapshot.Name.Length <= 120, $"match name was {snapshot.Name.Length} characters");
        var fleet = snapshot.Fleets.Single();
        Assert.True(fleet.Name.Length <= 120, $"fleet name was {fleet.Name.Length} characters");
        Assert.True(fleet.Faction!.Length <= 120, $"faction was {fleet.Faction.Length} characters");
    }

    [Fact]
    public void BattleLog_StopsGrowingOnceItReachesItsCeiling()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Long Game"));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();

        // Each edit writes a log line, so this is the cheapest way to run the log past its cap.
        var ship = service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant")).Ships.Single();
        for (var i = 0; i < 4200; i++)
        {
            service.UpdateShipDamage(ship.Id, new UpdateShipDamageRequest(owner.ParticipantToken, i % 3, 0, 0, 0, 0, 0, 0));
        }

        var snapshot = service.GetSnapshot(owner.MatchId);
        Assert.True(snapshot.MatchLog.Count <= 4000, $"log held {snapshot.MatchLog.Count} entries");

        // Trimming must not restart the sequence: two events in one game never share a number.
        Assert.Equal(
            snapshot.MatchLog.Count,
            snapshot.MatchLog.Select(entry => entry.Sequence).Distinct().Count());
        Assert.Equal(
            snapshot.MatchLog.Select(entry => entry.Sequence).OrderBy(sequence => sequence).ToArray(),
            snapshot.MatchLog.Select(entry => entry.Sequence).ToArray());
    }

    [Fact]
    public void Fleets_StopBeingAcceptedOnceTheMatchHoldsItsCeiling()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Fleet Flood"));
        for (var i = 0; i < 32; i++)
        {
            service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, $"Fleet {i}", null));
        }

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "One Too Many", null)));
        Assert.Contains("fleets", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CreateShipRequest Ship(string token, string name) => new(
        token, name, "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24);
}
