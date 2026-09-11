using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>How good the firing element's gunnery is.</summary>
public enum FireControlLevel
{
    /// <summary>The cheapest sight worth fitting.</summary>
    Basic = 0,

    /// <summary>A proper fire-control fit.</summary>
    Enhanced = 1,

    /// <summary>The best available.</summary>
    Superior = 2,
}

/// <summary>Which of a weapon's three range bands the shot falls in.</summary>
/// <remarks>
/// The values are the rungs each band shifts the firer's die, so the band can be added directly
/// rather than looked up.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Close, medium and long are what the rules call the three bands, and a table "
        + "reads them off a record card by those names. Renaming the member to satisfy the "
        + "analyzer would put the code and the card out of step for no benefit.")]
public enum WeaponRangeBand
{
    /// <summary>Inside the weapon's close band, where the gunner can hardly miss.</summary>
    Close = 1,

    /// <summary>The band the weapon is rated at.</summary>
    Medium = 0,

    /// <summary>Out at the edge of the weapon's reach.</summary>
    Long = -1,
}

/// <summary>
/// What the target is doing to make itself hard to hit. Only the single best of these counts - they
/// do not stack, and the target keeps the higher of this and its signature die rather than adding.
/// </summary>
public enum DefensivePosture
{
    /// <summary>Out in the open and doing nothing about it.</summary>
    None = 0,

    /// <summary>Behind something that breaks up the outline, or caught mid pop-up.</summary>
    SoftCover = 1,

    /// <summary>Moving hard and unpredictably.</summary>
    Evading = 2,

    /// <summary>Only the upper hull showing, or dug into a scrape.</summary>
    HullDown = 3,

    /// <summary>Nothing but the turret over the crest.</summary>
    TurretDown = 4,
}

/// <summary>The dice a shot will be settled with, or the reason there is no shot.</summary>
/// <param name="CanFire">
/// False when the firer's die has dropped off the bottom of the ladder, or when the profile does
/// not say which die one of the three sides of this shot rolls.
/// </param>
/// <param name="FirerDie">The die the firer rolls.</param>
/// <param name="TargetPrimaryDie">The die the target rolls for its signature.</param>
/// <param name="TargetSecondaryDie">The die the target rolls for its posture, if it has one.</param>
/// <param name="Reason">Why there is no shot, or null when there is one.</param>
/// <param name="IsMissingFromProfile">
/// True when the reason is a row the players have not entered rather than a rule about the shot.
/// The two are told apart here because a table reading a refusal needs to know whether to measure
/// again or to go and fill in their profile.
/// </param>
public readonly record struct ShotSolution(
    bool CanFire,
    QualityDie FirerDie,
    QualityDie TargetPrimaryDie,
    QualityDie? TargetSecondaryDie,
    string? Reason,
    bool IsMissingFromProfile = false)
{
    /// <summary>
    /// A shot the rules will not allow, carrying no dice at all.
    /// </summary>
    /// <param name="reason">Why there is no shot.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// The dice are left at <c>default</c> rather than filled in with something plausible. A refusal
    /// used to hand back a D4 as the target's die - a rung of the ladder, in the one code path where
    /// no die is being thrown - and a value that is never read is exactly the sort of number this
    /// module has just finished taking out of its own source. Nothing reads these: a refused shot
    /// produces no attempts, so there is nothing to render them from.
    /// </remarks>
    public static ShotSolution NoShot(string reason) => new(false, default, default, null, reason);

    /// <summary>A shot that cannot be worked out because the profile does not carry a row it needs.</summary>
    /// <param name="reason">Which row, in the words a table can act on.</param>
    /// <returns>The refusal.</returns>
    public static ShotSolution NotOnTheProfile(string reason) =>
        new(false, default, default, null, reason, IsMissingFromProfile: true);
}

/// <summary>The settled result of stage one.</summary>
/// <param name="Solution">The dice the shot was taken with.</param>
/// <param name="FirerRoll">What the firer rolled.</param>
/// <param name="TargetPrimaryRoll">What the target rolled for its signature.</param>
/// <param name="TargetSecondaryRoll">What the target rolled for its posture, if anything.</param>
/// <param name="IsHit">True when the firer's roll got through.</param>
public readonly record struct HitAttempt(
    ShotSolution Solution,
    int FirerRoll,
    int TargetPrimaryRoll,
    int? TargetSecondaryRoll,
    bool IsHit)
{
    /// <summary>The number the firer actually had to beat: the better of the target's two dice.</summary>
    public int TargetScore => Math.Max(TargetPrimaryRoll, TargetSecondaryRoll ?? 0);
}

