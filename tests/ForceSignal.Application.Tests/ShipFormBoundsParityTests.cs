using System.Text.Json;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The service's clamps, held against the bounds the new-ship form types within.
/// </summary>
/// <remarks>
/// <para>
/// Each of these numbers is written twice: once as a <c>min</c>/<c>max</c> on an input in
/// <c>ShipCard.tsx</c> and once as a <c>Math.Clamp</c> in <see cref="InMemoryMatchService"/>, in two
/// languages, with nothing holding the pair together. A form whose max drifts above the clamp lets a
/// player type a number that is silently discarded on the way in; one that drifts below it refuses a
/// value the engine would have taken.
/// </para>
/// <para>
/// <c>src/ForceSignal.Web/src/lib/shipFormBounds.json</c> holds them once.
/// <c>shipFormBounds.test.tsx</c> reads them off the rendered inputs and this reads them off the
/// service, through <see cref="InMemoryMatchService.CreateShip"/> rather than by calling the private
/// clamps - the front door a caller uses, so the guard cannot pass over a clamp the create path does
/// not actually apply.
/// </para>
/// <para>
/// These are shape bounds, not rules numbers, and <c>docs/roadmap.md</c> places them outside the
/// content-policy render guard deliberately. This adds the half of that argument that was missing:
/// the pair cannot drift silently.
/// </para>
/// </remarks>
public sealed class ShipFormBoundsParityTests
{
    [Fact]
    public void TheServiceClampsToExactlyWhatTheFormTypesWithin()
    {
        var bounds = Bounds();

        // Reached-the-subject: the fixture really loaded and really still carries the fire control
        // entry, which is the one this file was written for.
        Assert.True(bounds.Count >= 7, $"only {bounds.Count} bounds loaded from the fixture");
        Assert.Contains(bounds, bound => bound.Field == "fireControlMax" && bound.Max == 6);

        var (service, fleetId, token) = NewFleet();

        // One ship per bound, each asking for far more than the bound allows. What comes back is
        // what the service is willing to hold, which is the number the form has to agree with.
        var ship = service.CreateShip(fleetId, new CreateShipRequest(
            token,
            "Overreach",
            "Cruiser",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: 9999,
            ArmorMax: 9999,
            StartX: 12,
            StartY: 24,
            ScreenRating: 0,
            IconKey: "cruiser",
            FireControlMax: 9999,
            PointDefenseSystems: 9999,
            FighterBays: 9999,
            DamageControlParties: 9999,
            PointsValue: 9999999)).Ships.Single();

        // Reached-the-subject again: the request was accepted rather than refused, and it is the
        // ship that was asked for. A refusal would leave every assertion below unreached.
        Assert.Equal("Overreach", ship.Name);

        var actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["hullMax"] = ship.HullMax,
            ["armorMax"] = ship.ArmorMax,
            ["fireControlMax"] = ship.FireControlMax,
            ["pointDefenseSystems"] = ship.PointDefenseSystems,
            ["fighterBays"] = ship.FighterBays,
            ["damageControlParties"] = ship.DamageControlParties,
            ["pointsValue"] = ship.PointsValue,
        };

        foreach (var bound in bounds)
        {
            Assert.True(actual.ContainsKey(bound.Field), $"{bound.Field} is in the fixture and not read back here");
            Assert.Equal(bound.Max, actual[bound.Field]);
        }
    }

    [Fact]
    public void TheServiceFloorsToExactlyWhatTheFormTypesWithin()
    {
        var bounds = Bounds();
        var (service, fleetId, token) = NewFleet();

        // The other end of each bound. Hull is the one that is not zero - a ship of no boxes is not
        // a ship - and the fixture says so, so a hull floor that quietly became zero fails here.
        var ship = service.CreateShip(fleetId, new CreateShipRequest(
            token,
            "Underreach",
            "Cruiser",
            ThrustRating: 0,
            InitialVelocity: 0,
            InitialCourse: 1,
            HullMax: -9999,
            ArmorMax: -9999,
            StartX: 12,
            StartY: 24,
            ScreenRating: 0,
            IconKey: "cruiser",
            FireControlMax: -9999,
            PointDefenseSystems: -9999,
            FighterBays: -9999,
            DamageControlParties: -9999,
            PointsValue: -9999999)).Ships.Single();

        Assert.Equal("Underreach", ship.Name);

        var actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["hullMax"] = ship.HullMax,
            ["armorMax"] = ship.ArmorMax,
            ["fireControlMax"] = ship.FireControlMax,
            ["pointDefenseSystems"] = ship.PointDefenseSystems,
            ["fighterBays"] = ship.FighterBays,
            ["damageControlParties"] = ship.DamageControlParties,
            ["pointsValue"] = ship.PointsValue,
        };

        foreach (var bound in bounds)
        {
            Assert.Equal(bound.Min, actual[bound.Field]);
        }
    }

    private static (InMemoryMatchService Service, Guid FleetId, string Token) NewFleet()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Admiral", "Table", Rules: TestRules.Invented));
        var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Home Watch", null)).Fleets.Single();
        return (service, fleet.Id, owner.ParticipantToken);
    }

    private sealed record Bound(string Field, int Min, int Max, string Why);

    private static List<Bound> Bounds()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "ForceSignal.Web", "src", "lib", "shipFormBounds.json");
        Assert.True(File.Exists(path), $"the shared ship-form bounds fixture is not at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("bounds").EnumerateArray().Select(item => new Bound(
            item.GetProperty("field").GetString() ?? string.Empty,
            item.GetProperty("min").GetInt32(),
            item.GetProperty("max").GetInt32(),
            item.GetProperty("why").GetString() ?? string.Empty))];
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
