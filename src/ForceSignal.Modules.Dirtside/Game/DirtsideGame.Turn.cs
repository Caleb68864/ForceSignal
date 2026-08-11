using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

public sealed partial record DirtsideGame
{
    /// <summary>Opens the turn.</summary>
    /// <returns>The game with the turn under way, or why it could not start.</returns>
    public GameOutcome<DirtsideGame> BeginTurn() =>
        Apply(GroundCombatSequence.CanBeginTurn(Session), () => GroundCombatSequence.BeginTurn(Session));

    /// <summary>Takes or declines the first activation of the turn.</summary>
    /// <param name="chooser">The side that won the choice.</param>
    /// <param name="takeIt">True to go first, false to make the other side go.</param>
    /// <returns>The game with the choice made, or why it could not be.</returns>
    public GameOutcome<DirtsideGame> ChooseFirstActivator(SideId chooser, bool takeIt) =>
        Apply(
            GroundCombatSequence.CanChooseFirstActivator(Session, chooser),
            () => GroundCombatSequence.ChooseFirstActivator(Session, chooser, takeIt));

    /// <summary>Turns a platoon's marker over and starts its activation.</summary>
    /// <param name="side">The side activating.</param>
    /// <param name="unit">The platoon.</param>
    /// <returns>The game with the activation open, or why it could not be.</returns>
    /// <remarks>
    /// A platoon with nothing left on the table is refused here rather than at the first step. The
    /// shared layer has no view on it - a wiped-out platoon is still a face-up marker as far as the
    /// sequence is concerned - and letting it activate would open a frame that could never be closed,
    /// since a frame is complete when every element has chosen and there are none.
    /// </remarks>
    public GameOutcome<DirtsideGame> BeginActivation(SideId side, UnitId unit)
    {
        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<DirtsideGame>($"There is no platoon called '{unit}' on the table.");
        }

        if (Status(unit).IsWipedOut)
        {
            return GameOutcome.Refused<DirtsideGame>($"{Unit(unit).Name} has nothing left to activate.");
        }

