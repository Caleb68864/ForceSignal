using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Movement;

namespace ForceSignal.Application.Matches;

public sealed partial class InMemoryMatchService
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Reads and answers; nothing here writes. No version bump, no log line, no commitment - so a
    /// player dragging a course around does not broadcast a snapshot change to the table on every
    /// frame, and does not leave a trail of half-formed plots in the after-action log.
    /// </para>
    /// <para>
    /// An illegal draft is described rather than refused, which is the difference between this and
    /// <see cref="CommitOrder"/>. Locking an order is a commitment and has to hold the line; a
    /// preview is a question asked while the player is still deciding, and they pass through
    /// illegal drafts on the way to a legal one. The plot is walked either way so the map can keep
    /// drawing.
    /// </para>
    /// </remarks>
    public OrderPreviewDto PreviewOrder(Guid matchId, PreviewOrderRequest request)
    {
        lock (_gate)
        {
            var match = FindMatch(matchId);
            var participant = FindParticipant(match, request.ParticipantToken);
            var ship = FindOwnedShip(match, participant.Id, request.ShipId);

            RequireOrder(request.Order);
            var thrust = UsableThrust(ship);
            var shipState = new ShipMovementState(ship.CurrentVelocity, ship.CurrentCourse);
            var order = request.Order;

            var errors = new List<string>();
            if (IsDestroyed(ship))
            {
                errors.Add($"{ship.Name} is destroyed and cannot receive movement orders.");
            }

            if (IsFighterGroupShip(ship))
            {
                errors.Add($"{ship.Name} is a fighter group: fly it straight to where it is going rather than plotting a course.");
            }

            var validation = _rules.Validate(shipState, thrust, order);
            errors.AddRange(validation.Errors);

            // Resolve regardless. A draft that spends thrust it does not have still has a shape,
            // and showing the player that shape is how they see what to trim.
            var result = _rules.Resolve(shipState, order);
            var (path, runsOffTable) = WalkPath(ship.PositionX, ship.PositionY, result, match.TableWidth, match.TableDepth);

            var turnSteps = FullThrustLightCinematicRules.ManeuversFor(order).Sum(m => m.Steps);

            return new OrderPreviewDto(
                ship.Id,
                errors.Count == 0,
                errors,
                thrust,
                Math.Abs(order.VelocityDelta) + turnSteps,
                FullThrustLightCinematicRules.MaxTurnSteps(shipState, thrust, order.VelocityDelta),
                result.StartingVelocity,
                result.StartingCourse,
                result.EndingVelocity,
                result.EndingCourse,
                result.Segments ?? [],
                [.. path.Select(point => new TablePointDto(point.X, point.Y))],
                runsOffTable);
        }
    }
}
