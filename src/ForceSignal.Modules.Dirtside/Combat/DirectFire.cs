using System.Collections.ObjectModel;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>The element doing the shooting, as far as a shot needs to know.</summary>
/// <param name="Id">Whatever the caller calls this element. Only ever compared, never parsed.</param>
/// <param name="FireControl">Its gunnery.</param>
/// <param name="MovedOverHalf">True when it has moved, or will move, more than half its movement.</param>
/// <param name="IsDamaged">True when it is carrying a DMG marker.</param>
public sealed record FiringElement(
    string Id,
    FireControlLevel FireControl,
    bool MovedOverHalf = false,
    bool IsDamaged = false);

/// <summary>The element being shot at, as far as a shot needs to know.</summary>
/// <param name="Id">Whatever the caller calls this element.</param>
/// <param name="Signature">How loud it is, 1 for the largest down to 5 for the smallest.</param>
/// <param name="ArmourValue">The armour on the face that is being hit.</param>
/// <param name="Posture">What it is doing about being shot at.</param>
public sealed record TargetElement(
    string Id,
    int Signature,
    int ArmourValue,
    DefensivePosture Posture = DefensivePosture.None);

/// <summary>The weapon being fired, as far as a shot needs to know.</summary>
/// <param name="ChitCount">
/// How many chits each hit draws. An input, because the base rule ties it to size class but
/// launchers, flat-rated weapons and multi-tube artillery all set it some other way.
/// </param>
/// <param name="Validity">What the drawn chits may count, per band, off the record card.</param>
/// <param name="Barrels">
/// How many weapons of the same type and class are in the mount. A mount fires together and only
/// ever at one target, which is why this belongs to the weapon rather than to the declaration.
/// </param>
public sealed record WeaponMount(int ChitCount, WeaponValidityCard Validity, int Barrels = 1);

/// <summary>
/// One firing element's binding declaration of what it is shooting at.
/// </summary>
/// <remarks>
/// The whole point of this type is that it is made before any dice are thrown and cannot be revised
/// afterwards. It names the target rather than describing a search for one.
/// </remarks>
/// <param name="Firer">Who is shooting.</param>
/// <param name="Weapon">What they are shooting with.</param>
/// <param name="Target">Who they have designated, before any dice.</param>
/// <param name="MeasuredBand">The band the tape says the shot falls in.</param>
public sealed record FireDeclaration(
    FiringElement Firer,
    WeaponMount Weapon,
    TargetElement Target,
    WeaponRangeBand MeasuredBand);

/// <summary>Why a declared shot never got as far as the dice.</summary>
public enum ShotRefusal
{
    /// <summary>It was fired.</summary>
    None = 0,

    /// <summary>The target was already destroyed by an earlier shot, and the declaration is binding.</summary>
    TargetAlreadyDestroyed = 1,

    /// <summary>The firer's own systems went down earlier in this volley.</summary>
    FirerSystemsDown = 2,

    /// <summary>A damaged firer cannot reach that band at all.</summary>
    BandOutOfReach = 3,

    /// <summary>There is no firer die left on the ladder.</summary>
    NoDieLeft = 4,

    /// <summary>
    /// The game's rules profile does not say which die one of the three sides of this shot rolls.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="NoDieLeft"/>, which is a rule about the shot. This one is a gap in
    /// what the players entered, and the only refusal here that a table fixes by typing rather than
    /// by moving a model.
    /// </remarks>
    NoDieOnTheProfile = 5,
}

/// <summary>
/// One declared shot, resolved end to end, with everything a table would want to see.
/// </summary>
/// <param name="Declaration">What was declared, before any dice.</param>
/// <param name="Band">The band the shot resolved at, and why it is not the measured one.</param>
/// <param name="Refusal">Why nothing was rolled, or <see cref="ShotRefusal.None"/>.</param>
/// <param name="Reason">The refusal in words, or null.</param>
/// <param name="Attempts">One entry per barrel, sharing the target's single roll.</param>
/// <param name="Damage">One entry per barrel that hit, in barrel order.</param>
public sealed record ShotResult(
    FireDeclaration Declaration,
    EffectiveRangeBand Band,
    ShotRefusal Refusal,
    string? Reason,
    IReadOnlyList<HitAttempt> Attempts,
    IReadOnlyList<DamageOutcome> Damage)
{
    /// <summary>True when dice were actually thrown.</summary>
    public bool WasFired => Refusal == ShotRefusal.None;

    /// <summary>How many barrels beat the target's score.</summary>
    public int Hits => Attempts.Count(a => a.IsHit);

    /// <summary>True when this shot left the target out of the battle.</summary>
    public bool TargetDestroyed => Damage.Any(d => d.TargetDestroyed);

    /// <summary>True when the firer's own systems went down resolving this shot.</summary>
    public bool FirerSystemsDown => Damage.Any(d => d.FirerSystemsDown);
}