/// <summary>
/// Stage one: whether the shot connects at all.
/// </summary>
/// <remarks>
/// <para>
/// The whole to-hit question is answered by which die each side throws. A better sight, a longer
/// shot, a stealthier target - none of them is a modifier added to a roll, each is baked into the
/// die type. That keeps the arithmetic at the table to a single comparison.
/// </para>
/// <para>
/// The one asymmetry worth knowing: the target may roll two dice but keeps the higher rather than
/// adding them, so a good posture sets a floor on how hard it is to hit rather than stacking with
/// how quiet the vehicle is.
/// </para>
/// </remarks>
public static class HitResolution
{
    /// <summary>
    /// Works out the dice a shot will be settled with.
    /// </summary>
    /// <param name="profile">The dice this game's players entered off their own rulebook.</param>
    /// <param name="fireControl">The firing element's gunnery.</param>
    /// <param name="band">Which range band the shot falls in.</param>
    /// <param name="targetSignature">
    /// How loud the target is, from 1 for the largest to 5 for the smallest. A big vehicle is easy
    /// to see, so a low number here means a large defensive die.
    /// </param>
    /// <param name="posture">What the target is doing about being shot at.</param>
    /// <param name="firerMovedOverHalf">
    /// True when the firer has moved, or will move, more than half its movement this activation.
    /// </param>
    /// <returns>The dice, or the reason there is no shot worth taking.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The signature is outside one to five.</exception>
    /// <remarks>
    /// <para>
    /// Three dice are looked up and not one of them is this app's. A row the profile does not carry
    /// is a refusal that names the row, never a substitution: a game played on a die nobody entered
    /// is a game played on somebody else's rules, which is the whole reason these tables left this
    /// file.
    /// </para>
    /// <para>
    /// Only the rows this particular shot reads are required. A table whose vehicles are all
    /// basic-gunnery is never asked what a superior sight rolls, and a target out in the open is
    /// never asked what hull down is worth.
    /// </para>
    /// </remarks>
    public static ShotSolution Solve(
        DirtsideRulesProfile profile,
        FireControlLevel fireControl,
        WeaponRangeBand band,
        int targetSignature,
        DefensivePosture posture = DefensivePosture.None,
        bool firerMovedOverHalf = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetSignature, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetSignature, 5);

        // The sight sets the die at medium range; the band and shooting on the move move it. Which
        // die that is belongs to the player's rulebook, so it is read rather than known.
        if (profile.FireControlDie(fireControl) is not { } baseDie)
        {
            return ShotSolution.NotOnTheProfile(Missing($"the die a {fireControl} fire control rolls"));
        }

        if (profile.SignatureDie(targetSignature) is not { } primary)
        {
            return ShotSolution.NotOnTheProfile(Missing($"the die a signature {targetSignature} target rolls"));
        }

        if (!profile.HasPostureDie(posture))
        {
            return ShotSolution.NotOnTheProfile(Missing($"the die a {posture} target rolls"));
        }

        var steps = (int)band + (firerMovedOverHalf ? -1 : 0);
        var shifted = QualityDice.Shift(baseDie, steps);
        if (shifted.Overflow < 0)
        {
            // Unlike a shift that runs off the top, running off the bottom is not capped: there is
            // no die left to roll. The smallest sight cannot take a long shot on the move at all.
            return ShotSolution.NoShot(
                "There is no die left to roll: that sight cannot take that shot on the move.");
        }

        return new ShotSolution(true, shifted.Die, primary, profile.PostureDie(posture), null);
    }

    /// <summary>
    /// The refusal a missing row produces, in the words a table needs to fix it.
    /// </summary>
    /// <param name="row">What is not on the profile, phrased as the thing being looked up.</param>
    /// <returns>The sentence to refuse with.</returns>
    /// <remarks>
    /// It names the row rather than saying the profile is incomplete, for the same reason the salvo
    /// refusal one engine over names the reach the player entered: a refusal a player cannot act on
    /// is only a slower way of stopping.
    /// </remarks>
    private static string Missing(string row) =>
        $"This game has no die table entry for {row}. Enter it in the game's rules profile - "
        + "this app ships no dice of its own.";

    /// <summary>Rolls stage one.</summary>
    /// <param name="solution">The dice the shot is settled with.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>Whether the shot connected, and what was rolled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The solution says there is no shot.</exception>
    public static HitAttempt Roll(ShotSolution solution, IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(roller);
        if (!solution.CanFire)
        {
            throw new InvalidOperationException(solution.Reason ?? "That shot cannot be taken.");
        }

        var firerRoll = roller.Roll(solution.FirerDie);
        var primaryRoll = roller.Roll(solution.TargetPrimaryDie);
        int? secondaryRoll = solution.TargetSecondaryDie is { } secondary ? roller.Roll(secondary) : null;

        // The target keeps its better die rather than adding the two together.
        var targetScore = Math.Max(primaryRoll, secondaryRoll ?? 0);
        return new HitAttempt(
            solution,
            firerRoll,
            primaryRoll,
            secondaryRoll,
            OpposedRolls.Wins(firerRoll, targetScore));
    }
}
