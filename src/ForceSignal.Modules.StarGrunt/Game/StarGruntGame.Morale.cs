using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

public sealed partial record StarGruntGame
{
    /// <summary>
    /// Puts a unit's nerve to the test after something bad has happened to it.
    /// </summary>
    /// <param name="unit">The unit under strain.</param>
    /// <param name="threatLevel">
    /// How serious the thing that happened was, read off the player's own threat table.
    /// </param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the test resolved, or why it could not be taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Not an action, and not tied to an activation.</b> A test is taken the moment the
    /// triggering event happens, to whichever unit it happened to - which is usually a unit that has
    /// not gone yet, and may be one that never goes this turn. A unit can be tested several times in
    /// a turn and shed several levels before it ever activates.
    /// </para>
    /// <para>
    /// The threat level is supplied rather than derived. It comes from a table rated against the
    /// force's mission motivation, and that table is the user's own - the same reason the firepower
    /// die is an input. What the engine owns is the procedure: quality die against leadership plus
    /// threat, one level lost on a miss and two on a bad one.
    /// </para>
    /// </remarks>
    public GameOutcome<StarGruntGame> TakeConfidenceTest(UnitId unit, int threatLevel, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{unit}' on the table.");
        }

        if (threatLevel < 0)
        {
            return GameOutcome.Refused<StarGruntGame>("A threat level cannot be less than nothing.");
        }

        var definition = Unit(unit);
        var status = Status(unit);
        var test = Confidence.Test(
            status.Confidence,
            definition.QualityDie,
            definition.LeadershipValue,
            threatLevel,
            dice);

        var outcome = test.LevelsLost == 0
            ? $"held at {test.After}"
            : $"lost {(test.LevelsLost == 1 ? "a level" : "two levels")} to {test.After}";

        return GameOutcome.Allowed(
            this
                .WithStatus(unit, current => current with { Confidence = test.After })
                .WithLog(
                    $"{definition.Name} tested its nerve: rolled {test.Roll} against {test.ScoreToBeat} "
                    + $"(leadership {definition.LeadershipValue} plus threat {threatLevel}) and {outcome}."));
    }

    /// <summary>
    /// Spends a command element's action steadying a subordinate.
    /// </summary>
    /// <param name="rallyingUnit">The command element doing the rallying, which spends the action.</param>
    /// <param name="ralliedUnit">The unit being steadied, which does the rolling.</param>
    /// <param name="dice">Where the die result comes from.</param>
    /// <returns>The game with the attempt resolved, or why it could not be made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dice"/> is null.</exception>
    /// <remarks>
    /// The action belongs to the rallying unit and the roll belongs to the rallied one, which is
    /// unusual enough to be worth saying: the score to beat is both leadership values added
    /// together, so a good commander and a good sergeant are both worth something. One level per
    /// success, and never past what the unit's fatigue allows.
    /// </remarks>
    public GameOutcome<StarGruntGame> Rally(UnitId rallyingUnit, UnitId ralliedUnit, IQualityDiceRoller dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (!HasUnit(rallyingUnit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{rallyingUnit}' on the table.");
        }

        if (!HasUnit(ralliedUnit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{ralliedUnit}' on the table.");
        }

        if (rallyingUnit == ralliedUnit)
        {
            return GameOutcome.Refused<StarGruntGame>("A unit cannot rally itself: rallying comes from above.");
        }

        var commander = Unit(rallyingUnit);
        var subordinate = Unit(ralliedUnit);

        if (commander.Side != subordinate.Side)
        {
            return GameOutcome.Refused<StarGruntGame>($"{commander.Name} does not command {subordinate.Name}.");
        }

        // Only a superior command element can steady a subordinate - a platoon commander for a
        // squad, a company commander for a platoon. Two squads cannot talk each other round.
        if (commander.Level <= subordinate.Level)
        {
            return GameOutcome.Refused<StarGruntGame>(
                $"{commander.Name} is not senior to {subordinate.Name}: rallying comes from above.");
        }
        var subordinateStatus = Status(ralliedUnit);
        var ceiling = Confidence.StartingLevel(subordinate.Fatigue);

        if (subordinateStatus.Confidence >= ceiling)
        {
            return GameOutcome.Refused<StarGruntGame>(
                $"{subordinate.Name} is already as steady as {(subordinate.Fatigue == FatigueLevel.Fresh ? "it gets" : "its fatigue allows")}.");
        }

        // The commander's action is spent first, and stays spent on a failure.
        var spent = TakeStep(StarGruntSteps.Simple(StarGruntAction.Rally));
        if (!spent.IsAllowed)
        {
            return spent;
        }

        var scoreToBeat = commander.LeadershipValue + subordinate.LeadershipValue;
        var roll = dice.Roll(subordinate.QualityDie);
        var steadied = OpposedRolls.BeatsTarget(roll, scoreToBeat);
        var lifted = steadied
            ? Confidence.Rally(subordinateStatus.Confidence, 1, subordinate.Fatigue)
            : subordinateStatus.Confidence;

        return GameOutcome.Allowed(
            spent.Value!
                .WithStatus(ralliedUnit, current => current with { Confidence = lifted })
                .WithLog(
                    $"{commander.Name} rallied {subordinate.Name}: rolled {roll} against {scoreToBeat} and "
                    + (steadied ? $"steadied it to {lifted}." : "could not get through.")));
    }

    /// <summary>
    /// Spends an action putting a scattered unit back in order.
    /// </summary>
    /// <param name="unit">The unit sorting itself out.</param>
    /// <returns>The game with the unit back in order, or why it could not be.</returns>
    /// <remarks>
    /// Whether a unit is scattered is measured with a ruler at the table, so being disorganised is
    /// declared rather than computed - the same stance the rest of this takes on cover and range.
    /// What the game owns is the consequence: a disorganised unit must reorganise before it does
    /// anything else, and the activation policy already enforces that.
    /// </remarks>
    public GameOutcome<StarGruntGame> Reorganise(UnitId unit)
    {
        if (!HasUnit(unit))
        {
            return GameOutcome.Refused<StarGruntGame>($"There is no unit called '{unit}' on the table.");
        }

        if (!Status(unit).IsDisorganised)
        {
            return GameOutcome.Refused<StarGruntGame>($"{Unit(unit).Name} is already in good order.");
        }

        var spent = TakeStep(StarGruntSteps.Simple(StarGruntAction.Reorganise));
        if (!spent.IsAllowed)
        {
            return spent;
        }

        return GameOutcome.Allowed(
            spent.Value!
                .WithStatus(unit, current => current with { IsDisorganised = false })
                .WithLog($"{Unit(unit).Name} pulled itself back together."));
    }

    /// <summary>Replaces a unit's definition, for the scenario facts a game can change.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="edit">What to change about it.</param>
    /// <returns>The game with the change applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is null.</exception>
    public StarGruntGame WithUnitEdit(UnitId unit, Func<UnitDefinition, UnitDefinition> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return this with { Units = Units.SetItem(unit, edit(Unit(unit))) };
    }
}
