using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Dice;
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
    /// <para>
    /// How far it went is recorded because a shot from a vehicle that has moved over half its
    /// movement is a worse shot, and the element is the only thing that knows. The distance itself is
    /// the player's - measured with a tape at the table - so what is asked for is the answer, not the
    /// inches.
    /// </para>
    /// <para>
    /// An element that fired first and did not declare a move over half may not now make one: the
    /// shot was resolved unpenalised on that word. The other direction is left alone - an element
    /// that declared the move and then goes short has only cost itself.
    /// </para>
    /// </remarks>
    public GameOutcome<DirtsideGame> MoveElement(ElementId element, bool overHalfItsMovement)
    {
        if (WhyMoveIsRefused(element, overHalfItsMovement) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var moved = TakeStep(DirtsideSteps.Move(element));
        if (!moved.IsAllowed)
        {
            return moved;
        }

        var unit = Session.CurrentFrame!.Unit;
        return GameOutcome.Allowed(
            moved.Value!
                .WithStatus(unit, status => status.WithElement(
                    element, current => current with { MovedOverHalf = current.MovedOverHalf || overHalfItsMovement }))
                .WithLog($"{Describe(unit, element)} moved{(overHalfItsMovement ? ", over half its movement" : string.Empty)}."));
    }

    /// <summary>Whether an element could move now, and why not.</summary>
    /// <param name="element">The element.</param>
    /// <param name="overHalfItsMovement">True when the move would cover more than half its movement.</param>
    /// <returns>The refusal in words, or null when the move may be made.</returns>
    /// <remarks>
    /// The game's own reasons come first - an immobilised vehicle, a shot already taken on the
    /// promise of a short move - and the sequence layer's after them, in its words.
    /// </remarks>
    public string? WhyMoveIsRefused(ElementId element, bool overHalfItsMovement)
    {
        if (Session.CurrentFrame is { Kind: FrameKind.Activation } frame && Unit(frame.Unit).Element(element) is { } definition)
        {
            var status = Status(frame.Unit).Element(element);
            if (status.IsImmobilised)
            {
                return $"{definition.Name} is immobilised and will never move again, though it may still fire.";
            }

            if (overHalfItsMovement && !status.MovedOverHalf && HasFired(frame, element))
            {
                return $"{definition.Name} fired without declaring a move over half its movement, and that shot was "
                    + "resolved on its word. It may still move, but not that far.";
            }
        }

        var check = GroundCombatSequence.CanTakeStep(Session, DirtsideSteps.Move(element), new DirtsideActivationPolicy(this));
        return check.IsAllowed ? null : check.Reason;
    }

    /// <summary>True when this element has fired a weapon in the open frame.</summary>
    private static bool HasFired(ActivationFrame frame, ElementId element) =>
        !frame.Steps.IsDefaultOrEmpty
        && frame.Steps.Any(step => step.Subject == element && DirtsideSteps.ActionOf(step) == DirtsideAction.DirectFire);

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

    /// <summary>
    /// Answers an open area-defence interception window with one element's guns.
    /// </summary>
    /// <param name="unit">The platoon answering.</param>
    /// <param name="element">The element whose sensors are doing the intercepting.</param>
    /// <returns>The game with the interception under way, or why it was refused.</returns>
    /// <remarks>
    /// <para>
    /// This is what the combat action spent on <see cref="SetAreaDefenceSensors"/> buys, and until
    /// now nothing spent it on anything: the flag was set, echoed in the snapshot, and read by no
    /// rule at all, so an element whose sensors had never been switched on could intercept exactly
    /// as freely as one that had paid for it. A player who spends an action on a standing capability
    /// has to be able to find out that they have it and that somebody else does not.
    /// </para>
    /// <para>
    /// The interception itself costs nothing, which is why the sensor check is the whole of the
    /// gate: an element that has already used its activation may still answer, because live sensors
    /// are a capability bought earlier rather than a go being traded away now.
    /// </para>
    /// </remarks>
    public GameOutcome<DirtsideGame> InterceptWithAreaDefence(UnitId unit, ElementId element)
    {
        if (WhyInterceptionIsRefused(unit, element) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        return Apply(
            GroundCombatSequence.CanDeclareReaction(Session, unit, DirtsideActivationPolicy.AreaDefenceCost),
            () => DirtsideTurn.InterceptWithAreaDefence(Session, unit));
    }

    /// <summary>
    /// Why this element cannot intercept, or null when it can.
    /// </summary>
    /// <param name="unit">The platoon answering.</param>
    /// <param name="element">The element whose sensors would do it.</param>
    /// <returns>The refusal in words, or null.</returns>
    /// <remarks>
    /// Shared with <see cref="InterceptWithAreaDefence"/> rather than written twice, so a screen
    /// showing why a button is disabled uses the same words the command would refuse with.
    /// </remarks>
    public string? WhyInterceptionIsRefused(UnitId unit, ElementId element)
    {
        if (!HasUnit(unit))
        {
            return $"There is no platoon called '{unit}' on the table.";
        }

        if (Unit(unit).Element(element) is null)
        {
            return $"{Unit(unit).Name} has no element called '{element}'.";
        }

        var status = Status(unit).Element(element);
        if (status.IsDestroyed)
        {
            return $"{Describe(unit, element)} has been destroyed.";
        }

        if (status.IsSystemsDown)
        {
            return $"{Describe(unit, element)} has its systems down and cannot intercept.";
        }

        return status.AreaDefenceSensorsLive
            ? null
            : $"{Describe(unit, element)} does not have its area-defence sensors on.";
    }

    /// <summary>
    /// Tries to get an element's Systems Down marker off.
    /// </summary>
    /// <param name="element">The element whose crew are trying.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <param name="profile">The die and number this game's players entered off their own rulebook.</param>
    /// <returns>The game with the attempt made, or why it could not be.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Refused, not rolled and failed, on the activation the marker went on - the resolver says
    /// why, and refusing before the step is taken means the combat action is not spent on a roll
    /// that was never allowed. A failure costs the combat action and nothing else, so the same
    /// vehicle may try again every activation for as long as the game lasts.
    /// <para>
    /// A profile that does not carry this roll is refused in the same place and for the same
    /// reason: the crew must not spend their combat action on an attempt the game cannot settle.
    /// </para>
    /// </remarks>
    public GameOutcome<DirtsideGame> RecoverSystems(
        ElementId element,
        IQualityDiceRoller dice,
        DirtsideRulesProfile profile)
    {
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(profile);

        if (WhyRecoverSystemsIsRefused(element, profile) is { } reason)
        {
            return GameOutcome.Refused<DirtsideGame>(reason);
        }

        var unit = Session.CurrentFrame!.Unit;
        var stepped = TakeStep(DirtsideSteps.Act(DirtsideAction.RecoverSystems, element));
        if (!stepped.IsAllowed)
        {
            return stepped;
        }

        var status = Status(unit).Element(element);
        var attempt = SystemsDownRecovery.Attempt(
            profile,
            status.SystemsDownOnActivation ?? 0,
            CurrentActivationNumber,
            dice,
            Unit(unit).Element(element)!.HasBackupSystems);

        var name = Describe(unit, element);
        if (!attempt.Cleared)
        {
            return GameOutcome.Allowed(stepped.Value!.WithLog(
                $"{name}'s crew worked on the systems: rolled {attempt.Roll}, needed {attempt.Required}. Still down."));
        }

        return GameOutcome.Allowed(stepped.Value!
            .WithStatus(unit, platoon => platoon.WithElement(
                element, current => current with { IsSystemsDown = false, SystemsDownOnActivation = null }))
            .WithLog($"{name}'s crew worked on the systems: rolled {attempt.Roll}, needed {attempt.Required}. Systems back up."));
    }

    /// <summary>Whether an element could try to recover its systems now, and why not.</summary>
    /// <param name="element">The element.</param>
    /// <param name="profile">The die and number this game's players entered off their own rulebook.</param>
    /// <returns>The refusal in words, or null when the crew may try.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public string? WhyRecoverSystemsIsRefused(ElementId element, DirtsideRulesProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (Session.CurrentFrame is not { Kind: FrameKind.Activation } frame)
        {
            return "Nothing is activated.";
        }

        var platoon = Unit(frame.Unit);
        if (platoon.Element(element) is not { } definition)
        {
            return $"{platoon.Name} has no element called '{element}'.";
        }

        var status = Status(frame.Unit).Element(element);
        if (status.IsDestroyed)
        {
            return $"{definition.Name} is out of the battle.";
        }

        if (!status.IsSystemsDown)
        {
            return $"{definition.Name}'s systems are not down.";
        }

        if (!SystemsDownRecovery.CanAttempt(status.SystemsDownOnActivation ?? 0, CurrentActivationNumber))
        {
            return "Repairs cannot start until an activation after the one the damage happened on.";
        }

        if (profile.SystemsDownRecoveryDie is null || SystemsDownRecovery.Required(profile, definition.HasBackupSystems) is null)
        {
            return "This game has no die table entry for getting a Systems Down marker off. Enter the "
                + "die and the number it has to reach in the game's rules profile - this app ships "
                + "no dice of its own.";
        }

        var check = GroundCombatSequence.CanTakeStep(
            Session, DirtsideSteps.Act(DirtsideAction.RecoverSystems, element), new DirtsideActivationPolicy(this));
        return check.IsAllowed ? null : check.Reason;
    }

    /// <summary>Closes the open activation.</summary>
    /// <returns>The game with the activation closed, or why it could not be.</returns>
    /// <remarks>
    /// <para>
    /// Refused while any element still on the table has not said what it is doing, and the refusal
    /// names them - a player told only that the activation is incomplete has to guess who everybody
    /// is waiting on.
    /// </para>
    /// <para>
    /// Refused too while an assault is part-way through, because the tests it owes are owed by both
    /// sides and closing the frame would leave the defender's marker turned for nothing. The one
    /// stage that may be walked away from is the follow-through: the position is already taken, and
    /// declining to test for a second go is a choice, not an unfinished fight.
    /// </para>
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

        if (Assault is { Stage: not AssaultStage.AwaitingFollowThrough } open)
        {
            return GameOutcome.Refused<DirtsideGame>(
                $"{Unit(open.Attacker.Unit).Name} is in the middle of an assault on {Unit(open.DefenderUnit).Name}. Fight it out first.");
        }

        var closing = this;
        if (Assault is { } taken)
        {
            closing = (this with { Assault = null })
                .WithLog($"{Unit(taken.Attacker.Unit).Name} consolidated on the position rather than test to drive on through.");
        }

        // Whose activation is closing, read before the frame goes: an Under Fire marker lapses at the
        // end of the marked unit's *own* activation, and once the frame is closed there is nothing
        // left to say which unit that was.
        var activating = closing.Session.CurrentFrame is { Kind: FrameKind.Activation } closingFrame
            ? closingFrame.Unit
            : (UnitId?)null;

        var ended = closing.Apply(
            GroundCombatSequence.CanEndFrame(closing.Session, policy),
            () => GroundCombatSequence.EndFrame(closing.Session, policy));

        // The marker went on in the assault aftermath and, until this, came off nowhere at all: no
        // route in the game cleared it, and EndTurn deliberately cannot, because the clearing is tied
        // to the unit's own activation rather than to the turn - which is the whole reason
        // UnderFire.After takes that flag. A platoon that lost an assault therefore owed a reaction
        // test before every move it made for the rest of the game.
        if (!ended.IsAllowed || activating is not { } marked)
        {
            return ended;
        }

        var closed = ended.Value!;
        var wasMarked = closed.Status(marked).IsUnderFire;
        var stillMarked = UnderFire.After(wasMarked, itsOwnActivationEnded: true);
        if (wasMarked == stillMarked)
        {
            return GameOutcome.Allowed(closed);
        }

        return GameOutcome.Allowed(closed
            .WithStatus(marked, status => status with { IsUnderFire = stillMarked })
            .WithLog($"{closed.Unit(marked).Name} has finished its activation and is no longer under fire."));
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

    /// <summary>
    /// The number of the activation under way, for a marker to remember which one it went on.
    /// </summary>
    /// <remarks>
    /// The open frame's identity, which the session mints from one counter that never resets, so a
    /// later activation always has a larger number - across turns as well as within one. Zero when
    /// nothing is activated, which cannot happen for a marker placed by a shot but is the honest
    /// answer for one restored from a save that predates the number.
    /// </remarks>
    private int CurrentActivationNumber => Session.CurrentFrame?.Id.Value ?? 0;

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
