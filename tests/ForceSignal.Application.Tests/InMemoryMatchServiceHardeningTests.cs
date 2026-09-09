using ForceSignal.Application;
using ForceSignal.Domain.Rules;
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
            codes.Add(service.CreateMatch(new CreateMatchRequest("Blue", $"Match {i}", Rules: TestRules.Invented)).JoinCode);
        }

        // Every code is unique by construction; the point of the check is that generating 200 of
        // them neither collides its way into a loop nor exhausts the word space.
        Assert.Equal(200, codes.Count);
        Assert.All(codes, code => Assert.Matches("^[A-Z0-9-]+$", code));
    }

    [Fact]
    public void JoinCodes_AreDrawnFromASpaceTooLargeForAGuesserToWalk()
    {
        var service = new InMemoryMatchService();
        var words = new HashSet<string>(StringComparer.Ordinal);
        var slotCounts = new HashSet<int>();
        for (var i = 0; i < 400; i++)
        {
            var parts = service.CreateMatch(new CreateMatchRequest("Blue", $"Match {i}", Rules: TestRules.Invented))
                .JoinCode.Split('-');
            slotCounts.Add(parts.Length);
            foreach (var part in parts)
            {
                words.Add(part);
            }
        }

        // Four hundred codes is well short of crowding the space, so none of them should have taken
        // the numeric-suffix fallback and every code should be the same shape.
        var slots = Assert.Single(slotCounts);

        // Four hundred codes is 1,600 draws from the list, so a word going unseen is vanishingly
        // unlikely; the margin is here so the test cannot flake rather than because it is expected
        // to be used.
        Assert.True(words.Count >= 60, $"Only {words.Count} distinct code words were ever drawn.");

        // The property that actually matters, stated as the number it is. A room code is the only
        // thing between a stranger and a seat, so the space it comes from has to stay far enough
        // ahead of the rate limiter that walking it is hopeless. Three slots out of thirty-two words
        // was 32,768 codes - a server holding a few hundred live matches was one a script found a
        // seat in within a couple of hundred tries.
        Assert.True(
            Math.Pow(words.Count, slots) >= 1_000_000,
            $"{words.Count} words in {slots} slots is only {Math.Pow(words.Count, slots):N0} codes.");
    }

    [Fact]
    public void JoinCode_IsRefusedWhenItIsNotTheOneThatWasIssued()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Wrong Code", Rules: TestRules.Invented));

        // A near miss on the real code is still a miss.
        var wrong = owner.JoinCode[..^1] + (owner.JoinCode[^1] == 'A' ? 'B' : 'A');
        Assert.Throws<NotFoundException>(() => service.JoinMatch(new JoinMatchRequest(wrong, "Red")));
    }

    [Fact]
    public void ParticipantToken_FromAnotherMatchIsRefused()
    {
        var service = new InMemoryMatchService();
        var blue = service.CreateMatch(new CreateMatchRequest("Blue", "Blue Match", Rules: TestRules.Invented));
        var red = service.CreateMatch(new CreateMatchRequest("Red", "Red Match", Rules: TestRules.Invented));

        // A valid token is still only valid for the match that issued it.
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.CreateFleet(blue.MatchId, new CreateFleetRequest(red.ParticipantToken, "Interloper", null)));
    }

    [Fact]
    public void RestoredMatch_AlwaysHasAnOwnerSoTheTurnCanStillBeAdvanced()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "No Owner", Rules: TestRules.Invented));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Twin Seats", Rules: TestRules.Invented));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Too Many", Rules: TestRules.Invented));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", new string('N', 5000), Rules: TestRules.Invented));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Long Game", Rules: TestRules.Invented));
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
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Fleet Flood", Rules: TestRules.Invented));
        for (var i = 0; i < 32; i++)
        {
            service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, $"Fleet {i}", null));
        }

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "One Too Many", null)));
        Assert.Contains("fleets", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AShipIsOnlyReachableThroughTheMatchThatHoldsIt()
    {
        var service = new InMemoryMatchService();
        var blue = service.CreateMatch(new CreateMatchRequest("Blue", "Blue Match", Rules: TestRules.Invented));
        var red = service.CreateMatch(new CreateMatchRequest("Red", "Red Match", Rules: TestRules.Invented));
        var blueFleet = service.CreateFleet(blue.MatchId, new CreateFleetRequest(blue.ParticipantToken, "Blue", null)).Fleets.Single();
        var blueShip = service.CreateShip(blueFleet.Id, Ship(blue.ParticipantToken, "Valiant")).Ships.Single();

        // Red's token addresses Blue's ship: the ship resolves, but not to Red.
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.UpdateShipDamage(blueShip.Id, new UpdateShipDamageRequest(red.ParticipantToken, 1, 0, 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void AnUnknownShipIdIsReportedAsMissingRatherThanFoundInSomeOtherMatch()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Missing Ship", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));

        var missing = Assert.Throws<NotFoundException>(() =>
            service.UpdateShipDamage(Guid.NewGuid(), new UpdateShipDamageRequest(owner.ParticipantToken, 1, 0, 0, 0, 0, 0, 0)));
        Assert.Contains("not found", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARemovedOrdnanceMarkerStopsBeingAddressable()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Marker Life", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));

        var marker = service.CreateOrdnanceMarker(owner.MatchId, new CreateOrdnanceMarkerRequest(
            owner.ParticipantToken, "Salvo One", "Salvo", null, null, 20, 24, 1, 12, 3, 6, 24))
            .OrdnanceMarkers.Single();

        service.RemoveOrdnanceMarker(marker.Id, new RemoveOrdnanceMarkerRequest(owner.ParticipantToken));

        var missing = Assert.Throws<NotFoundException>(() =>
            service.RemoveOrdnanceMarker(marker.Id, new RemoveOrdnanceMarkerRequest(owner.ParticipantToken)));
        Assert.Contains("not found", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpeningMoreMatchesThanTheServerHoldsIsRefusedRatherThanRetiringALiveGame()
    {
        var service = new InMemoryMatchService();
        var first = service.CreateMatch(new CreateMatchRequest("Blue", "First", Rules: TestRules.Invented));

        // Fill the server to its ceiling. Creating a match needs no credentials, so this is what a
        // stranger with a loop can do - and it used to retire the oldest live game to make room,
        // which handed that stranger every table in progress. Now the newcomer is turned away.
        for (var i = 1; i < 500; i++)
        {
            service.CreateMatch(new CreateMatchRequest("Blue", $"Match {i}", Rules: TestRules.Invented));
        }

        var refused = Assert.Throws<InvalidOperationException>(() =>
            service.CreateMatch(new CreateMatchRequest("Blue", "One Too Many", Rules: TestRules.Invented)));
        Assert.Contains("as many as it holds", refused.Message, StringComparison.OrdinalIgnoreCase);

        // The first match - the oldest, the one that used to give way - is untouched.
        Assert.Equal("First", service.GetSnapshot(first.MatchId).Name);
    }

    [Fact]
    public void AJoinCodeWithStraySpacesStillJoins()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Pasted Code", Rules: TestRules.Invented));

        // A code pasted from a message often arrives with a space on either end. The lookup
        // already forgave that; joining did not, so the same code worked one call and failed the
        // next.
        var joined = service.JoinMatch(new JoinMatchRequest($"  {owner.JoinCode}  ", "Red"));

        Assert.Equal(owner.MatchId, joined.MatchId);
    }

    [Fact]
    public void ANullMovementOrderIsARefusalNotAFault()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Null Order", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        var ship = service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant")).Ships.Single();
        Assert.Equal("OrderEntry", service.SetReady(owner.MatchId, owner.ParticipantToken, true).Phase);

        // The contract says the order is required, but a JSON null binds to it all the same. Each
        // of the three routes that read one used to fall over inside the rules; each now refuses.
        var preview = Assert.Throws<InvalidOperationException>(() =>
            service.PreviewOrder(owner.MatchId, new PreviewOrderRequest(owner.ParticipantToken, ship.Id, null!)));
        var commit = Assert.Throws<InvalidOperationException>(() =>
            service.CommitOrder(owner.MatchId, new CommitOrderRequest(owner.ParticipantToken, ship.Id, null!, "salt")));

        Assert.Contains("order is required", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order is required", commit.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShipAndWeaponNamesAreCutToTheDisplayCeilingOnEveryPathThatSetsThem()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Long Names", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        var ship = service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant")).Ships.Single();
        var tooLong = new string('x', 5000);

        // Creating a ship already truncated; updating it, duplicating it and naming its mounts did
        // not, and a name is replayed into every log line and every snapshot that mentions it.
        var updated = service.UpdateShipProfile(ship.Id, new UpdateShipProfileRequest(
            owner.ParticipantToken, tooLong, tooLong, 4, 6, 3, 12, 4, 20, 24,
            Weapons: [new WeaponMountDto(Guid.Empty, tooLong, 2, 24, [FiringArc.Fore], 0, 0, 0)]))
            .Ships.Single();
        Assert.Equal(120, updated.Name.Length);
        Assert.Equal(120, updated.ClassName!.Length);
        Assert.All(updated.Weapons, weapon => Assert.Equal(120, weapon.Name.Length));

        var copy = service.DuplicateShip(ship.Id, new DuplicateShipRequest(owner.ParticipantToken, tooLong))
            .Ships.Single(s => s.Id != ship.Id);
        Assert.Equal(120, copy.Name.Length);
    }

    [Fact]
    public void RestoredLogLinesAreBoundedLikeEveryOtherRestoredString()
    {
        var source = new InMemoryMatchService();
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Log Source", Rules: TestRules.Invented));
        var fleet = source.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        source.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));
        var exported = source.GetSnapshot(owner.MatchId);

        // The count of entries was capped; each entry's text was not, and a log line was a place
        // to park most of an eight-megabyte upload for a day.
        var padded = exported with
        {
            MatchLog =
            [
                new MatchLogEntryDto(1, DateTimeOffset.UtcNow, 1, new string('p', 5000), new string('c', 5000), new string('m', 50_000)),
            ],
        };

        var restored = new InMemoryMatchService();
        var snapshot = restored.GetSnapshot(restored.RestoreMatch(padded, null).MatchId);
        var entry = snapshot.MatchLog[0];
        Assert.Equal(120, entry.Phase.Length);
        Assert.Equal(120, entry.Category.Length);
        Assert.Equal(1000, entry.Message.Length);
    }

    [Fact]
    public void ClaimingASeatWithTheCodeInAnyCaseWorks_AndTheClaimersNameIsKept()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Case Code", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));
        var restored = new InMemoryMatchService();
        var rebuilt = restored.RestoreMatch(service.GetSnapshot(owner.MatchId), null);
        var seat = rebuilt.Seats.Single();

        // The code is read aloud and typed back; the comparison folds case before it compares in
        // constant time. And the name the device offers is honoured - the contract always said it
        // would be, and the service used to drop it on the floor.
        var session = restored.ClaimSeat(rebuilt.MatchId, seat.ParticipantId,
            new ClaimSeatRequest("  Admiral Case  ", rebuilt.JoinCode.ToLowerInvariant()));

        var claimed = restored.GetSnapshot(rebuilt.MatchId).Participants.Single(p => p.Id == session.ParticipantId);
        Assert.Equal("Admiral Case", claimed.DisplayName);
    }

    [Fact]
    public void ASeatCannotBeTakenByAnyoneWhoDoesNotHaveTheRoomCode()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Takeover", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
        service.CreateShip(fleet.Id, Ship(owner.ParticipantToken, "Valiant"));
        var restored = new InMemoryMatchService();
        var rebuilt = restored.RestoreMatch(service.GetSnapshot(owner.MatchId), null);
        var seat = rebuilt.Seats.Single();

        // Claiming mints a full participant token, and the owner's seat carries the table controls
        // and the turn. Knowing the match id must not be enough: the id is handed out by the
        // room-code lookup and echoed in every notification, so it proves nothing about belonging
        // at the table.
        Assert.Throws<UnauthorizedAccessException>(() =>
            restored.ClaimSeat(rebuilt.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName)));
        Assert.Throws<UnauthorizedAccessException>(() =>
            restored.ClaimSeat(rebuilt.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName, "WRONG-CODE-HERE")));

        // The player who was actually at the table has the code, which is how they got here.
        var session = restored.ClaimSeat(rebuilt.MatchId, seat.ParticipantId, new ClaimSeatRequest(seat.DisplayName, rebuilt.JoinCode));
        Assert.False(string.IsNullOrWhiteSpace(session.ParticipantToken));
    }

    private static CreateShipRequest Ship(string token, string name) => new(
        token, name, "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24);
}
