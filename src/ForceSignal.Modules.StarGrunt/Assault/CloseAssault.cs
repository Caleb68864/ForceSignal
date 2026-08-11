using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Modules.StarGrunt.Assault;

/// <summary>One figure squaring up in a melee.</summary>
/// <param name="Quality">The figure's quality die.</param>
/// <param name="WeaponShift">
/// How many die types his close-combat weapon is worth, read off the player's own table. Nothing
/// here knows what a sword or a shotgun is worth; what it knows is that a weapon shifts the die and
/// that the shift is an open one.
/// </param>
/// <param name="PowerArmour">True when he is in power armour, which doubles his score.</param>
/// <param name="InCoverThisRound">
/// True when he is a defender still getting the benefit of cover, which is the first round only.
/// </param>
public readonly record struct Combatant(
    QualityDie Quality,
    int WeaponShift = 0,
    bool PowerArmour = false,
    bool InCoverThisRound = false);

/// <summary>How one pair of figures settled it.</summary>
/// <param name="AttackerDie">The die the attacker ended up throwing, after every shift.</param>
/// <param name="DefenderDie">The die the defender ended up throwing.</param>
/// <param name="AttackerScore">What the attacker's figure made of his die.</param>
/// <param name="DefenderScore">What the defender's figure made of his.</param>
/// <param name="AttackerDown">True when the attacker's figure went down.</param>
/// <param name="DefenderDown">True when the defender's figure went down.</param>
/// <remarks>
/// The dice are kept alongside the scores because the shifts are where a close combat is actually
/// decided, and a table arguing about a result wants to see which dice were picked up.
/// </remarks>
public readonly record struct MeleeExchange(
    QualityDie AttackerDie,
    QualityDie DefenderDie,
    int AttackerScore,
    int DefenderScore,
    bool AttackerDown,
    bool DefenderDown)
{
    /// <summary>True when neither could put the other down and both fight on.</summary>
    public bool Tied => !AttackerDown && !DefenderDown;
}

/// <summary>What became of a figure that went down.</summary>
public enum DownedFate
{
    /// <summary>Killed.</summary>
    Dead = 0,

    /// <summary>Hurt, and needing treatment like any other casualty.</summary>
    Wounded = 1,

    /// <summary>Knocked about. Back on his feet if his side won, a prisoner if it did not.</summary>
    Stunned = 2,
}

/// <summary>
/// Close assault: the nerve to charge, the nerve to stand, and the business at arm's length.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is procedure. The numbers that decide a charge - which weapon shifts a die by
/// how much, whether a unit is terrifying - are the user's own, and the two that are not are
/// countable rather than tabled: the threat of a charge follows from the attacker's own confidence,
/// and the threat of receiving one follows from the odds.
/// </para>
/// <para>
/// What is deliberately not here is who fights whom. The attacker pairs off one figure per
/// defender and the <em>defender</em> allocates the leftovers, which exists to stop an attacker
/// ganging up on leaders and specialists - a choice between two players standing over a table, not
/// something an engine should make for them.
/// </para>
/// </remarks>
public static class CloseAssault
{
    /// <summary>
    /// Whether a unit in this state of mind will go in at all.
    /// </summary>
    /// <param name="confidence">The attacker's confidence.</param>
    /// <returns>True when it can be ordered to charge.</returns>
    /// <remarks>
    /// A rule rather than a number, which is why it lives here while the threat the charge asks does
    /// not: a unit that has already lost its nerve does not find it by being told to charge. How
    /// much nerve the charge costs a unit that will go is a threat level off the player's own table,
    /// like every other threat level in this app.
    /// </remarks>
    public static bool CanCharge(ConfidenceLevel confidence) => confidence > ConfidenceLevel.Broken;

    /// <summary>
    /// A side's weight for the odds, counting a figure in power armour as two men.
    /// </summary>
    /// <param name="figures">Figures still standing.</param>
    /// <param name="powerArmoured">How many of them are in power armour.</param>
    /// <returns>The strength the odds are read from.</returns>
    public static int Strength(int figures, int powerArmoured = 0) =>
        Math.Max(0, figures) + Math.Clamp(powerArmoured, 0, Math.Max(0, figures));

