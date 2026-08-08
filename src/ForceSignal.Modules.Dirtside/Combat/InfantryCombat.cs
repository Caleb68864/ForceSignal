using System.Collections.ObjectModel;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>How much of a unit actually fired for effect.</summary>
public enum FireEffectiveness
{
    /// <summary>Nobody fired to any purpose. The target is still shaken up.</summary>
    Ineffective = 0,

    /// <summary>Half the eligible stands fired, rounded up.</summary>
    Partial = 1,

    /// <summary>Every eligible stand fired.</summary>
    Full = 2,
}

/// <summary>The check a unit makes before any chit is drawn, worked out but not yet rolled.</summary>
/// <param name="Die">The die the unit throws, after any Under Fire reduction.</param>
/// <param name="Leadership">The number the roll is measured against.</param>
/// <param name="EligibleStands">How many stands could fire if the unit were fully effective.</param>
/// <param name="WasReducedByUnderFire">True when an Under Fire marker cost the unit a die step.</param>
public readonly record struct FireEffectivenessSolution(
    QualityDie Die,
    int Leadership,
    int EligibleStands,
    bool WasReducedByUnderFire);

/// <summary>What the fire-effectiveness check came to.</summary>
/// <param name="Solution">The check that was made.</param>
/// <param name="Roll">What the unit rolled.</param>
/// <param name="Result">Which of the three tiers it reached.</param>
/// <param name="StandsFiring">How many stands may now draw chits.</param>
public readonly record struct FireEffectivenessCheck(
    FireEffectivenessSolution Solution,
    int Roll,
    FireEffectiveness Result,
    int StandsFiring)
{
    /// <summary>
    /// Whether the target ends up Under Fire. Static, because it does not depend on the check at
    /// all: any firefight marks the target however badly it went, which is why firing at a target
    /// you have no real hope of hurting is still worth doing.
    /// </summary>
    public static bool TargetIsUnderFire => true;
}

/// <summary>
/// One infantry stand, as the combat rules need it. Everything here is off the player's own roster.
/// </summary>
/// <remarks>
/// The two chit counts are separate fields rather than one, and that is deliberate. A firefight and a
/// close assault both group troops into "these draw more, those draw fewer", but they do <em>not</em>
/// group them the same way - a stand can sit in the larger bracket for one and the smaller for the
/// other. One shared number would be right most of the time, which is the worst way for a rule to be
/// wrong. Keeping them apart makes the difference structural instead of remembered.
/// </remarks>
/// <param name="Id">Whatever the caller calls this stand. Only ever compared, never parsed.</param>
/// <param name="FirefightChits">How many chits this stand draws in a ranged firefight.</param>
/// <param name="AssaultChits">How many chits this stand draws in a close assault.</param>
/// <param name="KillThreshold">
/// The valid total needed to remove this stand. Reaching it is enough - see
/// <see cref="InfantryCombat.IsDestroyed"/>.
/// </param>
public sealed record InfantryStand(string Id, int FirefightChits, int AssaultChits, int KillThreshold);

/// <summary>What a handful of small-arms chits is being drawn against.</summary>
/// <param name="Id">Whatever the caller calls it.</param>
/// <param name="KillThreshold">The valid total that removes it.</param>
/// <param name="IsSoftskinVehicle">
/// True for an unarmoured vehicle being shot at as though it were a stand. This single flag decides
/// whether the special chits do anything, so it must not be guessed at.
/// </param>
public sealed record SmallArmsTarget(string Id, int KillThreshold, bool IsSoftskinVehicle = false)
{
    /// <summary>An infantry stand. Specials never count.</summary>
    /// <param name="id">Whatever the caller calls it.</param>
    /// <param name="killThreshold">The valid total that removes it.</param>
    /// <returns>The target.</returns>
    public static SmallArmsTarget Stand(string id, int killThreshold) => new(id, killThreshold);

    /// <summary>
    /// An unarmoured vehicle, shot at as if it were a stand. Specials count.
    /// </summary>
    /// <param name="id">Whatever the caller calls it.</param>
    /// <param name="killThreshold">
    /// Its toughness expressed as a stand's. The rules give a softskin the toughness of the sturdiest
    /// class of infantry rather than a number of its own, so this is the caller's to fill in.
    /// </param>
    /// <returns>The target.</returns>
    public static SmallArmsTarget Softskin(string id, int killThreshold) =>
        new(id, killThreshold, IsSoftskinVehicle: true);
}

