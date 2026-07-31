using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Movement;

/// <summary>Lightweight cinematic movement profile used by ForceSignal's Full Thrust-compatible workflow.</summary>
public sealed class FullThrustLightCinematicRules : IOrderNormalizer, IOrderValidator, IMovementResolver
{
    /// <summary>Stable key used to identify this movement rules profile in snapshots.</summary>
    public const string ProfileKey = "full-thrust-light-cinematic";

    /// <inheritdoc />
    public string Normalize(MovementOrder order)
    {
        var maneuvers = NormalizeManeuvers(order);
        var turnSteps = maneuvers.Sum(m => m.Steps);
        var turnDirection = turnSteps == 0
            ? TurnDirection.None
            : maneuvers.Select(m => m.Direction).Distinct().Count() == 1
                ? maneuvers[0].Direction
                : TurnDirection.None;

        if (maneuvers.Count == 0)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                order.VelocityDelta,
                TurnSteps = 0,
                TurnDirection = TurnDirection.None
            }, StableJson.Options);
        }

        var normalized = new
        {
            order.VelocityDelta,
            TurnSteps = turnSteps,
            TurnDirection = turnDirection,
            TurnManeuvers = maneuvers
        };

        return System.Text.Json.JsonSerializer.Serialize(normalized, StableJson.Options);
    }

    /// <inheritdoc />
    public OrderValidationResult Validate(ShipMovementState shipState, int thrustRating, MovementOrder order)
    {
        var errors = new List<string>();

        if (shipState.Course is < 1 or > 12)
        {
            errors.Add("Starting course must be from 1 through 12.");
        }

        if (shipState.Velocity < 0)
        {
            errors.Add("Starting velocity cannot be negative.");
        }

        if (thrustRating < 0)
        {
            errors.Add("Thrust rating cannot be negative.");
        }

        var maneuverErrors = new List<string>();
        var maneuvers = NormalizeManeuvers(order, maneuverErrors);
        errors.AddRange(maneuverErrors);
        var totalTurnSteps = maneuvers.Sum(m => m.Steps);
        var usesTurnManeuvers = order.TurnManeuvers is { Count: > 0 };
        var thrustSpent = Math.Abs(order.VelocityDelta) + totalTurnSteps;
        var turnCap = (int)Math.Ceiling(thrustRating / 2.0);

        if (thrustSpent > thrustRating)
        {
            errors.Add("Velocity change and turns cannot spend more than thrust rating.");
        }

        if (shipState.Velocity + order.VelocityDelta < 0)
        {
            errors.Add("Ending velocity cannot be negative.");
        }

        if (!usesTurnManeuvers && order.TurnSteps < 0)
        {
            errors.Add("Turn steps cannot be negative.");
        }

        if (!usesTurnManeuvers && order.TurnSteps > 0 && order.TurnDirection == TurnDirection.None)
        {
            errors.Add("A turn direction is required when turn steps are entered.");
        }

        if (totalTurnSteps > turnCap)
        {
            errors.Add("Turn points cannot exceed half thrust rounded up in the light cinematic profile.");
        }

        if (!usesTurnManeuvers && order.TurnSteps == 0 && order.TurnDirection != TurnDirection.None)
        {
            errors.Add("Turn direction must be none when no turn steps are entered.");
        }

        return errors.Count == 0 ? OrderValidationResult.Success : new OrderValidationResult(false, errors);
    }

    /// <inheritdoc />
    public MovementResult Resolve(ShipMovementState shipState, MovementOrder order)
    {
        var endingVelocity = shipState.Velocity + order.VelocityDelta;
        var maneuvers = NormalizeManeuvers(order);
        var course = shipState.Course;
        var segments = new List<MovementSegment>();
        var segmentDistance = maneuvers.Count == 0 ? endingVelocity : endingVelocity / (decimal)(maneuvers.Count + 1);

        segments.Add(new MovementSegment(course, segmentDistance));

        foreach (var maneuver in maneuvers)
        {
            var signedTurn = maneuver.Direction == TurnDirection.Port ? -maneuver.Steps : maneuver.Steps;
            course = WrapCourse(course + signedTurn);
            segments.Add(new MovementSegment(course, segmentDistance));
        }

        return new MovementResult(
            shipState.Velocity,
            shipState.Course,
            endingVelocity,
            course,
            segments);
    }

    /// <summary>Wraps any course number onto the twelve-point course clock.</summary>
    public static int WrapCourse(int course)
    {
        var zeroBased = (course - 1) % 12;
        if (zeroBased < 0)
        {
            zeroBased += 12;
        }

        return zeroBased + 1;
    }

    private static IReadOnlyList<TurnManeuver> NormalizeManeuvers(MovementOrder order, List<string>? errors = null)
    {
        if (order.TurnManeuvers is { Count: > 0 })
        {
            var maneuvers = new List<TurnManeuver>();
            foreach (var maneuver in order.TurnManeuvers)
            {
                if (maneuver.Steps <= 0)
                {
                    errors?.Add("Turn maneuver steps must be greater than zero.");
                    continue;
                }

                if (maneuver.Direction == TurnDirection.None)
                {
                    errors?.Add("Each turn maneuver must declare port or starboard.");
                    continue;
                }

                maneuvers.Add(new TurnManeuver(maneuver.Direction, Math.Abs(maneuver.Steps)));
            }

            return maneuvers;
        }

        if (order.TurnSteps <= 0 || order.TurnDirection == TurnDirection.None)
        {
            return Array.Empty<TurnManeuver>();
        }

        return [new TurnManeuver(order.TurnDirection, Math.Abs(order.TurnSteps))];
    }
}
