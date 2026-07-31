using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Movement;

namespace ForceSignal.Modules.FullThrust.Tests;

public sealed class FullThrustLightCinematicRulesTests
{
    private readonly FullThrustLightCinematicRules _rules = new();

    [Theory]
    [InlineData(12, TurnDirection.Starboard, 1)]
    [InlineData(1, TurnDirection.Port, 12)]
    [InlineData(11, TurnDirection.Starboard, 12)]
    public void Resolve_WrapsCourseAcrossTwelvePointClock(int startingCourse, TurnDirection direction, int expectedCourse)
    {
        var result = _rules.Resolve(new ShipMovementState(8, startingCourse), new MovementOrder(0, 1, direction));

        Assert.Equal(expectedCourse, result.EndingCourse);
    }

    [Fact]
    public void Validate_RejectsVelocityChangeBeyondThrustRating()
    {
        var result = _rules.Validate(new ShipMovementState(8, 3), 2, new MovementOrder(3, 0, TurnDirection.None));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("thrust", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsTurnsBeyondLightCinematicTurnCap()
    {
        var result = _rules.Validate(new ShipMovementState(8, 3), 3, new MovementOrder(0, 3, TurnDirection.Port));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("half thrust", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_AppliesMultipleTurnManeuversInDeclaredOrder()
    {
        var order = new MovementOrder(
            0,
            3,
            TurnDirection.None,
            [
                new TurnManeuver(TurnDirection.Starboard, 1),
                new TurnManeuver(TurnDirection.Port, 1),
                new TurnManeuver(TurnDirection.Starboard, 1)
            ]);

        var result = _rules.Resolve(new ShipMovementState(8, 12), order);

        Assert.Equal(1, result.EndingCourse);
        Assert.Equal([12, 1, 12, 1], result.Segments!.Select(segment => segment.Course));
    }

    [Fact]
    public void Validate_AppliesTurnCapToTotalManeuverSteps()
    {
        var order = new MovementOrder(
            0,
            4,
            TurnDirection.None,
            [
                new TurnManeuver(TurnDirection.Port, 1),
                new TurnManeuver(TurnDirection.Starboard, 1),
                new TurnManeuver(TurnDirection.Port, 2)
            ]);

        var result = _rules.Validate(new ShipMovementState(8, 3), 6, order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("half thrust", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Normalize_PreservesOrderedTurnManeuvers()
    {
        var first = _rules.Normalize(new MovementOrder(
            1,
            0,
            TurnDirection.Port,
            [new TurnManeuver(TurnDirection.Port, 1), new TurnManeuver(TurnDirection.Starboard, 1)]));
        var second = _rules.Normalize(new MovementOrder(
            1,
            2,
            TurnDirection.None,
            [new TurnManeuver(TurnDirection.Port, 1), new TurnManeuver(TurnDirection.Starboard, 1)]));

        Assert.Equal(first, second);
        Assert.Contains("turnManeuvers", first);
    }

    [Fact]
    public void Normalize_IsDeterministicForEquivalentNoTurnOrders()
    {
        var first = _rules.Normalize(new MovementOrder(1, 0, TurnDirection.None));
        var second = _rules.Normalize(new MovementOrder(1, 0, TurnDirection.Port));

        Assert.Equal(first, second);
    }

    [Fact]
    public void CommitmentService_RejectsTamperedReveal()
    {
        var commitments = new Sha256CommitmentService();
        var salt = commitments.CreateSalt();
        var original = _rules.Normalize(new MovementOrder(1, 0, TurnDirection.None));
        var changed = _rules.Normalize(new MovementOrder(2, 0, TurnDirection.None));
        var hash = commitments.CreateHash(original, salt);

        Assert.False(commitments.Verify(hash, changed, salt));
    }
}
