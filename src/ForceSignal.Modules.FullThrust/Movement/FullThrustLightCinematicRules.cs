using ForceSignal.Domain.Rules;

namespace ForceSignal.Modules.FullThrust.Movement;

/// <summary>Lightweight cinematic movement profile used by ForceSignal's Full Thrust-compatible workflow.</summary>
public sealed class FullThrustLightCinematicRules : IOrderNormalizer, IOrderValidator, IMovementResolver
{
    /// <summary>Stable key used to identify this movement rules profile in snapshots.</summary>
    public const string ProfileKey = "full-thrust-light-cinematic";

    /// <summary>
    /// Largest velocity change an order may name. No ship has anything like this much thrust - the
    /// bound exists because the arithmetic below has to be safe on any number that arrives.
    /// Math.Abs(int.MinValue) throws rather than returning a positive, and adding an unbounded
    /// delta to a velocity wraps silently into a negative one, so both are refused by name here
    /// rather than surfacing as a crash or a ship that accelerated into nonsense.
    /// </summary>
    public const int MaxVelocityDelta = 100;

    /// <summary>
    /// Most turn maneuvers one order may hold. A legal order can only contain as many as half the
    /// thrust rating, so this is far above any real plot; it stops a caller sending a list long
    /// enough to be expensive to check and to hash.
    /// </summary>
    public const int MaxTurnManeuvers = 32;

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

        // Checked before anything else touches the numbers: the thrust arithmetic below is not safe
        // on an unbounded delta, and there is nothing useful to say about the rest of an order
        // whose velocity change is not a number a ship could plot.
        if (Math.Abs((long)order.VelocityDelta) > MaxVelocityDelta)
        {
            return new OrderValidationResult(false, [$"Velocity change must be within {MaxVelocityDelta} of a standstill."]);
        }

        if (order.TurnManeuvers is { Count: > MaxTurnManeuvers })
        {
            return new OrderValidationResult(false, [$"An order cannot hold more than {MaxTurnManeuvers} turn maneuvers."]);
        }

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
        // A ship at rest may be rotated on the spot to any heading, spending no thrust and making
        // no other move. It is the one turn that costs nothing and ignores the half-thrust cap.
        var rotatingAtRest = shipState.Velocity == 0 && order.VelocityDelta == 0 && totalTurnSteps > 0;

        if (!rotatingAtRest && thrustSpent > thrustRating)
        {
            errors.Add("Velocity change and turns cannot spend more than thrust rating.");
        }

        if (rotatingAtRest && totalTurnSteps > 12)
        {
            errors.Add("A rotation at rest cannot exceed a full circle.");
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

        if (!rotatingAtRest && totalTurnSteps > turnCap)
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
    /// <remarks>
    /// A ship applies sideways thrust throughout its move, so a plotted turn is made half at the
    /// start and half at the mid-point: pivot half the turn rounded down, run half the distance,
    /// pivot the rest, run the rest. Rounding down is why a single-point turn happens entirely at
    /// the mid-point. A plotted sequence of turns gives each one an equal share of the move and
    /// splits it the same way.
    /// </remarks>
    public MovementResult Resolve(ShipMovementState shipState, MovementOrder order)
    {
        var endingVelocity = shipState.Velocity + order.VelocityDelta;
        var maneuvers = NormalizeManeuvers(order);
        var course = shipState.Course;
        var segments = new List<MovementSegment>();

        if (maneuvers.Count == 0)
        {
            segments.Add(new MovementSegment(course, endingVelocity));
            return new MovementResult(shipState.Velocity, shipState.Course, endingVelocity, course, segments);
        }

        var halfLeg = endingVelocity / (decimal)(maneuvers.Count * 2);
        foreach (var maneuver in maneuvers)
        {
            var sign = maneuver.Direction == TurnDirection.Port ? -1 : 1;
            var openingPivot = maneuver.Steps / 2;
            course = WrapCourse(course + (sign * openingPivot));
            AddSegment(segments, course, halfLeg);
            course = WrapCourse(course + (sign * (maneuver.Steps - openingPivot)));
            AddSegment(segments, course, halfLeg);
        }

        return new MovementResult(
            shipState.Velocity,
            shipState.Course,
            endingVelocity,
            course,
            segments);
    }

    /// <summary>
    /// Appends a leg, extending the previous one when the course has not actually changed. A
    /// single-point turn pivots nothing at the start, so without this the trail would carry a
    /// needless kink-free split.
    /// </summary>
    private static void AddSegment(List<MovementSegment> segments, int course, decimal distance)
    {
        if (segments.Count > 0 && segments[^1].Course == course)
        {
            segments[^1] = segments[^1] with { Distance = segments[^1].Distance + distance };
            return;
        }

        segments.Add(new MovementSegment(course, distance));
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
            // Normalizing happens on the reveal path before the hash has been checked, so the list
            // here has not necessarily been through Validate. Reading past the ceiling would be
            // work done on behalf of a caller who has not yet proved they committed to anything.
            foreach (var maneuver in order.TurnManeuvers.Take(MaxTurnManeuvers))
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
