using ForceSignal.Modules.StarGrunt.Dice;

namespace ForceSignal.Modules.StarGrunt.Combat;

/// <summary>What one potential hit did when it met the target's armour.</summary>
public enum HitEffect
{
    /// <summary>The armour stopped it.</summary>
    Stopped = 0,

    /// <summary>Someone is hurt. Two of these on one figure in a single resolution is a death.</summary>
    Wound = 1,

    /// <summary>Straight through. Someone is dead.</summary>
    Kill = 2,
}

/// <summary>One potential hit resolved against armour, kept whole so a table can audit it.</summary>
/// <param name="ImpactRoll">What the weapon rolled.</param>
/// <param name="ArmourRoll">What the armour rolled.</param>
/// <param name="Effect">What that comparison amounted to.</param>
public readonly record struct ResolvedHit(int ImpactRoll, int ArmourRoll, HitEffect Effect);

/// <summary>
/// Everything one exchange of fire produced, from the opposed roll down to the individual hits.
/// </summary>
/// <param name="Range">The range solution the shot was taken under.</param>
/// <param name="Opposed">The opposed roll that opened the exchange.</param>
/// <param name="Suppresses">True when the fire was at least a minor success.</param>
/// <param name="PotentialHits">How many hits the fire total bought, including any extra one.</param>
/// <param name="RemainderRoll">
/// The roll made for the leftover points, or null when the total divided evenly or the fire was
/// not effective.
/// </param>
/// <param name="Hits">Each potential hit resolved against armour.</param>
public sealed record FireOutcome(
    RangeSolution Range,
    MultipleOpposedRoll Opposed,
    bool Suppresses,
    int PotentialHits,
    int? RemainderRoll,
    IReadOnlyList<ResolvedHit> Hits)
{
    /// <summary>Figures killed outright by this fire.</summary>
    public int Kills => Hits.Count(hit => hit.Effect == HitEffect.Kill);

    /// <summary>Wound results this fire scored, before any are paired into deaths.</summary>
    public int Wounds => Hits.Count(hit => hit.Effect == HitEffect.Wound);
}

/// <summary>
/// What is being fired, at what, from where. Every die here is entered by the user from their own
/// records - the engine ships no weapon or armour values of its own.
/// </summary>
/// <param name="FirerQuality">The firing unit's quality die, which also sets the range band.</param>
/// <param name="FirepowerDie">
/// The small-arms firepower die, derived from how many figures are actually shooting.
/// </param>
/// <param name="SupportDice">One extra die per support weapon joining the volley.</param>
/// <param name="ImpactDie">The weapon's impact die, rolled once per potential hit.</param>
/// <param name="TargetArmourDie">The target's personal armour die, before cover.</param>
/// <param name="DistanceInches">Distance between firer and target.</param>
/// <param name="TargetPosture">The target's cover and posture.</param>
/// <param name="IsCloseRangeWeapon">True for a weapon effective only inside one band.</param>
public sealed record FireAttempt(
    QualityDie FirerQuality,
    QualityDie FirepowerDie,
    IReadOnlyList<QualityDie> SupportDice,
    QualityDie ImpactDie,
    QualityDie TargetArmourDie,
    decimal DistanceInches,
    TargetPosture TargetPosture,
    bool IsCloseRangeWeapon = false);

/// <summary>
/// Direct fire at a dispersed target - which is to say, at infantry.
/// </summary>
/// <remarks>
/// <para>
/// The whole sequence is one opposed roll followed by three steps. The firer throws a quality die,
/// a firepower die, and one more for each support weapon; the target throws a single range die.
/// How many firer dice get through decides everything: none is nothing, one pins heads down, two
/// or more lets the volley actually hurt someone.
/// </para>
/// <para>
/// The step that reads oddly and is worth stating plainly: an effective volley's hit count comes
/// from adding up <em>all</em> the firer's dice - the ones that lost as well as the ones that won -
/// and dividing by the <em>type</em> of the target's range die rather than the number it rolled.
/// That is why distance and cover reduce casualties twice over, once by being harder to beat and
/// again by being a larger divisor.
/// </para>
/// <para>
/// Suppression, not casualties, is the ordinary result of a firefight. Any success at all lays it.
/// </para>
/// </remarks>
/// <param name="roller">Die source, injectable so a game can be replayed exactly.</param>
public sealed class FireCombat(IQualityDiceRoller? roller = null)
{
    private readonly IQualityDiceRoller _roller = roller ?? new QualityDiceRoller();

    /// <summary>Resolves one exchange of fire from end to end.</summary>
    /// <param name="attempt">What is firing at what.</param>
    /// <returns>Everything the exchange produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attempt"/> is null.</exception>
    public FireOutcome Resolve(FireAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var range = RangeBands.Resolve(
            attempt.DistanceInches,
            attempt.FirerQuality,
            attempt.TargetPosture,
            attempt.IsCloseRangeWeapon);
        if (!range.CanFireEffectively)
        {
            return new FireOutcome(range, default, false, 0, null, []);
        }

        // Step 1. The firer's hand is quality, firepower, and one die per support weapon; the
        // target answers with the single range die.
        var firerDice = new List<QualityDie>(2 + attempt.SupportDice.Count)
        {
            attempt.FirerQuality,
            attempt.FirepowerDie,
        };
        firerDice.AddRange(attempt.SupportDice);

        var firerRolls = firerDice.Select(_roller.Roll).ToArray();
        var opposed = OpposedRolls.Resolve(firerRolls, _roller.Roll(range.RangeDie));
        var suppresses = opposed.Result != OpposedResult.Failed;
        if (opposed.Result != OpposedResult.Effective)
        {
            return new FireOutcome(range, opposed, suppresses, 0, null, []);
        }

        // Step 2. Potential hits, from the total of every firer die divided by the range die's
        // type. The leftover points buy a chance at one more.
        var divisor = QualityDice.Faces(range.RangeDie);
        var potentialHits = opposed.ActorTotal / divisor;
        var remainder = opposed.ActorTotal % divisor;

        int? remainderRoll = null;
        if (remainder > 0)
        {
            remainderRoll = _roller.Roll(range.RangeDie);
            if (remainderRoll <= remainder)
            {
                potentialHits++;
            }
        }

        // Step 3. Each potential hit is the weapon's impact against the target's armour, with the
        // armour shifted up by whatever the target is hiding behind. No other modifiers apply.
        var armour = QualityDice.ShiftClosed(attempt.TargetArmourDie, attempt.TargetPosture.Shifts);
        var hits = new List<ResolvedHit>(potentialHits);
        for (var hit = 0; hit < potentialHits; hit++)
        {
            var impactRoll = _roller.Roll(attempt.ImpactDie);
            var armourRoll = _roller.Roll(armour);
            hits.Add(new ResolvedHit(impactRoll, armourRoll, Compare(impactRoll, armourRoll)));
        }

        return new FireOutcome(range, opposed, true, potentialHits, remainderRoll, hits);
    }

    /// <summary>
    /// Reads one impact roll against one armour roll. Armour that matches the impact stops it -
    /// the general rule that a roll must exceed rather than equal holds here too.
    /// </summary>
    /// <param name="impactRoll">What the weapon rolled.</param>
    /// <param name="armourRoll">What the armour rolled.</param>
    /// <returns>What the hit amounted to.</returns>
    public static HitEffect Compare(int impactRoll, int armourRoll) => impactRoll switch
    {
        _ when impactRoll > armourRoll * 2 => HitEffect.Kill,
        _ when impactRoll > armourRoll => HitEffect.Wound,
        _ => HitEffect.Stopped,
    };
}
