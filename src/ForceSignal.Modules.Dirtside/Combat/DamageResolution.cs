using System.Collections.ObjectModel;
using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>What the numbers alone did to the target.</summary>
public enum NumericalDamage
{
    /// <summary>The total fell short of the armour. The plating held.</summary>
    None = 0,

    /// <summary>The total exactly matched the armour.</summary>
    Damaged = 1,

    /// <summary>The total beat the armour. Out of the battle.</summary>
    KnockedOut = 2,
}

/// <summary>One chit as it came out of the pot, with what it turned out to be worth.</summary>
/// <param name="Chit">The chit itself.</param>
/// <param name="Counts">
/// False when the chit was drawn but does nothing. It still consumed its slot in the draw, which is
/// the entire reason one weapon differs from another.
/// </param>
/// <param name="CountedValue">What it added to the total, after the value scale.</param>
public readonly record struct DrawnChit(DamageChit Chit, bool Counts, int CountedValue)
{
    /// <summary>
    /// True when this was a perfectly good chit that happened to be printed with a zero.
    /// </summary>
    /// <remarks>
    /// Distinguishable from an invalid chit on purpose. Both add nothing, but only one of them says
    /// anything about the weapon.
    /// </remarks>
    public bool IsValidZero => Counts && !Chit.IsSpecial && Chit.Value == 0;

    /// <summary>True when the chit was drawn, took up a slot, and did nothing.</summary>
    public bool WastedTheSlot => !Counts;
}

/// <summary>Everything a hit's damage resolution settled.</summary>
/// <param name="Draw">Every chit that left the pot, in order, with what each was worth.</param>
/// <param name="ValidTotal">The total of the counting chits, after the value scale.</param>
/// <param name="ArmourValue">What the total was compared against.</param>
/// <param name="Numerical">What the total alone did.</param>
/// <param name="Immobilised">The target may never move again, but may still fight from the spot.</param>
/// <param name="TargetSystemsDown">The target may still move but may take no action.</param>
/// <param name="FirerSystemsDown">The firer's own systems failed and it may take no combat action.</param>
/// <param name="CatastrophicKill">The target is gone entirely, whatever its armour.</param>
/// <param name="ShotNeverHappened">
/// The firer's systems failed, so the shot is treated as never fired and the target takes nothing at
/// all - including anything the rest of this same draw would have done to it.
/// </param>
public sealed record DamageOutcome(
    IReadOnlyList<DrawnChit> Draw,
    int ValidTotal,
    int ArmourValue,
    NumericalDamage Numerical,
    bool Immobilised,
    bool TargetSystemsDown,
    bool FirerSystemsDown,
    bool CatastrophicKill,
    bool ShotNeverHappened)
{
    /// <summary>True when the target is out of the battle, by the numbers or by a Boom.</summary>
    public bool TargetDestroyed => CatastrophicKill || Numerical == NumericalDamage.KnockedOut;

    /// <summary>True when the target is carrying a DMG marker and nothing worse.</summary>
    public bool TargetDamaged => !TargetDestroyed && Numerical == NumericalDamage.Damaged;

    /// <summary>True when the hit did nothing whatsoever to the target.</summary>
    public bool TargetUnharmed =>
        !TargetDestroyed && Numerical == NumericalDamage.None && !Immobilised && !TargetSystemsDown;
}

/// <summary>The dice-free plan for a damage resolution, or the reason no chit will be drawn.</summary>
/// <param name="DrawsChits">False when the weapon cannot harm this target at all.</param>
/// <param name="ChitCount">How many chits this hit draws.</param>
/// <param name="Validity">What the drawn chits will be allowed to count.</param>
/// <param name="ArmourValue">The armour value on the face that was hit.</param>
/// <param name="Reason">Why nothing will be drawn, or null when a draw is coming.</param>
public readonly record struct DamageSolution(
    bool DrawsChits,
    int ChitCount,
    ChitValidity Validity,
    int ArmourValue,
    string? Reason);

