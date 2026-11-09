using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;

namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>
/// Every die and number a Dirtside game is played against, gathered in one place and supplied by
/// the player rather than shipped with this app.
/// </summary>
/// <remarks>
/// <para>
/// The same argument as <c>RulesProfile</c> one module over, and the same shape: the engine owns the
/// <em>procedures</em> - which die is thrown against which, that the target keeps the better of its
/// two dice, that a band and a hurried shot move the firer down the ladder - and the player owns
/// every <em>number</em> those procedures read, off their own rulebook.
/// </para>
/// <para>
/// This module used to hold three tables written into <c>HitResolution</c>: a fire-control level was
/// worth a D6, a D8 or a D10; a posture was worth a D6 through a D12; and a signature indexed the
/// quality ladder directly, so a signature of 1 was a D12. Those are readings off somebody's card,
/// and the module's whole combat model rested on them. They are here now, and this app ships none of
/// them: <see cref="Empty"/> is blank rather than typical, and there is deliberately no
/// <c>Parse</c> that conjures a set from a name.
/// </para>
/// <para>
/// <b>A missing entry is a refusal, never a substitution.</b> Every lookup here answers null when
/// the player has not entered that row, and every caller turns that null into a refusal that names
/// the row - see <see cref="HitResolution.Solve"/>. A profile that quietly filled in a plausible die
/// would be the same defect as the tables this type replaced, wearing a better hat.
/// </para>
/// <para>
/// <b>Only the rows a game actually reads are needed.</b> Nothing here requires a complete profile,
/// because a complete profile is not a thing this app can define: a table fielding nothing but
/// basic-gunnery infantry never asks what a superior sight rolls, and refusing to start their game
/// until they had entered one would be this app inventing a requirement instead of a number. The
/// over-strict failure has been paid for twice in this project already.
/// </para>
/// </remarks>
/// <param name="FireControlDice">What each gunnery level rolls at its own range band.</param>
/// <param name="PostureDice">What each posture is worth as a second defensive die.</param>
/// <param name="SignatureDice">What each signature rolls, by the number printed on the card.</param>
/// <param name="SystemsDownRecoveryDie">The die a crew throws to get a Systems Down marker off.</param>
/// <param name="SystemsDownRecoveryRoll">The number that throw has to reach without backup systems.</param>
/// <param name="SystemsDownRecoveryRollWithBackup">The number it has to reach with them.</param>
/// <param name="AreaDefenceReach">
/// How far an area-defence system reaches to intercept, in the players' own distance units.
/// </param>
/// <param name="LeadershipValues">
/// Which numbers count as Leadership Values in this game, or unentered when the players have not
/// said. This module used to hold no opinion at all: a command marker took whatever number arrived,
/// and a probe put 99 and -4 on one. The same entry, on the same terms, as StarGrunt's - one engine
/// reading a number off a page cannot be stricter about it than the other, and both feed the one
/// <see cref="ForceSignal.Modules.GroundCombat.Morale.ConfidenceLadder"/>.
/// </param>
/// <param name="RungsLostUnderFire">
/// How many rungs an Under Fire marker costs a fire-effectiveness check, or null when the players have
/// not said. Null rather than zero, because no cost at all is an answer a table can give.
/// </param>
/// <param name="FirerWalk">
/// How far the firer's die moves for the range band and for a hurried shot, or null when the players
/// have not said - in which case the walk this app has always played is used and
/// <see cref="PlaysTheBuiltInFirerWalk"/> says so. See <see cref="Combat.FirerWalk"/>.
/// </param>
public sealed record DirtsideRulesProfile(
    ImmutableDictionary<FireControlLevel, QualityDie> FireControlDice,
    ImmutableDictionary<DefensivePosture, QualityDie> PostureDice,
    ImmutableDictionary<int, QualityDie> SignatureDice,
    QualityDie? SystemsDownRecoveryDie = null,
    int SystemsDownRecoveryRoll = 0,
    int SystemsDownRecoveryRollWithBackup = 0,
    int AreaDefenceReach = 0,
    LeadershipRange LeadershipValues = default,
    int? RungsLostUnderFire = null,
    FirerWalk? FirerWalk = null)
{
    /// <summary>
    /// A profile with nothing entered. What a game created without one plays on, and what every
    /// stored game written before profiles existed reads back as.
    /// </summary>
    /// <remarks>
    /// Blank, not typical. A game on this profile is a game that can be set up, moved around and
    /// talked about, and whose first shot is refused with the name of the row it is missing. That is
    /// the deliberate trade: retiring such a game outright would take the table's whole evening, and
    /// inventing a die for it would take the thing this policy exists to protect.
    /// </remarks>
    public static DirtsideRulesProfile Empty { get; } = new(
        ImmutableDictionary<FireControlLevel, QualityDie>.Empty,
        ImmutableDictionary<DefensivePosture, QualityDie>.Empty,
        ImmutableDictionary<int, QualityDie>.Empty);

    /// <summary>True when nothing whatever has been entered.</summary>
    public bool IsBlank =>
        FireControlDice.IsEmpty
        && PostureDice.IsEmpty
        && SignatureDice.IsEmpty
        && SystemsDownRecoveryDie is null
        && InterceptionReach is null
        && LeadershipValues.IsBlank
        && RungsLostUnderFire is null
        && FirerWalk is null;

    /// <summary>
    /// The walk a shot is solved with: the players' entry, or <see cref="Combat.FirerWalk.BuiltIn"/>
    /// when they have not made one.
    /// </summary>
    public FirerWalk FirerWalkOrBuiltIn => FirerWalk ?? Combat.FirerWalk.BuiltIn;

    /// <summary>
    /// True when the players have not entered a firer walk, so every shot in this game walks the app's
    /// own rungs. Reported rather than refused - see <see cref="Combat.FirerWalk"/> for why.
    /// </summary>
    public bool PlaysTheBuiltInFirerWalk => FirerWalk is null;

    /// <summary>The die a gunnery level rolls, or null when the profile does not say.</summary>
    /// <param name="level">The gunnery level.</param>
    /// <returns>The die, or null.</returns>
    public QualityDie? FireControlDie(FireControlLevel level) =>
        FireControlDice.TryGetValue(level, out var die) ? die : null;

    /// <summary>
    /// The die a posture is worth, or null when the profile does not say.
    /// </summary>
    /// <param name="posture">What the target is doing about being shot at.</param>
    /// <returns>The die, or null. <see cref="DefensivePosture.None"/> is always null.</returns>
    /// <remarks>
    /// A target doing nothing rolls one die, so <see cref="DefensivePosture.None"/> answers null the
    /// way an unentered row does - and both mean "no second die", which is why the caller can treat
    /// them alike. The two are still distinguished before the refusal: <see cref="HasPostureDie"/>
    /// is what separates "nothing to look up" from "nothing entered".
    /// </remarks>
    public QualityDie? PostureDie(DefensivePosture posture) =>
        posture != DefensivePosture.None && PostureDice.TryGetValue(posture, out var die) ? die : null;

    /// <summary>True when the profile has a die for this posture, or the posture needs none.</summary>
    /// <param name="posture">What the target is doing.</param>
    /// <returns>Whether a shot at a target in this posture can be worked out.</returns>
    public bool HasPostureDie(DefensivePosture posture) =>
        posture == DefensivePosture.None || PostureDice.ContainsKey(posture);

    /// <summary>The die a signature rolls, or null when the profile does not say.</summary>
    /// <param name="signature">The number printed on the target's card.</param>
    /// <returns>The die, or null.</returns>
    public QualityDie? SignatureDie(int signature) =>
        SignatureDice.TryGetValue(signature, out var die) ? die : null;

    /// <summary>The number a recovery throw has to reach, or null when the profile does not say.</summary>
    /// <param name="hasBackupSystems">Whether backup systems were bought at design time.</param>
    /// <returns>The number to reach or beat, or null.</returns>
    public int? SystemsDownRecoveryTarget(bool hasBackupSystems)
    {
        var required = hasBackupSystems ? SystemsDownRecoveryRollWithBackup : SystemsDownRecoveryRoll;
        return required > 0 ? required : null;
    }

    /// <summary>How far an area-defence system reaches, or null when the profile does not say.</summary>
    /// <remarks>
    /// <para>
    /// Zero is "not entered", the same convention <see cref="SystemsDownRecoveryTarget"/> uses, and
    /// it costs nothing here: a system that reaches nowhere is not a system anybody would write on a
    /// card, so there is no real answer that zero would be hiding.
    /// </para>
    /// <para>
    /// <b>What this is not.</b> It is not half of a resolver. Nothing in this engine compares it
    /// with a distance yet, because what an interception measures its reach <em>against</em> - the
    /// firer, the target, the round's path - is one of the things nobody has written down. It is
    /// here because it is the one piece of interception that is a <em>number</em> rather than a
    /// procedure, and numbers are the players'. Its reader today is the eligibility question on the
    /// game, which will not call an element able to answer until its table has said how far it
    /// reaches.
    /// </para>
    /// </remarks>
    public int? InterceptionReach => AreaDefenceReach > 0 ? AreaDefenceReach : null;

    /// <summary>Compares two profiles by their contents, tables included.</summary>
    /// <param name="other">The profile to compare against.</param>
    /// <remarks>
    /// <see cref="ImmutableDictionary{TKey,TValue}"/> compares by reference, so the generated
    /// equality would call two profiles built from the same numbers unequal. The Full Thrust profile
    /// had to write this out by hand for the same reason.
    /// </remarks>
    public bool Equals(DirtsideRulesProfile? other) =>
        other is not null
        && SystemsDownRecoveryDie == other.SystemsDownRecoveryDie
        && SystemsDownRecoveryRoll == other.SystemsDownRecoveryRoll
        && SystemsDownRecoveryRollWithBackup == other.SystemsDownRecoveryRollWithBackup
        && AreaDefenceReach == other.AreaDefenceReach
        && LeadershipValues == other.LeadershipValues
        && RungsLostUnderFire == other.RungsLostUnderFire
        && FirerWalk == other.FirerWalk
        && Same(FireControlDice, other.FireControlDice)
        && Same(PostureDice, other.PostureDice)
        && Same(SignatureDice, other.SignatureDice);

    /// <summary>Hashes a profile by its contents.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SystemsDownRecoveryDie);
        hash.Add(SystemsDownRecoveryRoll);
        hash.Add(SystemsDownRecoveryRollWithBackup);
        hash.Add(AreaDefenceReach);
        hash.Add(LeadershipValues);
        hash.Add(RungsLostUnderFire);
        hash.Add(FirerWalk);
        hash.Add(FireControlDice.Count);
        hash.Add(PostureDice.Count);
        hash.Add(SignatureDice.Count);
        return hash.ToHashCode();
    }

    private static bool Same<TKey>(
        ImmutableDictionary<TKey, QualityDie> left,
        ImmutableDictionary<TKey, QualityDie> right)
        where TKey : notnull =>
        left.Count == right.Count
        && left.All(entry => right.TryGetValue(entry.Key, out var die) && die == entry.Value);
}