    /// <summary>
    /// How much nerve it takes to stand and receive a charge.
    /// </summary>
    /// <param name="attackerStrength">The charging side's strength.</param>
    /// <param name="defenderStrength">The receiving side's strength.</param>
    /// <param name="terror">True when the attackers are the sort that frighten people.</param>
    /// <returns>The threat level for the defender's confidence test.</returns>
    /// <remarks>
    /// The odds round down and never fall below one, so an even fight is still worth testing for.
    /// Terror doubles whatever the odds produced, which is what makes a modest edge terrifying.
    /// </remarks>
    public static int StandThreat(int attackerStrength, int defenderStrength, bool terror = false)
    {
        var odds = defenderStrength <= 0 ? attackerStrength : attackerStrength / defenderStrength;
        var threat = Math.Max(1, odds);
        return terror ? threat * 2 : threat;
    }

    /// <summary>
    /// Fights one pair of figures.
    /// </summary>
    /// <param name="attacker">The charging figure.</param>
    /// <param name="defender">The receiving figure.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>What the exchange came to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    /// <remarks>
    /// The higher roll downs the loser and a tie settles nothing - both fight on. Weapon shifts are
    /// open, so a shift that would push a die past the top of the ladder comes off the opponent's
    /// die instead, which is the same crossover the shared dice layer already does for fire.
    /// Power armour doubles the score after the roll rather than shifting the die before it.
    /// </remarks>
    public static MeleeExchange Fight(Combatant attacker, Combatant defender, IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(roller);

        var dice = QualityDice.ShiftOpposed(
            attacker.Quality,
            attacker.WeaponShift,
            defender.Quality,
            defender.WeaponShift + (defender.InCoverThisRound ? CoverShift : 0));

        var attackerScore = roller.Roll(dice.Actor) * (attacker.PowerArmour ? 2 : 1);
        var defenderScore = roller.Roll(dice.Opponent) * (defender.PowerArmour ? 2 : 1);

        return new MeleeExchange(
            dice.Actor,
            dice.Opponent,
            attackerScore,
            defenderScore,
            AttackerDown: defenderScore > attackerScore,
            DefenderDown: attackerScore > defenderScore);
    }

    /// <summary>
    /// Reads one roll against the bands a player's own table gives for a downed figure.
    /// </summary>
    /// <param name="roll">What the die showed.</param>
    /// <param name="deadUpTo">The highest roll that means dead.</param>
    /// <param name="woundedUpTo">The highest roll that means wounded; anything above it is stunned.</param>
    /// <returns>His fate.</returns>
    /// <remarks>
    /// The bands are supplied rather than known here, for the same reason every other threshold in
    /// this app is. What the engine owns is the shape - worst outcome low, best outcome high, and
    /// the roll left until the end of the assault because a man down in the first round may be
    /// picked up by whoever holds the ground at the finish.
    /// </remarks>
    public static DownedFate Fate(int roll, int deadUpTo, int woundedUpTo) =>
        roll <= deadUpTo ? DownedFate.Dead
        : roll <= woundedUpTo ? DownedFate.Wounded
        : DownedFate.Stunned;

    /// <summary>
    /// Which side has to test its nerve first between rounds, and at what threat.
    /// </summary>
    /// <param name="attackerCasualties">What the charging side has lost in this assault.</param>
    /// <param name="defenderCasualties">What the receiving side has lost in it.</param>
    /// <returns>True when it is the attacker who tests first, and the threat each side faces.</returns>
    /// <remarks>
    /// The side that has come off worst tests first, and the defender tests first when the losses
    /// are even. Each side's threat is one per casualty <em>it</em> has taken, so the test that
    /// hurts most is the one the losing side takes.
    /// </remarks>
    public static (bool AttackerFirst, int AttackerThreat, int DefenderThreat) BetweenRounds(
        int attackerCasualties,
        int defenderCasualties) =>
        (attackerCasualties > defenderCasualties, Math.Max(0, attackerCasualties), Math.Max(0, defenderCasualties));

    /// <summary>
    /// What cover is worth to a defender in the first round of a melee.
    /// </summary>
    /// <remarks>
    /// One step, and named rather than inlined because it is the one shift in a close combat this
    /// engine owns: it is a property of being charged in cover rather than of any weapon, and it
    /// stops mattering the moment the attackers are in among them.
    /// </remarks>
    private const int CoverShift = 1;
}