/// <summary>
/// Stage two: what a hit that connected actually did.
/// </summary>
/// <remarks>
/// <para>
/// Stage two is a draw rather than a roll. Chits come blind out of a pot, the ones this weapon is
/// allowed to count are totalled, and the total meets a flat armour number: short of it, nothing;
/// level with it, damaged; past it, gone.
/// </para>
/// <para>
/// The one thing that must not be got wrong: <em>invalid chits consume their draw slot</em>. They
/// are never discarded and redrawn. Every colour in the pot carries the same spread of values, so
/// the colours are identical in severity and differ only in how many of them there are and which of
/// them a weapon may count. That makes throwing draws away the <em>only</em> mechanism separating
/// one weapon from another. Filter the invalid chits out before drawing and every weapon in the game
/// silently becomes the same weapon, with no test failing to tell you.
/// </para>
/// <para>
/// Two asymmetries sit on top. A weapon that is ineffective against this target draws nothing at
/// all, because specials are not colour-gated and a weapon that cannot scratch a target must not be
/// able to blow it up. And a Systems Down chit belonging to the firer rewrites the whole shot after
/// the fact - it did not happen, and whatever else the same draw contained goes with it.
/// </para>
/// </remarks>
public static class DamageResolution
{
    /// <summary>
    /// Works out what a hit is going to draw, without drawing it.
    /// </summary>
    /// <param name="chitCount">
    /// How many chits this hit draws. An input, not something derived here: the base rule ties it to
    /// the weapon's size class, but guided missiles take it from the launcher, several weapons are
    /// flat regardless of class, and artillery multiplies it by the number of tubes. The engine
    /// stays ignorant of why it is drawing four.
    /// </param>
    /// <param name="validity">What the drawn chits are allowed to count, off the target's card.</param>
    /// <param name="armourValue">The armour value on the face that was hit.</param>
    /// <returns>The plan, or the reason nothing will be drawn.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validity"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The count or the armour value is negative.</exception>
    public static DamageSolution Solve(int chitCount, ChitValidity validity, int armourValue)
    {
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentOutOfRangeException.ThrowIfNegative(chitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(armourValue);

        if (validity.IsIneffective)
        {
            // Short-circuits here, before the pot is ever touched. This is not the same condition as
            // "no valid colours": a weapon with no valid colours still draws, still wastes every
            // slot, and can still turn up a special. A weapon the rules say cannot harm this target
            // must not be able to immobilise or destroy it by luck of the draw.
            return new DamageSolution(false, 0, validity, armourValue,
                "That weapon cannot harm that target, so no chits are drawn.");
        }

        return new DamageSolution(true, chitCount, validity, armourValue, null);
    }

    /// <summary>Draws for a hit and settles what it did.</summary>
    /// <param name="chitCount">How many chits this hit draws.</param>
    /// <param name="validity">What the drawn chits are allowed to count.</param>
    /// <param name="armourValue">The armour value on the face that was hit.</param>
    /// <param name="pot">The pot to draw from.</param>
    /// <returns>What the hit did.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validity"/> or <paramref name="pot"/> is null.</exception>
    public static DamageOutcome Resolve(int chitCount, ChitValidity validity, int armourValue, IChitPot pot) =>
        Resolve(Solve(chitCount, validity, armourValue), pot);

    /// <summary>Draws for a planned hit and settles what it did.</summary>
    /// <param name="solution">The plan from <see cref="Solve"/>.</param>
    /// <param name="pot">The pot to draw from.</param>
    /// <returns>What the hit did.</returns>
    /// <remarks>
    /// An ineffective weapon returns a no-effect outcome rather than throwing. Unlike stage one's
    /// refusal to roll a die that does not exist, this is a real result of a real hit - the round
    /// connected and did nothing - and the after-action log wants to say so.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pot"/> is null.</exception>
    public static DamageOutcome Resolve(DamageSolution solution, IChitPot pot)
    {
        ArgumentNullException.ThrowIfNull(pot);

        if (!solution.DrawsChits)
        {
            return Nothing(solution.ArmourValue);
        }

        // Drawing and scoring is shared with the infantry rules, because the one thing that must
        // never differ between them is that an invalid chit burns its slot.
        var tally = ChitDraw.From(solution.ChitCount, solution.Validity, pot);
        var total = tally.ValidTotal;

        var numerical = Compare(total, solution.ArmourValue);

        // Specials fire regardless of the total - a vehicle can shrug off the numbers and still be
        // immobilised - with one exception: once the numbers have already knocked it out, there is
        // nothing left for a special to do to it.
        var suppressSpecials = numerical == NumericalDamage.KnockedOut;
        var immobilised = !suppressSpecials && tally.Counted(ChitSpecial.Mobility);
        var targetSystemsDown = !suppressSpecials && tally.Counted(ChitSpecial.SystemsDownTarget);
        var boom = !suppressSpecials && tally.Counted(ChitSpecial.Boom);

        // The firer's Systems Down is checked against the raw draw rather than the surviving
        // specials, and it is checked last. It does not add to the result, it replaces it: the shot
        // is treated as never fired, so nothing that happened after the trigger was pulled happened
        // either. A knock-out cannot suppress it, because a knock-out that never occurred cannot
        // suppress anything.
        if (tally.Counted(ChitSpecial.SystemsDownFirer))
        {
            return new DamageOutcome(
                tally.Draw,
                ValidTotal: 0,
                solution.ArmourValue,
                NumericalDamage.None,
                Immobilised: false,
                TargetSystemsDown: false,
                FirerSystemsDown: true,
                CatastrophicKill: false,
                ShotNeverHappened: true);
        }

        return new DamageOutcome(
            tally.Draw,
            total,
            solution.ArmourValue,
            numerical,
            immobilised,
            targetSystemsDown,
            FirerSystemsDown: false,
            boom,
            ShotNeverHappened: false);
    }

    /// <summary>Where a total lands against an armour value.</summary>
    /// <param name="validTotal">The total of the counting chits.</param>
    /// <param name="armourValue">The armour value on the face that was hit.</param>
    /// <returns>What the numbers alone did.</returns>
    /// <remarks>
    /// Equal is a hit, not a bounce. That boundary is the whole texture of the game's armour values
    /// and it is deliberately not a "greater than or equal" shortcut.
    /// </remarks>
    public static NumericalDamage Compare(int validTotal, int armourValue) =>
        validTotal < armourValue ? NumericalDamage.None
        : validTotal == armourValue ? NumericalDamage.Damaged
        : NumericalDamage.KnockedOut;

    private static DamageOutcome Nothing(int armourValue) => new(
        Array.Empty<DrawnChit>(),
        ValidTotal: 0,
        armourValue,
        NumericalDamage.None,
        Immobilised: false,
        TargetSystemsDown: false,
        FirerSystemsDown: false,
        CatastrophicKill: false,
        ShotNeverHappened: false);
}

/// <summary>
/// What a DMG marker actually costs the vehicle carrying it.
/// </summary>
/// <remarks>
/// Damage is a flag, not a counter. A second damaging hit on an already-damaged vehicle does not
/// halve its movement again or shift its bands twice - these are the effects of being damaged, so
/// they are functions of the undamaged vehicle rather than transformations to apply repeatedly.
/// </remarks>
public static class DamagedEffects
{
    /// <summary>
    /// How halved movement rounds.
    /// </summary>
    /// <remarks>
    /// A choice, not a rule. The rules say half speed and leave odd numbers alone. Rounding towards
    /// zero is the pick, so that being damaged is never free.
    /// </remarks>
    public const MidpointRounding HalvedMovementRounding = MidpointRounding.ToZero;