        return Apply(
            GroundCombatSequence.CanBeginActivation(Session, side, unit),
            () => GroundCombatSequence.BeginActivation(Session, side, unit));
    }

    /// <summary>Takes one step in the open activation.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The game with the step taken, or why it was refused.</returns>
    public GameOutcome<DirtsideGame> TakeStep(ActivationStep step)
    {
        var policy = new DirtsideActivationPolicy(this);
        return Apply(
            GroundCombatSequence.CanTakeStep(Session, step, policy),
            () => GroundCombatSequence.TakeStep(Session, step, policy));
    }

    /// <summary>Moves one element.</summary>
    /// <param name="element">The element moving.</param>
    /// <param name="overHalfItsMovement">True when the move covers more than half its movement.</param>
    /// <returns>The game with the move taken, or why it was refused.</returns>
    /// <remarks>
    /// How far it went is recorded because a shot from a vehicle that has moved over half its
    /// movement is a worse shot, and the element is the only thing that knows. The distance itself is
    /// the player's - measured with a tape at the table - so what is asked for is the answer, not the
    /// inches.
    /// </remarks>
    public GameOutcome<DirtsideGame> MoveElement(ElementId element, bool overHalfItsMovement)
    {
        var moved = TakeStep(DirtsideSteps.Move(element));
        if (!moved.IsAllowed)
        {
            return moved;
        }

        var unit = Session.CurrentFrame!.Unit;
        return GameOutcome.Allowed(
            moved.Value!
                .WithStatus(unit, status => status.WithElement(
                    element, current => current with { MovedOverHalf = overHalfItsMovement }))
                .WithLog($"{Describe(unit, element)} moved{(overHalfItsMovement ? ", over half its movement" : string.Empty)}."));
    }

    /// <summary>Declares that an element is sitting this activation out.</summary>
    /// <param name="element">The element standing down.</param>
    /// <returns>The game with it recorded, or why it was refused.</returns>
    /// <remarks>
    /// A real step rather than the absence of one. An element that sits out has given up its go for
    /// the whole turn and may not act later, so saying so is what lets the activation close honestly.
    /// </remarks>
    public GameOutcome<DirtsideGame> StandDown(ElementId element)
    {
        var unit = Session.CurrentFrame?.Unit;
        var stood = TakeStep(DirtsideSteps.StandDown(element));
        return stood.IsAllowed && unit is { } platoon
            ? GameOutcome.Allowed(stood.Value!.WithLog($"{Describe(platoon, element)} stood down for the turn."))
            : stood;
    }

    /// <summary>Switches an element's area-defence sensors on or off.</summary>
    /// <param name="element">The element.</param>
    /// <param name="live">True to switch them on.</param>
    /// <returns>The game with the sensors set, or why it was refused.</returns>
    /// <remarks>
    /// This spends the element's one combat action, which looks generous until you notice what it
    /// buys: live sensors intercept on anybody's activation for the rest of the turn, including one
    /// where the element has long since gone. The combat action is the price of a standing reaction.
    /// </remarks>
    public GameOutcome<DirtsideGame> SetAreaDefenceSensors(ElementId element, bool live)
    {
        var unit = Session.CurrentFrame?.Unit;
        var toggled = TakeStep(DirtsideSteps.Act(DirtsideAction.ToggleAreaDefenceSensors, element));
        if (!toggled.IsAllowed || unit is not { } platoon)
        {
            return toggled;
        }

        return GameOutcome.Allowed(
            toggled.Value!
                .WithStatus(platoon, status => status.WithElement(
                    element, current => current with { AreaDefenceSensorsLive = live }))
                .WithLog($"{Describe(platoon, element)} switched its area-defence sensors {(live ? "on" : "off")}."));
    }

    /// <summary>Closes the open activation.</summary>
    /// <returns>The game with the activation closed, or why it could not be.</returns>
    /// <remarks>
    /// Refused while any element still on the table has not said what it is doing, and the refusal
    /// names them - a player told only that the activation is incomplete has to guess who everybody
    /// is waiting on.
    /// </remarks>
    public GameOutcome<DirtsideGame> EndActivation()
    {
        var policy = new DirtsideActivationPolicy(this);
        if (Session.CurrentFrame is { Kind: FrameKind.Activation } frame
            && policy.StillToChoose(frame) is { Count: > 0 } waiting)
        {
            return GameOutcome.Refused<DirtsideGame>(
                $"{Unit(frame.Unit).Name} cannot finish while {Join(frame.Unit, waiting)} "
                + $"{(waiting.Count == 1 ? "has" : "have")} not said what to do. An element that sits out "
                + "gives up its go for the turn, so it has to say so.");
        }

        return Apply(
            GroundCombatSequence.CanEndFrame(Session, policy),
            () => GroundCombatSequence.EndFrame(Session, policy));
    }

    /// <summary>Declines to activate anything.</summary>
    /// <param name="side">The side passing.</param>
    /// <returns>The game with the pass recorded, or why it could not be.</returns>
    public GameOutcome<DirtsideGame> Pass(SideId side) =>
        Apply(GroundCombatSequence.CanPass(Session, side), () => GroundCombatSequence.Pass(Session, side));

    /// <summary>Closes the turn.</summary>
    /// <returns>The game with the turn closed, or why it could not be.</returns>
    /// <remarks>
    /// Everything a turn's worth of markers records is cleared here: an element that moved over half
    /// its movement has not done so next turn, and a passed reaction test does not carry over.
    /// </remarks>
    public GameOutcome<DirtsideGame> EndTurn()
    {
        var ended = Apply(GroundCombatSequence.CanEndTurn(Session), () => GroundCombatSequence.EndTurn(Session));
        if (!ended.IsAllowed)
        {
            return ended;
        }

        var cleared = ended.Value!;
        foreach (var unit in cleared.Units.Keys)
        {
            cleared = cleared.WithStatus(unit, status => status with
            {
                ReactionTestCleared = false,
                Elements = status.Elements.SetItems(
                    status.Elements.Select(pair =>
                        KeyValuePair.Create(pair.Key, pair.Value with { MovedOverHalf = false }))),
            });
        }

        return GameOutcome.Allowed(cleared);
    }

    /// <summary>An element in words, for the log.</summary>
    private string Describe(UnitId unit, ElementId element) =>
        Units.TryGetValue(unit, out var platoon) && platoon.Element(element) is { } part
            ? $"{platoon.Name}'s {part.Name}"
            : $"{unit}'s {element}";

    /// <summary>
    /// A list of elements in words, by the names on the table rather than by id.
    /// </summary>
    /// <remarks>
    /// Ids are whatever the client generated - in the web client, eight random characters - so a
    /// refusal that listed them told a player to go and find "ze09ww91". The refusal exists to say
    /// who everybody is waiting on, and it can only do that in the names on the models.
    /// </remarks>
    private string Join(UnitId unit, IReadOnlyList<ElementId> elements) =>
        string.Join(", ", elements.Select(element =>
            Units.TryGetValue(unit, out var platoon) && platoon.Element(element) is { } part
                ? part.Name
                : element.ToString()));

    /// <summary>
    /// Runs a shared-layer transition behind its own check, so a refusal is an answer rather than an
    /// exception.
    /// </summary>
    private GameOutcome<DirtsideGame> Apply(SequenceCheck check, Func<GroundCombatSession> transition) =>
        check.IsAllowed
            ? GameOutcome.Allowed(this with { Session = transition() })
            : GameOutcome.Refused<DirtsideGame>(check.Reason ?? "That is not legal now.");
}