/// <summary>Every declared shot in one player's firing, resolved in declaration order.</summary>
/// <param name="Shots">The shots, in the order they were declared.</param>
public sealed record DirectFireVolley(IReadOnlyList<ShotResult> Shots)
{
    /// <summary>Every element left out of the battle by this firing, first destruction first.</summary>
    public IReadOnlyList<string> Destroyed =>
        [.. Shots.Where(s => s.TargetDestroyed).Select(s => s.Declaration.Target.Id).Distinct(StringComparer.Ordinal)];

    /// <summary>Every shot that was declared and then never fired.</summary>
    public IReadOnlyList<ShotResult> Wasted => [.. Shots.Where(s => !s.WasFired)];
}

/// <summary>
/// A whole piece of direct fire, from the binding declaration through to the chits on the table.
/// </summary>
/// <remarks>
/// <para>
/// The order here is the rule, not an implementation detail. A player designates a target for every
/// firing element <em>before</em> any dice are rolled, and the declaration is binding: three shots
/// at one element, the first of which kills it, are three shots spent - the other two are wasted and
/// may not be re-pointed at a fresh target. That is a real cost paid for the information the player
/// did not have, and an engine that let the second shot look around for something else alive would
/// be quietly playing a different and much more forgiving game.
/// </para>
/// <para>
/// So the volley takes the declarations as a list, resolves them strictly in order, and refuses -
/// rather than redirects - any shot whose target is already gone.
/// </para>
/// <para>
/// The other thing this class exists to do is compute the effective range band exactly once and hand
/// the same value to both stages. See <see cref="EffectiveRangeBand"/> for why that matters more
/// than it looks.
/// </para>
/// </remarks>
public static class DirectFire
{
    /// <summary>
    /// Resolves one declared shot in isolation, dice and chits and all.
    /// </summary>
    /// <param name="profile">The dice this game's players entered off their own rulebook.</param>
    /// <param name="declaration">The binding declaration.</param>
    /// <param name="roller">Die source.</param>
    /// <param name="pot">The chit pot. Every hit draws from it independently.</param>
    /// <returns>The shot, whole, with its audit trail.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The mount has fewer than one barrel.</exception>
    public static ShotResult Resolve(
        DirtsideRulesProfile profile,
        FireDeclaration declaration,
        IQualityDiceRoller roller,
        IChitPot pot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(roller);
        ArgumentNullException.ThrowIfNull(pot);
        ArgumentOutOfRangeException.ThrowIfLessThan(declaration.Weapon.Barrels, 1);

        // Once, here, and then handed to both stages. Never recomputed downstream.
        var band = EffectiveRangeBand.For(declaration.MeasuredBand, declaration.Firer.IsDamaged);
        if (!band.CanFire)
        {
            return Refused(declaration, band, ShotRefusal.BandOutOfReach,
                "A damaged vehicle cannot take a long-range shot at all.");
        }

        var solution = HitResolution.Solve(
            profile,
            declaration.Firer.FireControl,
            band.Band,
            declaration.Target.Signature,
            declaration.Target.Posture,
            declaration.Firer.MovedOverHalf);

        if (!solution.CanFire)
        {
            // Two different refusals arrive here wearing the same shape: a ladder that ran out, and
            // a row the profile does not carry. They are kept apart, because a table reading "the
            // game has no die for this" needs to know it is a gap in what they entered and not a
            // rule about their sight.
            return Refused(
                declaration,
                band,
                solution.IsMissingFromProfile ? ShotRefusal.NoDieOnTheProfile : ShotRefusal.NoDieLeft,
                solution.Reason);
        }

        var attempts = RollMount(solution, declaration.Weapon.Barrels, roller);

        // Validity is read at the band the shot *resolved* at, not the band it was measured at. For
        // a weapon whose colours narrow with range, a DMG marker on the firer therefore changes what
        // its hits can do, not merely how often it lands them.
        var validity = declaration.Weapon.Validity.At(band.Band);

        var damage = new List<DamageOutcome>(attempts.Count);
        foreach (var attempt in attempts)
        {
            if (!attempt.IsHit)
            {
                continue;
            }

            // Each hit is its own resolution, so each one is its own draw with the pot whole again
            // in between. A twin mount landing both barrels is two draws of the weapon's size, never
            // one draw of twice it.
            //
            // Every barrel that hit is resolved, including the ones landing on a target the first
            // barrel already destroyed. A mount fires together, so its barrels are simultaneous -
            // the binding-declaration waste rule is about separate declared shots, and applying it
            // inside a single mount would make the second barrel of a twin turret behave as though
            // it had waited to see what the first one did.
            damage.Add(DamageResolution.Resolve(
                declaration.Weapon.ChitCount, validity, declaration.Target.ArmourValue, pot));
        }

        return new ShotResult(
            declaration,
            band,
            ShotRefusal.None,
            null,
            attempts,
            new ReadOnlyCollection<DamageOutcome>(damage));
    }

