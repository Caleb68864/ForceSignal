using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>What has happened to one element.</summary>
/// <param name="IsDestroyed">True when it is out of the battle.</param>
/// <param name="IsDamaged">True when it carries a DMG marker.</param>
/// <param name="IsSystemsDown">True when its systems are down and it is doing nothing until they are back.</param>
/// <param name="MovedOverHalf">True when it has moved, or will move, more than half its movement.</param>
/// <param name="Posture">What it is doing about being shot at.</param>
/// <param name="AreaDefenceSensorsLive">True when its sensors are on and it may intercept all turn.</param>
public sealed record ElementStatus(
    bool IsDestroyed = false,
    bool IsDamaged = false,
    bool IsSystemsDown = false,
    bool MovedOverHalf = false,
    DefensivePosture Posture = DefensivePosture.None,
    bool AreaDefenceSensorsLive = false)
{
    /// <summary>An element nothing has happened to yet.</summary>
    public static ElementStatus Fresh { get; } = new();
}

/// <summary>
/// What has happened to one platoon, and to each element inside it.
/// </summary>
/// <remarks>
/// This is the half of the world the activation rules read but the sequence layer does not own -
/// confidence, Under Fire, integrity - kept alongside the per-element damage so that one lookup
/// answers both.
/// </remarks>
public sealed record PlatoonStatus
{
    /// <summary>Where its confidence marker stands. Meaningless for a cybertank, which carries none.</summary>
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Confident;

    /// <summary>True when it is carrying an Under Fire marker.</summary>
    public bool IsUnderFire { get; init; }

    /// <summary>True when it has come apart and may only move to close its ranks.</summary>
    public bool IsDisorganised { get; init; }

    /// <summary>
    /// True when the move about to be ordered would take it toward the enemy or out of cover.
    /// </summary>
    /// <remarks>
    /// A declaration rather than a calculation, and deliberately so: whether a move counts as
    /// advancing is an eyeball judgement at a real table.
    /// </remarks>
    public bool NextMoveAdvancesOnTheEnemy { get; init; }

    /// <summary>True when the reaction test the next move needs has been rolled and passed.</summary>
    public bool ReactionTestCleared { get; init; }

    /// <summary>What has happened to each element, by id.</summary>
    public ImmutableDictionary<ElementId, ElementStatus> Elements { get; init; } =
        ImmutableDictionary<ElementId, ElementStatus>.Empty;

    /// <summary>A platoon at full strength with nothing yet done to it.</summary>
    /// <param name="platoon">The platoon.</param>
    /// <returns>Its opening status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="platoon"/> is null.</exception>
    public static PlatoonStatus ForFullStrength(PlatoonDefinition platoon)
    {
        ArgumentNullException.ThrowIfNull(platoon);

        return new PlatoonStatus
        {
            Elements = platoon.Elements.IsDefaultOrEmpty
                ? ImmutableDictionary<ElementId, ElementStatus>.Empty
                : platoon.Elements.ToImmutableDictionary(element => element.Id, _ => ElementStatus.Fresh),
        };
    }

    /// <summary>What has happened to one element.</summary>
    /// <param name="element">The element to look up.</param>
    /// <returns>Its status, fresh when nothing has been recorded against it.</returns>
    public ElementStatus Element(ElementId element) =>
        Elements.TryGetValue(element, out var status) ? status : ElementStatus.Fresh;

    /// <summary>The same status with one element changed.</summary>
    /// <param name="element">The element.</param>
    /// <param name="edit">What to change about it.</param>
    /// <returns>The status with the change applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is null.</exception>
    public PlatoonStatus WithElement(ElementId element, Func<ElementStatus, ElementStatus> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return this with { Elements = Elements.SetItem(element, edit(Element(element))) };
    }

    /// <summary>True when nothing in this platoon is left on the table.</summary>
    public bool IsWipedOut => !Elements.IsEmpty && Elements.Values.All(element => element.IsDestroyed);

    /// <summary>
    /// The state the activation rules read.
    /// </summary>
    /// <param name="kind">Which column of the confidence table this platoon reads.</param>
    /// <param name="isCybertank">True when it carries no confidence marker at all.</param>
    /// <returns>The state.</returns>
    public DirtsideUnitState ToUnitState(DirtsideUnitKind kind, bool isCybertank) => new()
    {
        Kind = kind,
        IsCybertank = isCybertank,
        Confidence = Confidence,
        IsUnderFire = IsUnderFire,
        IsDisorganised = IsDisorganised,
        NextMoveAdvancesOnTheEnemy = NextMoveAdvancesOnTheEnemy,
        ReactionTestCleared = ReactionTestCleared,
    };

    /// <summary>Compares two statuses by their contents, the per-element map included.</summary>
    /// <param name="other">The status to compare against.</param>
    /// <remarks>
    /// Written by hand because <see cref="ImmutableDictionary{TKey,TValue}"/> compares by reference.
    /// </remarks>
    public bool Equals(PlatoonStatus? other) =>
        other is not null
        && Confidence == other.Confidence
        && IsUnderFire == other.IsUnderFire
        && IsDisorganised == other.IsDisorganised
        && NextMoveAdvancesOnTheEnemy == other.NextMoveAdvancesOnTheEnemy
        && ReactionTestCleared == other.ReactionTestCleared
        && StructuralEquality.Map(Elements, other.Elements);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Confidence, IsUnderFire, IsDisorganised, NextMoveAdvancesOnTheEnemy, ReactionTestCleared,
            StructuralEquality.MapHash(Elements));
}
