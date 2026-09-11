using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The four things that have to be true of a live match, asserted once per way of becoming one.
/// </summary>
/// <remarks>
/// <para>
/// A match becomes live by being put in <c>_matches</c>, having its join code indexed, having its
/// fleets, ships and markers indexed, and being given its <c>Persist</c> delegate. Teardown was
/// centralised in <c>Forget</c> and correctly reverses all four; setup was written out by hand in
/// three siblings of one partial class - <c>CreateMatch</c>, <c>LoadPersistedMatches</c> and
/// <c>RestoreMatch</c> - and they had already diverged, with <c>CreateMatch</c> not indexing at all.
/// </para>
/// <para>
/// The divergence was harmless where it sat: a newly created match has nothing to index yet. What it
/// was not is safe, because the rule had become "four steps, except when it is three". This holds
/// every entrance to the same four, so a fourth one - or a fifth step - cannot quietly be three.
/// </para>
/// <para>
/// Asserted through the public surface rather than by reading the dictionaries: each index is the
/// only way one of these ids can be resolved, so a lookup that answers proves the index carries it.
/// </para>
/// </remarks>
public sealed class MatchRegistrationTests
{
    [Fact]
    public void ACreatedMatchIsLiveEveryWayAMatchCanBe()
    {
        var store = new MemoryStore();
        var service = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Fresh", Rules: TestRules.Invented));

        AssertLive(service, store, owner.MatchId, owner.JoinCode, owner.ParticipantToken);
    }

    [Fact]
    public void AMatchLoadedFromTheStoreAtStartupIsLiveEveryWayAMatchCanBe()
    {
        var store = new MemoryStore();
        var before = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = before.CreateMatch(new CreateMatchRequest("Blue", "Restarted", Rules: TestRules.Invented));
        Populate(before, owner.MatchId, owner.ParticipantToken);

        // The process goes away and a new one opens the same file.
        var after = new InMemoryMatchService(null, store, loadPersisted: true);

        AssertLive(after, store, owner.MatchId, owner.JoinCode, owner.ParticipantToken);
    }

    [Fact]
    public void AMatchRestoredFromAnExportIsLiveEveryWayAMatchCanBe()
    {
        var store = new MemoryStore();
        var source = new InMemoryMatchService(null, store, loadPersisted: true);
        var owner = source.CreateMatch(new CreateMatchRequest("Blue", "Exported", Rules: TestRules.Invented));
        Populate(source, owner.MatchId, owner.ParticipantToken);
        var exported = source.GetSnapshot(owner.MatchId);

        var target = new MemoryStore();
        var service = new InMemoryMatchService(null, target, loadPersisted: true);
        var restored = service.RestoreMatch(exported, savedAt: DateTimeOffset.UtcNow);

        // Restore reissues ids and tokens, so the seat has to be claimed before anything is command-
        // able - which is the documented difference between this path and the persistence one.
        var seat = service.GetSeats(restored.MatchId, restored.JoinCode)[0];
        var claimed = service.ClaimSeat(restored.MatchId, seat.ParticipantId, new ClaimSeatRequest("Blue", restored.JoinCode));

        AssertLive(service, target, restored.MatchId, restored.JoinCode, claimed.ParticipantToken);
    }

    /// <summary>Gives the match something in each of the three entity indexes.</summary>
    private static void Populate(InMemoryMatchService service, Guid matchId, string token)
    {
        var fleet = service.CreateFleet(matchId, new CreateFleetRequest(token, "Blue Watch", null)).Fleets.Single();
        service.CreateShip(fleet.Id, new CreateShipRequest(
            token, "Valiant", "Cruiser", 4, 0, 1, 10, 2, 12, 24, 0, null, "cruiser"));
        service.CreateOrdnanceMarker(matchId, new CreateOrdnanceMarkerRequest(
            token, "Salvo One", "Salvo", null, null, 20, 24, 1, 4, 4, 4, 4));
    }

    private static void AssertLive(
        InMemoryMatchService service,
        MemoryStore store,
        Guid matchId,
        string joinCode,
        string token)
    {
        // 1. Findable by id.
        var snapshot = service.GetSnapshot(matchId);
        Assert.Equal(matchId, snapshot.MatchId);

        // 2. Findable by join code. `GetSeats` is the reader that takes one.
        Assert.NotEmpty(service.GetSeats(matchId, joinCode));

        // 3. Persisting. Touching the match has to reach the store, which is the step that has
        //    nothing to do with any dictionary and so would survive every index being dropped.
        //    Read after a mutation rather than at rest, because a row already written by an earlier
        //    step would make this pass on a match whose `Persist` had been dropped since.
        service.UpdateTable(matchId, new UpdateMatchTableRequest(token, 48, 48));
        Assert.Contains(matchId, store.Rows.Keys);
        Assert.Contains("\"tableWidth\":48", store.Rows[matchId], StringComparison.Ordinal);

        // 4. Indexed, per entity that exists. Each of these ids can only be resolved through its own
        //    index, so an answer here is the index carrying it. A match with nothing to index skips
        //    this honestly rather than asserting against an empty list.
        foreach (var fleet in snapshot.Fleets)
        {
            Assert.NotEmpty(service.CreateShip(fleet.Id, new CreateShipRequest(
                token, "Indexed", "Escort", 0, 0, 1, 4, 0, 10, 10, 0, null, "escort")).Ships);
        }

        foreach (var ship in snapshot.Ships)
        {
            Assert.Equal(1, service.UpdateShipDamage(ship.Id, new UpdateShipDamageRequest(token, 1, 0, 0, 0, 0, 0, 0))
                .Ships.Single(s => s.Id == ship.Id).HullDamage);
        }

        foreach (var marker in snapshot.OrdnanceMarkers)
        {
            service.RemoveOrdnanceMarker(marker.Id, new RemoveOrdnanceMarkerRequest(token));
        }
    }
}
