using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Sequence;

/// <summary>
/// Building the shared layer's steps in Dirtside's vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Every step here names the element that took it, and every resource it spends is namespaced by
/// that element. That is the difference between the two games written down in one place: the other
/// game's limits are per activation, so its resource names are bare, while all of Dirtside's are per
/// element within the activation. Because a frame's spent set is flat, scoping is a matter of what
/// the name is built from, and the shared layer stays entirely uninterested in either scheme.
/// </para>
/// <para>
/// Going through these factories rather than writing the strings by hand is what stops a typo from
/// quietly becoming an action nobody has a rule for - or worse, an element nobody has a limit for.
/// </para>
/// </remarks>
public static class DirtsideSteps
{
    /// <summary>Prefix marking an element as having used its move.</summary>
    public const string MovePrefix = "moved:";

    /// <summary>Prefix marking an element as having used its combat action.</summary>
    public const string ActionPrefix = "acted:";

    /// <summary>Prefix marking a weapon system as fired.</summary>
    public const string WeaponPrefix = "weapon:";

    /// <summary>Prefix marking an element as having stood itself down for the turn.</summary>
    public const string StoodDownPrefix = "stood-down:";

    /// <summary>An element moving.</summary>
    /// <param name="element">The element moving.</param>
    /// <returns>The step.</returns>
    public static ActivationStep Move(ElementId element) =>
        ActivationStep.Of(Name(DirtsideAction.Move), element, Moved(element));

    /// <summary>
    /// An element firing one weapon system.
    /// </summary>
    /// <param name="element">The element firing.</param>
    /// <param name="weapon">The caller's name for the weapon system.</param>
    /// <returns>The step.</returns>
    /// <remarks>
    /// One weapon per step because the rules allow one weapon system per combat action, and one
    /// combat action per element. Several mounts of the same type and class are one system and belong
    /// under one name here - they fire together, at one target, and the engine should no more see two
    /// of them than the player does.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="weapon"/> is blank.</exception>
    public static ActivationStep Fire(ElementId element, string weapon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weapon);
        return ActivationStep.Of(
            Name(DirtsideAction.DirectFire), element, Acted(element), WeaponPrefix + weapon);
    }

    /// <summary>An element spending its combat action on something that fires nothing.</summary>
    /// <param name="action">Which of the actions it took.</param>
    /// <param name="element">The element acting.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentException">That action is not a weaponless combat action.</exception>
    public static ActivationStep Act(DirtsideAction action, ElementId element)
    {
        if (!DirtsideActions.IsCombatAction(action) || DirtsideActions.NamesAWeapon(action))
        {
            throw new ArgumentException(
                $"{action} is not a combat action that can be taken without naming a weapon.",
                nameof(action));
        }

        return ActivationStep.Of(Name(action), element, Acted(element));
    }

    /// <summary>
    /// An element declaring that it is doing nothing this turn.
    /// </summary>
    /// <param name="element">The element sitting out.</param>
    /// <returns>The step.</returns>
    /// <remarks>
    /// Recorded rather than left implicit because it is a decision with teeth: an element that does
    /// nothing during its unit's activation may not act later in the turn, and the activation is over
    /// for everybody at once. Written down, that is a fact in the after-action log; left out, it is
    /// indistinguishable from an activation the player has not finished yet.
    /// </remarks>
    public static ActivationStep StandDown(ElementId element) =>
        ActivationStep.Of(Name(DirtsideAction.Nothing), element, StoodDown(element));

    /// <summary>The step kind string for an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>Its kind.</returns>
    public static string Name(DirtsideAction action) => action.ToString();

    /// <summary>The resource name for an element having moved.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The name, ready to look up in a frame's spent set.</returns>
    public static string Moved(ElementId element) => MovePrefix + element.Value;

    /// <summary>The resource name for an element having taken its combat action.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The name, ready to look up in a frame's spent set.</returns>
    public static string Acted(ElementId element) => ActionPrefix + element.Value;

    /// <summary>The resource name for an element having stood down.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The name, ready to look up in a frame's spent set.</returns>
    public static string StoodDown(ElementId element) => StoodDownPrefix + element.Value;

    /// <summary>Reads an action back out of a step.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The action, or null when the kind is not one of Dirtside's.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static DirtsideAction? ActionOf(ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Enum.TryParse<DirtsideAction>(step.Kind, ignoreCase: false, out var action)
            && Enum.IsDefined(action)
            ? action
            : null;
    }

    /// <summary>The caller's own name for the weapon a fire step used, or null when it named none.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The unprefixed weapon name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is null.</exception>
    public static string? WeaponOf(ActivationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Consumes.IsDefaultOrEmpty)
        {
            return null;
        }

        var named = step.Consumes.FirstOrDefault(
            name => name.StartsWith(WeaponPrefix, StringComparison.Ordinal));

        return named is null ? null : named[WeaponPrefix.Length..];
    }
}