    /// <summary>
    /// Resolves a whole piece of firing: every declaration, in the order it was declared.
    /// </summary>
    /// <param name="profile">The dice this game's players entered off their own rulebook.</param>
    /// <param name="declarations">
    /// Every firing element's designated target, all of them made before this call. The order is the
    /// order the player chose to resolve them in, and it is load-bearing.
    /// </param>
    /// <param name="roller">Die source.</param>
    /// <param name="pot">The chit pot.</param>
    /// <returns>Every shot, fired or wasted.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static DirectFireVolley Resolve(
        DirtsideRulesProfile profile,
        IEnumerable<FireDeclaration> declarations,
        IQualityDiceRoller roller,
        IChitPot pot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(roller);
        ArgumentNullException.ThrowIfNull(pot);

        var destroyed = new HashSet<string>(StringComparer.Ordinal);
        var silenced = new HashSet<string>(StringComparer.Ordinal);
        var shots = new List<ShotResult>();

        foreach (var declaration in declarations)
        {
            if (destroyed.Contains(declaration.Target.Id))
            {
                // The binding declaration, in one branch. The shot is spent, not redirected.
                shots.Add(Refused(
                    declaration,
                    EffectiveRangeBand.For(declaration.MeasuredBand, declaration.Firer.IsDamaged),
                    ShotRefusal.TargetAlreadyDestroyed,
                    $"{declaration.Target.Id} was already destroyed, and the declaration is binding: "
                    + "the shot is wasted rather than re-pointed."));
                continue;
            }

            if (silenced.Contains(declaration.Firer.Id))
            {
                // A firer whose own systems have gone down may take no further combat action. Not
                // strictly part of the declaration rule, but it lands in the same place: a shot that
                // was declared and cannot now be taken.
                shots.Add(Refused(
                    declaration,
                    EffectiveRangeBand.For(declaration.MeasuredBand, declaration.Firer.IsDamaged),
                    ShotRefusal.FirerSystemsDown,
                    $"{declaration.Firer.Id} has systems down and may take no further combat action."));
                continue;
            }

            var shot = Resolve(profile, declaration, roller, pot);
            shots.Add(shot);

            if (shot.TargetDestroyed)
            {
                destroyed.Add(declaration.Target.Id);
            }

            if (shot.FirerSystemsDown)
            {
                silenced.Add(declaration.Firer.Id);
            }
        }

        return new DirectFireVolley(new ReadOnlyCollection<ShotResult>(shots));
    }

    /// <summary>
    /// Rolls a mount: the target throws once, and every barrel throws the firer's die against that
    /// single score.
    /// </summary>
    /// <remarks>
    /// The target rolling once is the whole shape of the multiple mount. Rerolling its dice per
    /// barrel would make each barrel an independent duel and would wash out the streaky quality of a
    /// twin mount against a target that happened to roll well.
    /// </remarks>
    private static ReadOnlyCollection<HitAttempt> RollMount(
        ShotSolution solution,
        int barrels,
        IQualityDiceRoller roller)
    {
        var primaryRoll = roller.Roll(solution.TargetPrimaryDie);
        int? secondaryRoll = solution.TargetSecondaryDie is { } secondary ? roller.Roll(secondary) : null;
        var targetScore = Math.Max(primaryRoll, secondaryRoll ?? 0);

        var attempts = new List<HitAttempt>(barrels);
        for (var barrel = 0; barrel < barrels; barrel++)
        {
            var firerRoll = roller.Roll(solution.FirerDie);
            attempts.Add(new HitAttempt(
                solution,
                firerRoll,
                primaryRoll,
                secondaryRoll,
                OpposedRolls.Wins(firerRoll, targetScore)));
        }

        return new ReadOnlyCollection<HitAttempt>(attempts);
    }

    private static ShotResult Refused(
        FireDeclaration declaration,
        EffectiveRangeBand band,
        ShotRefusal refusal,
        string? reason) => new(
        declaration,
        band,
        refusal,
        reason,
        Array.Empty<HitAttempt>(),
        Array.Empty<DamageOutcome>());
}