/// <summary>
/// How many rungs the firer's die moves from the die its sight rolls at medium range: up for a close
/// shot, down for a long one, and down again for a firer that moved over half.
/// </summary>
/// <param name="RungsUpAtClose">Rungs the die moves up for a shot in the close band.</param>
/// <param name="RungsDownAtLong">Rungs the die moves down for a shot in the long band.</param>
/// <param name="RungsDownMovingOverHalf">Rungs it moves down for a firer that moved over half.</param>
/// <remarks>
/// <para>
/// This used to be <c>(int)band + (firerMovedOverHalf ? -1 : 0)</c> in <see cref="HitResolution"/>:
/// the <see cref="WeaponRangeBand"/> enum's values <em>were</em> the table. The procedure - the band
/// and a hurried shot move the die, and running off the bottom is no shot - stays the engine's; how
/// far it moves is a number, and numbers are the players'.
/// </para>
/// <para>
/// <b>Optional, with a reported fallback, rather than refused when missing.</b> A game's profile is
/// fixed when it is created and no route edits it, so refusing a close or long shot on a profile with
/// no walk would strand every stored game's direct fire with nothing the table could type to fix it.
/// That is the chit pot's trade (gap 15): a game without the entry plays <see cref="BuiltIn"/> and the
/// snapshot's profile says so. Whether the fallback should become a refusal is the owner's call and is
/// recorded in the roadmap.
/// </para>
/// </remarks>
public sealed record FirerWalk(int RungsUpAtClose, int RungsDownAtLong, int RungsDownMovingOverHalf)
{
    /// <summary>
    /// The walk this app played before the entry existed: one rung per band and one for a hurried
    /// shot. Kept so that games created without an entry play on exactly as they always have.
    /// </summary>
    public static FirerWalk BuiltIn { get; } = new(1, 1, 1);

    /// <summary>The signed rungs a shot moves the firer's die.</summary>
    /// <param name="band">The range band the shot falls in.</param>
    /// <param name="firerMovedOverHalf">Whether the firer moved, or will move, over half.</param>
    /// <returns>Positive moves the die up the ladder, negative down.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The band is not one this engine knows.</exception>
    public int Steps(WeaponRangeBand band, bool firerMovedOverHalf)
    {
        var forBand = band switch
        {
            WeaponRangeBand.Close => RungsUpAtClose,
            WeaponRangeBand.Medium => 0,
            WeaponRangeBand.Long => -RungsDownAtLong,
            _ => throw new ArgumentOutOfRangeException(
                nameof(band), band, $"No firer walk is defined for range band {band}."),
        };

        return forBand - (firerMovedOverHalf ? RungsDownMovingOverHalf : 0);
    }
}
