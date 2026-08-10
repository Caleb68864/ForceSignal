using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.StarGrunt.Sequence;

/// <summary>
/// Building the shared layer's steps in StarGrunt's vocabulary.
/// </summary>
/// <remarks>
/// The shared layer treats a step's kind as an opaque string on purpose, so this is where the string
/// gets its meaning. Going through these factories rather than writing the strings by hand is what
/// keeps a typo from silently becoming an action nobody has a rule for.
/// </remarks>
public static class StarGruntSteps
{
    /// <summary>Prefix marking a consumed resource as a weapon rather than anything else.</summary>
    /// <remarks>
    /// Namespaced because the frame's resource set is flat and shared with any other limit a game
    /// wants to express against it.
    /// </remarks>
    public const string WeaponPrefix = "weapon:";

    /// <summary>Prefix marking a consumed resource as an activation handed to a named subordinate.</summary>
    public const string TransferPrefix = "transfer:";

    /// <summary>A single move.</summary>
    /// <param name="subject">The element moving, or null for the whole squad.</param>
    /// <returns>The step.</returns>
    public static ActivationStep Move(ElementId? subject = null) =>
        ActivationStep.Of(Name(StarGruntAction.Move), subject);

    /// <summary>
    /// Both actions spent moving, declared as one thing.
    /// </summary>
    /// <param name="subject">The element moving, or null for the whole squad.</param>
    /// <returns>The step.</returns>
    /// <remarks>
    /// Announcing the dash before it is run is how it works at a table, and it is the only way the
    /// reaction window can open at the mid-point without the opponent having to be shown a window
    /// that may yet be withdrawn.
    /// </remarks>
    public static ActivationStep Dash(ElementId? subject = null) =>
        ActivationStep.Of(Name(StarGruntAction.Dash), subject);

    /// <summary>Fire one weapon.</summary>
    /// <param name="weapon">The caller's name for the weapon, which is what the limit is kept against.</param>
    /// <param name="subject">The element firing, or null for the whole squad.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentException"><paramref name="weapon"/> is blank.</exception>
    public static ActivationStep Fire(string weapon, ElementId? subject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weapon);
        return ActivationStep.Of(Name(StarGruntAction.Fire), subject, WeaponPrefix + weapon);
    }

    /// <summary>
    /// Fire small arms with support weapons folded in, spending every weapon named.
    /// </summary>
    /// <param name="weapon">The small arms the volley is resolved on.</param>
    /// <param name="supportWeapons">Support weapons adding their weight to it.</param>
    /// <param name="subject">The element firing, or null for the whole squad.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="supportWeapons"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="weapon"/> is blank.</exception>
    /// <remarks>
    /// All of them are spent, because a weapon folded into squad fire may not also fire on its own
    /// that activation. Putting each into the frame's resources is what enforces it - the limit is
    /// read off the steps rather than remembered anywhere.
    /// </remarks>
    public static ActivationStep Fire(string weapon, IEnumerable<string> supportWeapons, ElementId? subject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weapon);
        ArgumentNullException.ThrowIfNull(supportWeapons);

        var spent = new List<string> { WeaponPrefix + weapon };
        spent.AddRange(supportWeapons.Select(name => WeaponPrefix + name));
        return ActivationStep.Of(Name(StarGruntAction.Fire), subject, [.. spent]);
    }

    /// <summary>Every weapon a step spends.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The weapon resources it names, which may be more than one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static IEnumerable<string> WeaponsOf(ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Consumes.IsDefaultOrEmpty
            ? []
            : step.Consumes.Where(name => name.StartsWith(WeaponPrefix, StringComparison.Ordinal));
    }

    /// <summary>Hand a named subordinate a whole extra activation.</summary>
    /// <param name="beneficiary">The unit being sprung.</param>
    /// <param name="subject">The element making the call, or null.</param>
    /// <returns>The step.</returns>
    /// <remarks>
    /// The beneficiary rides in the consumed-resource set so that both facts the rules care about -
    /// how many transfers this commander has made, and that he has not sprung the same unit twice -
    /// are read back off the frame rather than tallied anywhere.
    /// </remarks>
    public static ActivationStep Transfer(UnitId beneficiary, ElementId? subject = null) =>
        ActivationStep.Of(Name(StarGruntAction.TransferAction), subject, TransferPrefix + beneficiary.Value);

    /// <summary>Any action that needs nothing but its own name.</summary>
    /// <param name="action">The action taken.</param>
    /// <param name="subject">The element acting, or null for the whole squad.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentException">The action needs more than its name.</exception>
    public static ActivationStep Simple(StarGruntAction action, ElementId? subject = null)
    {
        if (action is StarGruntAction.Fire or StarGruntAction.TransferAction)
        {
            throw new ArgumentException(
                $"{action} has to name what it spends; use the dedicated factory.", nameof(action));
        }

        return ActivationStep.Of(Name(action), subject);
    }

    /// <summary>The step kind string for an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>Its kind.</returns>
    public static string Name(StarGruntAction action) => action.ToString();

    /// <summary>Reads an action back out of a step.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The action, or null when the kind is not one of StarGrunt's.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static StarGruntAction? ActionOf(ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Enum.TryParse<StarGruntAction>(step.Kind, ignoreCase: false, out var action)
            && Enum.IsDefined(action)
            ? action
            : null;
    }

    /// <summary>The subordinate a transfer step names, or null when it names none.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The prefixed resource name, ready to look up in a frame's spent set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static string? TransferTargetOf(ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Consumes.IsDefaultOrEmpty
            ? null
            : step.Consumes.FirstOrDefault(name => name.StartsWith(TransferPrefix, StringComparison.Ordinal));
    }

    /// <summary>The weapon a fire step names, or null when it names none.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The prefixed resource name, ready to look up in a frame's spent set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static string? WeaponOf(ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Consumes.IsDefaultOrEmpty
            ? null
            : step.Consumes.FirstOrDefault(name => name.StartsWith(WeaponPrefix, StringComparison.Ordinal));
    }
}