/// <summary>One stand's draw, kept entirely to itself.</summary>
/// <param name="FirerId">Which stand drew.</param>
/// <param name="TargetId">What it drew against.</param>
/// <param name="Tally">Every chit it drew, and the total that counted.</param>
/// <param name="KillThreshold">The number it had to reach.</param>
/// <param name="Destroyed">True when this one draw was enough on its own.</param>
/// <param name="Immobilised">A softskin that will not be driving away.</param>
/// <param name="TargetSystemsDown">A softskin whose electronics have gone.</param>
/// <param name="CatastrophicKill">A softskin that went up altogether.</param>
public sealed record StandFireResult(
    string FirerId,
    string TargetId,
    ChitTally Tally,
    int KillThreshold,
    bool Destroyed,
    bool Immobilised,
    bool TargetSystemsDown,
    bool CatastrophicKill)
{
    /// <summary>True when this draw did anything at all.</summary>
    public bool HadAnyEffect => Destroyed || Immobilised || TargetSystemsDown || CatastrophicKill;
}

/// <summary>Every firing stand's draw in one firefight, side by side and never added together.</summary>
/// <param name="Effectiveness">The unit's check, or null when the caller ran it separately.</param>
/// <param name="Draws">One entry per stand that fired.</param>
public sealed record FirefightResult(
    FireEffectivenessCheck? Effectiveness,
    IReadOnlyList<StandFireResult> Draws)
{
    /// <summary>
    /// How many target stands this firefight removed: one per successful draw.
    /// </summary>
    /// <remarks>
    /// A count of winning draws, never a total of points. The distinction is the whole rule.
    /// </remarks>
    public int Casualties => Draws.Count(d => d.Destroyed);
}

/// <summary>What became of infantry riding a vehicle that was hit.</summary>
/// <param name="Rolls">One roll per stand aboard, or empty when no roll was called for.</param>
/// <param name="LostOn">The number a roll had to reach to kill a stand, or zero.</param>
/// <param name="Lost">How many stands were killed.</param>
/// <param name="AllKilled">True when the vehicle went up and took everyone with it.</param>
public sealed record MountedCasualties(
    IReadOnlyList<int> Rolls,
    int LostOn,
    int Lost,
    bool AllKilled);

/// <summary>
/// Infantry, who are stands rather than vehicles: whole or gone, with nothing in between.
/// </summary>
/// <remarks>
/// <para>
/// The shape is different from vehicle fire in three ways that matter. There is no to-hit roll at
/// all - a unit rolls once to see how much of itself is actually shooting, and after that it is
/// chits straight away. There is no damaged state, so the comparison is <em>reach or exceed</em>
/// rather than the vehicles' three-way split at the exact boundary. And every firing stand is
/// resolved on its own.
/// </para>
/// <para>
/// That last one is the trap. Chits are <b>never pooled across stands</b>. Four stands drawing two
/// chits each is four separate draws each measured against the kill total on its own, which is a
/// very different thing from one draw of eight measured once - the pooled version would turn a
/// scattering of near-misses into a kill and would make a big unit far deadlier than the rules
/// intend. It is exactly the shape of the twin-mount mistake, and it is guarded the same way: the
/// engine returns a list of results and never a sum.
/// </para>
/// </remarks>
public static class InfantryCombat
{
    /// <summary>
    /// Works out the fire-effectiveness check, without rolling it.
    /// </summary>
    /// <param name="quality">The unit's quality die.</param>
    /// <param name="leadership">The unit's leadership number.</param>
    /// <param name="eligibleStands">How many stands are in range and able to join a firefight.</param>
    /// <param name="underFire">True when the unit is carrying an Under Fire marker.</param>
    /// <returns>The die to throw and the numbers to measure it against.</returns>
    /// <remarks>
    /// Nothing special is needed to honour "a unit with the lowest leadership can never be
    /// ineffective". Ineffective means rolling <em>below</em> leadership, and no die can roll below
    /// one, so it falls out of the comparison rather than being a case to remember.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The leadership or the stand count is negative.</exception>
    public static FireEffectivenessSolution SolveFireEffectiveness(
        QualityDie quality,
        int leadership,
        int eligibleStands,
        bool underFire = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(leadership);
        ArgumentOutOfRangeException.ThrowIfNegative(eligibleStands);

        // Being under fire costs a step. Closed rather than open: there is no opponent here to hand
        // the overflow to, so a unit already at the bottom of the ladder simply stays there.
        var die = underFire ? QualityDice.ShiftClosed(quality, -1) : quality;
        return new FireEffectivenessSolution(die, leadership, eligibleStands, underFire);
    }

