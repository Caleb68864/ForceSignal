using System.Text.Json;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The server half of the legacy-arc parity fixture.
/// </summary>
/// <remarks>
/// <para>
/// A fleet file names its arcs as text and two implementations read that text: <c>ExpandLegacyArc</c>
/// here and <c>expandLegacyArc</c> in <c>normalize.ts</c>. They agreed on every spelling either suite
/// exercised and disagreed on the one neither did - this side strips spaces <em>and</em> hyphens
/// before comparing, the client stripped only spaces and only in its fallback branch - so a weapons
/// cell reading <c>Fore-Port</c> resolved to ForePort here and fell back to Fore on screen. The
/// mount lost its port arc in front of the player while the server went on firing it through both.
/// </para>
/// <para>
/// <c>src/ForceSignal.Web/src/lib/legacyArcCases.json</c> holds the cases once and
/// <c>legacyArcCases.test.ts</c> runs the same list against the client.
/// </para>
/// <para>
/// Driven through <see cref="InMemoryMatchService.CreateShip"/> rather than by calling the private
/// expander, because that is the door a fleet import comes in at - a guard that called the helper
/// could pass while the create path did something else with its answer.
/// </para>
/// </remarks>
public sealed class LegacyArcParityTests
{
    [Fact]
    public void ALegacyArcNameExpandsToExactlyWhatTheSharedCaseListSays()
    {
        var cases = Cases();

        // Reached-the-subject: the fixture really loaded and really still carries the hyphenated
        // case this file was written for.
        Assert.True(cases.Count >= 13, $"only {cases.Count} cases loaded from the fixture");
        Assert.Contains(cases, entry => entry.Written == "Fore-Port");

        foreach (var entry in cases)
        {
            var arcs = ArcsForMountWritten(entry.Written);

            // Compared as sets: the order is not the subject and the two implementations build
            // their lists differently.
            Assert.Equal(
                entry.Arcs.Order(StringComparer.Ordinal),
                arcs.Select(arc => arc.ToString()).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void NoLegacyNameEverResurrectsTheBlindSpot()
    {
        // Rules-fidelity gap 3: no weapon fires through the aft arc. An expansion that handed it
        // back would seat a mount there through the import door, past every editor guard.
        Assert.DoesNotContain(FiringArc.Aft, ArcsForMountWritten("Aft"));
    }

    [Fact]
    public void AnExplicitArcListStillWinsOverTheLegacyName()
    {
        // The control that must be accepted. A normalizer that had started preferring the legacy
        // name would satisfy every case above and quietly overwrite what a modern file states.
        var (service, fleetId, token) = NewFleet();
        var weapon = new WeaponMountDto(Guid.NewGuid(), "Battery", 3, 24, new[] { FiringArc.AftPort })
        {
            Arc = "Fore-Port",
        };

        var ship = service.CreateShip(fleetId, Ship(token, "Explicit", weapon)).Ships.Single();
        Assert.Equal(new[] { FiringArc.AftPort }, ship.Weapons.Single().Arcs);
    }

    [Fact]
    public void ANameNeitherSideKnowsFallsBackRatherThanBeingGuessedAt()
    {
        // The dullest violation available: an expander that stripped every non-letter would turn
        // "Fore/Port" into an arc. Both sides fall back instead of inventing a bearing.
        Assert.Equal(new[] { FiringArc.Fore }, ArcsForMountWritten("Fore/Port"));
    }

    private static IReadOnlyList<FiringArc> ArcsForMountWritten(string legacyArc)
    {
        var (service, fleetId, token) = NewFleet();
        var weapon = new WeaponMountDto(Guid.NewGuid(), "Battery", 3, 24, Arcs: []) { Arc = legacyArc };

        var ship = service.CreateShip(fleetId, Ship(token, "Carrier Of One Mount", weapon)).Ships.Single();

        // Reached-the-subject: the mount survived normalization at all. A ship that named no
        // readable mount comes back with none, which would leave a Single() here throwing rather
        // than an assertion failing - but the message would be about the wrong thing.
        Assert.Single(ship.Weapons);

        // Never null off this path: a mount that named nothing readable still bears through Fore
        // rather than through nowhere, so a null here is itself the failure.
        return Assert.IsAssignableFrom<IReadOnlyList<FiringArc>>(ship.Weapons.Single().Arcs);
    }

    private static CreateShipRequest Ship(string token, string name, WeaponMountDto weapon) =>
        new(token, name, "Cruiser", 0, 0, 1, 10, 0, 12, 24, 0, [weapon], "cruiser");

    private static (InMemoryMatchService Service, Guid FleetId, string Token) NewFleet()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Arcs", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();
        return (service, fleet.Id, owner.ParticipantToken);
    }

    private sealed record Case(string Written, string[] Arcs);

    private static List<Case> Cases()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "ForceSignal.Web", "src", "lib", "legacyArcCases.json");
        Assert.True(File.Exists(path), $"the shared legacy-arc case list is not at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(item => new Case(
            item.GetProperty("written").GetString() ?? string.Empty,
            [.. item.GetProperty("arcs").EnumerateArray().Select(arc => arc.GetString() ?? string.Empty)]))];
    }

    /// <summary>The repository root, found rather than assumed, so this guard cannot pass by missing it.</summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ForceSignal.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"no ForceSignal.slnx above {AppContext.BaseDirectory}, so this guard cannot reach the fixture it is about");
    }
}
