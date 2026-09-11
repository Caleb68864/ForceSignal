using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;

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
public sealed record DirtsideRulesProfile(
    ImmutableDictionary<FireControlLevel, QualityDie> FireControlDice,
    ImmutableDictionary<DefensivePosture, QualityDie> PostureDice,
    ImmutableDictionary<int, QualityDie> SignatureDice,
    QualityDie? SystemsDownRecoveryDie = null,
    int SystemsDownRecoveryRoll = 0,
    int SystemsDownRecoveryRollWithBackup = 0)
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
        && SystemsDownRecoveryDie is null;

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
