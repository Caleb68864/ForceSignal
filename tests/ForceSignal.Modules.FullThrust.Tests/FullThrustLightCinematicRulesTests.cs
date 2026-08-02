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
    public void Resolve_SplitsAPlottedTurnHalfAtTheStartAndHalfAtTheMidPoint()
    {
        // The rulebook's worked example: a three-point turn to port at velocity 10 from course 3.
        // Pivot one point (half of three, rounded down) to course 2, run 5, pivot the remaining two
        // to course 12, run the last 5.
        var result = _rules.Resolve(
            new ShipMovementState(Velocity: 10, Course: 3),
            new MovementOrder(0, 3, TurnDirection.Port));

        Assert.Equal([2, 12], result.Segments!.Select(segment => segment.Course));
        Assert.Equal([5m, 5m], result.Segments!.Select(segment => segment.Distance));
        Assert.Equal(12, result.EndingCourse);
        Assert.Equal(10, result.EndingVelocity);
    }

    [Fact]
    public void Resolve_MakesASinglePointTurnEntirelyAtTheMidPoint()
    {
        // The other worked example: S1 at velocity 14 from course 8. Half of one rounds to none, so
        // the ship runs 7 on its original heading before it turns at all.
        var result = _rules.Resolve(
            new ShipMovementState(Velocity: 14, Course: 8),
            new MovementOrder(0, 1, TurnDirection.Starboard));

        Assert.Equal([8, 9], result.Segments!.Select(segment => segment.Course));
        Assert.Equal([7m, 7m], result.Segments!.Select(segment => segment.Distance));
        Assert.Equal(9, result.EndingCourse);
    }

    [Fact]
    public void Resolve_MovesTheWholeOfTheNewVelocity()
    {
        var result = _rules.Resolve(
            new ShipMovementState(Velocity: 6, Course: 12),
            new MovementOrder(4, 2, TurnDirection.Starboard));

        Assert.Equal(10, result.EndingVelocity);
        Assert.Equal(10m, result.Segments!.Sum(segment => segment.Distance));
        Assert.Equal([1, 2], result.Segments!.Select(segment => segment.Course));
    }

    [Fact]
    public void Resolve_WithNoTurnRunsStraight()
    {
        var result = _rules.Resolve(
            new ShipMovementState(Velocity: 8, Course: 5),
            new MovementOrder(0, 0, TurnDirection.None));

        var only = Assert.Single(result.Segments!);
        Assert.Equal(5, only.Course);
        Assert.Equal(8m, only.Distance);
    }

    [Fact]
    public void Resolve_GivesEachTurnInASequenceAnEqualShareOfTheMove()
    {
        // Leg one: pivot 1 to course 1, run 3, pivot 1 to course 2, run 3.
        // Leg two: pivot 1 back to course 1, run 3, pivot 1 back to course 12, run 3.
        var result = _rules.Resolve(
            new ShipMovementState(Velocity: 12, Course: 12),
            new MovementOrder(0, 0, TurnDirection.None,
            [
                new TurnManeuver(TurnDirection.Starboard, 2),
                new TurnManeuver(TurnDirection.Port, 2),
            ]));

        Assert.Equal([1, 2, 1, 12], result.Segments!.Select(segment => segment.Course));
        Assert.Equal(12m, result.Segments!.Sum(segment => segment.Distance));
        Assert.Equal(12, result.EndingCourse);
    }

    [Fact]
    public void Resolve_MergesLegsThatShareACourse()
    {
        // Single-point turns pivot nothing at the start of their leg, so the trail should not carry
        // two legs running on the same heading.
        var result = _rules.Resolve(
            new ShipMovementState(Velocity: 12, Course: 12),
            new MovementOrder(0, 0, TurnDirection.None,
            [
                new TurnManeuver(TurnDirection.Starboard, 1),
                new TurnManeuver(TurnDirection.Starboard, 1),
            ]));

        Assert.Equal([12, 1, 2], result.Segments!.Select(segment => segment.Course));
        Assert.Equal([3m, 6m, 3m], result.Segments!.Select(segment => segment.Distance));
    }

    [Fact]
    public void Resolve_RotatesAStationaryShipWithoutMovingIt()
    {
        var result = _rules.Resolve(
            new ShipMovementState(Velocity: 0, Course: 12),
            new MovementOrder(0, 5, TurnDirection.Starboard));

        Assert.Equal(5, result.EndingCourse);
        Assert.Equal(0, result.EndingVelocity);
        Assert.Equal(0m, result.Segments!.Sum(segment => segment.Distance));
    }

    [Fact]
    public void Validate_LetsAShipAtRestRotateToAnyHeadingForFree()
    {
        // Six points is a full about-face, well past the half-thrust cap a moving ship lives under.
        var result = _rules.Validate(
            new ShipMovementState(Velocity: 0, Course: 12),
            thrustRating: 2,
            new MovementOrder(0, 6, TurnDirection.Port));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_LetsAThrustlessHullRotateAtRest()
    {
        // A station has no drives at all, but it can still come about while stopped.
        var result = _rules.Validate(
            new ShipMovementState(Velocity: 0, Course: 3),
            thrustRating: 0,
            new MovementOrder(0, 3, TurnDirection.Starboard));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_StillChargesThrustWhenAStationaryShipAlsoGetsUnderWay()
    {
        // Rotation is free only when the ship makes no other move.
        var result = _rules.Validate(
            new ShipMovementState(Velocity: 0, Course: 12),
            thrustRating: 2,
            new MovementOrder(1, 6, TurnDirection.Port));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("thrust rating", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RefusesARotationBeyondAFullCircle()
    {
        var result = _rules.Validate(
            new ShipMovementState(Velocity: 0, Course: 12),
            thrustRating: 4,
            new MovementOrder(0, 13, TurnDirection.Port));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("full circle", StringComparison.OrdinalIgnoreCase));
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
