using System.Text.Json;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Movement;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// The resolver's turn ceiling, held against the helm's copy of it.
/// </summary>
/// <remarks>
/// <para>
/// The client cannot ask the server for this number in time to draw a compass, so it keeps its own
/// <c>maxLegalTurn</c>. A second implementation of a rule is exactly what this repository has paid
/// for before, so it is allowed to exist only on the condition that something fails when the two
/// disagree. <c>src/ForceSignal.Web/src/lib/turnLimitCases.json</c> is that something: this test
/// runs every case against the resolver and <c>movement.test.ts</c> runs the same cases against the
/// client, so a change to either side turns one of the two suites red.
/// </para>
/// <para>
/// What it was written for: the resolver carves out a ship at rest - velocity zero and no velocity
/// change rotates to any heading for free, ignoring the half-thrust cap - by name in
/// <see cref="FullThrustLightCinematicRules.MaxTurnSteps"/> and again in its <c>Validate</c>. The
/// client had no copy of that carve-out, so the compass drew its limits at half thrust around a
/// stationary ship and the map answered "has no turn points left" to a rotation the server would
/// have accepted. With the new-ship form now opening at velocity zero, that was every ship on its
/// first turn.
/// </para>
/// </remarks>
public sealed class TurnLimitParityTests
{
    [Fact]
    public void TheResolverAnswersEveryCaseTheHelmIsHeldTo()
    {
        var cases = Cases();

        // Reached-the-subject. A fixture that failed to load, or that parsed to an empty list,
        // would make this test pass over nothing at all - which is the shape of failure this
        // repository has recorded more than any other. The carve-out case must be present by name
        // too, so the file cannot be quietly reduced to the cases both sides already agreed on.
        Assert.True(cases.Count >= 10, $"only {cases.Count} turn-limit cases loaded from the fixture");
        Assert.Contains(cases, item => item.CurrentVelocity == 0 && item.VelocityDelta == 0 && item.MaxTurnSteps == 12);
        Assert.Contains(cases, item => item.CurrentVelocity > 0);

        foreach (var item in cases)
        {
            var actual = FullThrustLightCinematicRules.MaxTurnSteps(
                new ShipMovementState(item.CurrentVelocity, 1),
                item.ThrustRating,
                item.VelocityDelta);

            Assert.Equal(item.MaxTurnSteps, actual);
        }
    }

    [Fact]
    public void AnOrderTheCeilingAllowsIsAnOrderValidateAccepts()
    {
        // The ceiling is only worth agreeing on if it describes what the resolver will actually
        // accept. Without this, both sides could agree on a number that Validate then refuses -
        // which is the half-wired shape one level up, two implementations agreeing about a rule
        // that a third disagrees with.
        var rules = new FullThrustLightCinematicRules();

        foreach (var item in Cases().Where(item => item.MaxTurnSteps > 0))
        {
            var order = new MovementOrder(
                item.VelocityDelta,
                item.MaxTurnSteps,
                TurnDirection.Starboard,
                [new TurnManeuver(TurnDirection.Starboard, item.MaxTurnSteps)]);

            var result = rules.Validate(new ShipMovementState(item.CurrentVelocity, 1), item.ThrustRating, order);

            Assert.True(result.IsValid, $"{item.Name}: the ceiling allows {item.MaxTurnSteps} steps and Validate refused them: {string.Join("; ", result.Errors)}");
        }
    }

    private sealed record TurnCase(string Name, int CurrentVelocity, int ThrustRating, int VelocityDelta, int MaxTurnSteps);

    private static List<TurnCase> Cases()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "ForceSignal.Web", "src", "lib", "turnLimitCases.json");
        Assert.True(File.Exists(path), $"the shared turn-limit fixture is not at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(item => new TurnCase(
            item.GetProperty("name").GetString() ?? string.Empty,
            item.GetProperty("currentVelocity").GetInt32(),
            item.GetProperty("thrustRating").GetInt32(),
            item.GetProperty("velocityDelta").GetInt32(),
            item.GetProperty("maxTurnSteps").GetInt32()))];
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