    /// <summary>Rolls the fire-effectiveness check.</summary>
    /// <param name="solution">The check to roll.</param>
    /// <param name="roller">Die source.</param>
    /// <returns>Which tier the unit reached and how many stands that lets fire.</returns>
    /// <remarks>
    /// Which particular stands make up the firing half is the player's choice, not this engine's -
    /// the rules let a support-weapon team always be one of them, and that is a decision at the
    /// table rather than an algorithm.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="roller"/> is null.</exception>
    public static FireEffectivenessCheck RollFireEffectiveness(
        FireEffectivenessSolution solution,
        IQualityDiceRoller roller)
    {
        ArgumentNullException.ThrowIfNull(roller);

        var roll = roller.Roll(solution.Die);
        var result = roll < solution.Leadership ? FireEffectiveness.Ineffective
            : roll < solution.Leadership * 2 ? FireEffectiveness.Partial
            : FireEffectiveness.Full;

        var firing = result switch
        {
            FireEffectiveness.Ineffective => 0,

            // Half, rounded up, so a lone stand still gets to shoot.
            FireEffectiveness.Partial => (solution.EligibleStands + 1) / 2,
            _ => solution.EligibleStands,
        };

        return new FireEffectivenessCheck(solution, roll, result, firing);
    }

    /// <summary>
    /// Draws for one stand against one target, and settles it on its own.
    /// </summary>
    /// <remarks>
    /// The primitive everything else here is built from: a firefight, a close-assault exchange, a
    /// vehicle turning its gun on infantry without any to-hit roll, and a hull charge going off at
    /// something that got too close are all this, called once per drawing stand with a different
    /// count and a different validity row.
    /// </remarks>
    /// <param name="firerId">Whichever stand or weapon is drawing.</param>
    /// <param name="chitCount">How many chits it draws. Situational, so it is an input.</param>
    /// <param name="validity">
    /// What the chits may count, off the card. Whether specials count is <em>not</em> taken from
    /// here - see the remarks on <see cref="Against"/>.
    /// </param>
    /// <param name="target">What is being shot at.</param>
    /// <param name="pot">The pot. Whole again before the next stand draws.</param>
    /// <returns>This one draw, settled.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static StandFireResult ResolveDraw(
        string firerId,
        int chitCount,
        ChitValidity validity,
        SmallArmsTarget target,
        IChitPot pot)
    {
        ArgumentNullException.ThrowIfNull(firerId);
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(pot);

        var tally = ChitDraw.From(chitCount, Against(validity, target), pot);
        var destroyed = IsDestroyed(tally.ValidTotal, target.KillThreshold);

        // Same suppression as vehicle damage, for the same reason: there is nothing left for a
        // special to do to something that the numbers have already accounted for.
        var specials = !destroyed;

        return new StandFireResult(
            firerId,
            target.Id,
            tally,
            target.KillThreshold,
            destroyed,
            specials && tally.Counted(ChitSpecial.Mobility),
            specials && tally.Counted(ChitSpecial.SystemsDownTarget),
            specials && tally.Counted(ChitSpecial.Boom));
    }

    /// <summary>
    /// Resolves a firefight: every stand allowed to fire draws for itself.
    /// </summary>
    /// <param name="firingStands">The stands that the effectiveness check allowed to fire.</param>
    /// <param name="validity">What their chits may count, set by the target's cover.</param>
    /// <param name="target">The element being fired at.</param>
    /// <param name="pot">The pot.</param>
    /// <param name="effectiveness">The check that produced this list, carried through for the log.</param>
    /// <returns>One result per firing stand, and nothing summed.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is null.</exception>
    public static FirefightResult ResolveFirefight(
        IEnumerable<InfantryStand> firingStands,
        ChitValidity validity,
        SmallArmsTarget target,
        IChitPot pot,
        FireEffectivenessCheck? effectiveness = null)
    {
        ArgumentNullException.ThrowIfNull(firingStands);

        var draws = new List<StandFireResult>();
        foreach (var stand in firingStands)
        {
            // Each stand's own count, each stand's own draw, each stand's own comparison. The pot is
            // whole again between them, so one stand cannot use up another's luck.
            draws.Add(ResolveDraw(stand.Id, stand.FirefightChits, validity, target, pot));
        }

        return new FirefightResult(effectiveness, new ReadOnlyCollection<StandFireResult>(draws));
    }

    /// <summary>
    /// Forces the specials flag to match what is being shot at, whatever the caller supplied.
    /// </summary>
    /// <param name="validity">The colour validity off the card.</param>
    /// <param name="target">What is being shot at.</param>
    /// <returns>The same validity with the specials question answered by the target.</returns>
    /// <remarks>
    /// <para>
    /// This is decided here rather than trusted to the caller because it runs in both directions and
    /// both are load-bearing. Against infantry the specials do nothing - a stand cannot be
    /// immobilised or have its electronics knocked out, and letting them through would kill stands
    /// that the numbers never touched. Against an unarmoured vehicle they <em>do</em> count, and that
    /// is the only route by which rifle fire wrecks a vehicle at all: a truck can be immobilised or
    /// go up altogether from small arms that could never total enough points to matter.
    /// </para>
    /// <para>
    /// A caller who fills the card in from the wrong row would otherwise silently get one of those
    /// two wrong, and neither failure looks like a bug from the outside.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static ChitValidity Against(ChitValidity validity, SmallArmsTarget target)
    {
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(target);

        return validity with { SpecialsCount = target.IsSoftskinVehicle };
    }

    /// <summary>
    /// Turns a colour row into one an infantry anti-vehicle rocket draws on.
    /// </summary>
    /// <param name="validity">The colour validity off the card.</param>
    /// <returns>The same row with the specials switched on.</returns>
    /// <remarks>
    /// The rocket's whole point. Numerically it is two chits and will rarely out-total real armour,
    /// but the specials count, so a cheap disposable launcher can immobilise or destroy a tank it
    /// could never penetrate. Switching them off here would make the weapon almost pointless while
    /// still looking as though it worked.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="validity"/> is null.</exception>
    public static ChitValidity AntiVehicleRocket(ChitValidity validity)
    {
        ArgumentNullException.ThrowIfNull(validity);
        return validity with { SpecialsCount = true };
    }

    /// <summary>Whether a total is enough to remove a stand.</summary>
    /// <param name="validTotal">The total of the counting chits.</param>
    /// <param name="killThreshold">The number needed.</param>
    /// <returns>True when the stand is gone.</returns>
    /// <remarks>
    /// Reaching it is enough. This is the opposite convention to vehicle armour, where equalling the
    /// number is only a DAMAGED result - infantry have no damaged state to land on, so the boundary
    /// has to fall the other way. Writing them as two separate rules rather than one shared helper is
    /// the point: they look alike and are not.
    /// </remarks>
    public static bool IsDestroyed(int validTotal, int killThreshold) => validTotal >= killThreshold;

    /// <summary>
    /// Works out what happened to stands riding a vehicle that has just been hit.
    /// </summary>
    /// <param name="outcome">What the hit did to the transport.</param>
    /// <param name="riderStands">How many stands were aboard.</param>
    /// <param name="roller">Die source.</param>
    /// <param name="transportCrashed">True when an aircraft came down rather than merely stopping.</param>
    /// <returns>The rolls made and the stands lost.</returns>
    /// <remarks>
    /// The ladder is: a shot that never happened or a transport merely immobilised or blinded costs
    /// the passengers nothing; a damaged one shakes a few of them out; a knocked-out one is far
    /// worse; and a catastrophic kill or a crash takes everybody, with no roll at all.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The stand count is negative.</exception>
    public static MountedCasualties ResolveRiders(
        DamageOutcome outcome,
        int riderStands,
        IQualityDiceRoller roller,
        bool transportCrashed = false)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(roller);
        ArgumentOutOfRangeException.ThrowIfNegative(riderStands);

        if (transportCrashed || (!outcome.ShotNeverHappened && outcome.CatastrophicKill))
        {
            // No roll: there is nothing to survive.
            return new MountedCasualties(Array.Empty<int>(), 0, riderStands, true);
        }

        if (outcome.ShotNeverHappened)
        {
            return new MountedCasualties(Array.Empty<int>(), 0, 0, false);
        }

        var lostOn = outcome.Numerical switch
        {
            NumericalDamage.KnockedOut => RidersLostOnKnockedOut,
            NumericalDamage.Damaged => RidersLostOnDamaged,

            // Immobilised or blinded, but intact: the passengers simply get out.
            _ => 0,
        };

        if (lostOn == 0)
        {
            return new MountedCasualties(Array.Empty<int>(), 0, 0, false);
        }

        var rolls = new List<int>(riderStands);
        var lost = 0;
        for (var stand = 0; stand < riderStands; stand++)
        {
            var roll = roller.Roll(QualityDie.D6);
            rolls.Add(roll);
            if (roll >= lostOn)
            {
                lost++;
            }
        }

        return new MountedCasualties(
            new ReadOnlyCollection<int>(rolls), lostOn, lost, lost == riderStands && riderStands > 0);
    }

    /// <summary>A rider on a merely damaged transport is lost on this or better.</summary>
    public const int RidersLostOnDamaged = 6;

    /// <summary>A rider on a knocked-out transport is lost on this or better.</summary>
    public const int RidersLostOnKnockedOut = 3;
}
