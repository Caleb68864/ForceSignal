using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>
/// Playing a turn.
/// </summary>
/// <remarks>
/// Every rule here belongs to <see cref="GroundCombatSequence"/> and none of them is reimplemented.
/// Each command asks the matching <c>Can*</c>, returns its reason when it says no, and otherwise
/// applies the verb and carries the new session into a new game. That is the whole of this file, and
/// it should stay that way: a rule that gets restated here is a rule with two homes.
/// </remarks>
public sealed partial record StarGruntGame
{
    /// <summary>Opens the next turn.</summary>
    /// <returns>The game with a turn under way, or why not.</returns>
    public GameOutcome<StarGruntGame> BeginTurn() =>
        Apply(GroundCombatSequence.CanBeginTurn(Session), () => GroundCombatSequence.BeginTurn(Session));

    /// <summary>Settles who takes the first activation this turn.</summary>
    /// <param name="chooser">The side making the choice.</param>
    /// <param name="takeIt">True to go first, false to make the opponent go first.</param>
    /// <returns>The game with the alternation started, or why not.</returns>
    public GameOutcome<StarGruntGame> ChooseFirstActivator(SideId chooser, bool takeIt) =>
        Apply(
            GroundCombatSequence.CanChooseFirstActivator(Session, chooser),
            () => GroundCombatSequence.ChooseFirstActivator(Session, chooser, takeIt));

    /// <summary>Opens an activation.</summary>
    /// <param name="side">The side activating.</param>
    /// <param name="unit">The unit being activated.</param>
    /// <returns>The game with the activation open, or why not.</returns>
    public GameOutcome<StarGruntGame> BeginActivation(SideId side, UnitId unit)
    {
        // Asked before the sequence sees it, because the sequence knows unit ids but not rosters:
        // to it an unknown id is simply a unit that is not this side's to activate, which is a true
        // but unhelpful thing to tell somebody who has mistyped a name.
        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{unit}' on the table.");
        }

        // Casualties are the game's business, not the sequence's: the shared layer alternates
        // between units and has no idea any of them can die. Without this a squad that had been
        // wiped out kept its place in the alternation and could still be activated.
        if (Status(unit).IsWipedOut)
        {
            return GameOutcome.Refused<StarGruntGame>($"{Unit(unit).Name} has been wiped out.");
        }

        return Apply(
            GroundCombatSequence.CanBeginActivation(Session, side, unit),
            () => GroundCombatSequence.BeginActivation(Session, side, unit));
    }

    /// <summary>Takes one step inside the open activation.</summary>
    /// <param name="step">What the unit did.</param>
    /// <returns>The game with the step recorded, or why not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public GameOutcome<StarGruntGame> TakeStep(ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var policy = Policy();
        return Apply(
            GroundCombatSequence.CanTakeStep(Session, step, policy),
            () => GroundCombatSequence.TakeStep(Session, step, policy));
    }

    /// <summary>Closes the frame on top of the stack.</summary>
    /// <returns>The game with the activation closed, or why not.</returns>
    public GameOutcome<StarGruntGame> EndActivation()
    {
        var policy = Policy();
        return Apply(
            GroundCombatSequence.CanEndFrame(Session, policy),
            () => GroundCombatSequence.EndFrame(Session, policy));
    }

    /// <summary>Declines to activate anything, when the rules allow it.</summary>
    /// <param name="side">The side passing.</param>
    /// <returns>The game with the pass recorded, or why not.</returns>
    public GameOutcome<StarGruntGame> Pass(SideId side) =>
        Apply(GroundCombatSequence.CanPass(Session, side), () => GroundCombatSequence.Pass(Session, side));

    /// <summary>Ends the turn once both sides are done with it.</summary>
    /// <returns>The game with the turn ended, or why not.</returns>
    public GameOutcome<StarGruntGame> EndTurn() =>
        Apply(GroundCombatSequence.CanEndTurn(Session), () => GroundCombatSequence.EndTurn(Session));

    /// <summary>
    /// The activation policy, built over this game.
    /// </summary>
    /// <remarks>
    /// Built per call rather than held, because the game is a value: a policy kept in a field would
    /// be reading whichever game happened to construct it, which after one casualty is the wrong one.
    /// </remarks>
    internal StarGruntActivationPolicy Policy() => new(this);

    /// <summary>Runs a sequence transition, turning its refusal into an answer.</summary>
    private GameOutcome<StarGruntGame> Apply(SequenceCheck check, Func<GroundCombatSession> transition) =>
        check.IsAllowed
            ? GameOutcome.Allowed(this with { Session = transition() })
            : GameOutcome.Refused<StarGruntGame>(check.Reason!);
}