    /// <summary>The movement a damaged vehicle has.</summary>
    /// <param name="baseMovement">The vehicle's undamaged movement.</param>
    /// <returns>Half of it, rounded by <see cref="HalvedMovementRounding"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The movement is negative.</exception>
    public static int Movement(int baseMovement)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseMovement);
        return (int)Math.Round(baseMovement / 2.0, HalvedMovementRounding);
    }

    /// <summary>
    /// The band a damaged vehicle's shot is actually treated as being in.
    /// </summary>
    /// <param name="band">The band the shot is really at.</param>
    /// <returns>
    /// One band worse, or null when there is no worse band left - a damaged vehicle cannot take a
    /// long-range shot at all.
    /// </returns>
    /// <remarks>
    /// Note what this is not: it is not a penalty applied to the firer's die on top of the range
    /// band. It moves the band itself, which is why the long shot disappears rather than becoming
    /// merely unlikely.
    /// </remarks>
    public static WeaponRangeBand? Band(WeaponRangeBand band) => band switch
    {
        WeaponRangeBand.Close => WeaponRangeBand.Medium,
        WeaponRangeBand.Medium => WeaponRangeBand.Long,
        _ => null,
    };

    /// <summary>Whether a damaged vehicle can shoot at a given band at all.</summary>
    /// <param name="band">The band the shot is really at.</param>
    /// <returns>False for a long-range shot, true otherwise.</returns>
    public static bool CanFireAt(WeaponRangeBand band) => Band(band) is not null;
}
