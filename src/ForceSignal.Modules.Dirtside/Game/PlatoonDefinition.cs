using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>
/// One weapon system on an element, as the record card describes it.
/// </summary>
/// <remarks>
/// Every number here is the player's, off their own card. What the engine knows is that a weapon
/// draws chits, that a mount fires its barrels together at one target, and that a fixed mount is
/// aimed by pointing the vehicle - not what any particular gun is worth.
/// </remarks>
/// <param name="Name">What the player calls this system. Only ever compared, never parsed.</param>
/// <param name="ChitCount">How many chits each hit draws.</param>
/// <param name="Barrels">Weapons of the same type in the mount, fired together at one target.</param>
/// <param name="IsFixedMount">True when it can only be aimed by pointing the whole vehicle.</param>
/// <param name="Validity">What the drawn chits may count, per band, off the record card.</param>
/// <param name="IsInterceptable">True when an area-defence gun could shoot down what this throws.</param>
public sealed record WeaponDefinition(
    string Name,
    int ChitCount,
    int Barrels,
    bool IsFixedMount,
    WeaponValidityCard Validity,
    bool IsInterceptable = false);

/// <summary>
/// One vehicle, stand or model inside a platoon.
/// </summary>
/// <param name="Id">What the player calls it.</param>
/// <param name="Name">Its name on the table.</param>
/// <param name="FireControl">Its gunnery.</param>
/// <param name="Signature">How loud it is: 1 for the largest, up to 5 for the smallest.</param>
/// <param name="ArmourValue">The armour on the face most likely to be hit.</param>
/// <param name="Movement">How far it moves in one go, in the player's own units.</param>
/// <param name="Weapons">What it can shoot with.</param>
public sealed record ElementDefinition(
    ElementId Id,
    string Name,
    FireControlLevel FireControl,
    int Signature,
    int ArmourValue,
    int Movement,
    ImmutableArray<WeaponDefinition> Weapons)
{
    /// <summary>One of this element's weapons by name.</summary>
    /// <param name="weapon">The player's name for the system.</param>
    /// <returns>The weapon, or null when it does not carry one by that name.</returns>
    public WeaponDefinition? Weapon(string weapon) =>
        Weapons.IsDefaultOrEmpty
            ? null
            : Weapons.FirstOrDefault(mount => string.Equals(mount.Name, weapon, StringComparison.Ordinal));

    /// <summary>Compares two elements by their contents, weapons included.</summary>
    /// <param name="other">The element to compare against.</param>
    /// <remarks>
    /// Written by hand because <see cref="ImmutableArray{T}"/> compares by reference, so the record's
    /// own equality would compare two identical rosters as different.
    /// </remarks>
    public bool Equals(ElementDefinition? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && FireControl == other.FireControl
        && Signature == other.Signature
        && ArmourValue == other.ArmourValue
        && Movement == other.Movement
        && StructuralEquality.Sequence(Weapons, other.Weapons);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Id, Name, FireControl, Signature, ArmourValue, Movement,
            StructuralEquality.SequenceHash(Weapons));
}

/// <summary>
/// A platoon: the thing that activates, and the elements inside it that decide.
/// </summary>
/// <remarks>
/// The unit of activation in this game is the platoon, but nothing about what to <em>do</em> is
/// decided at that level - each element takes one move and one combat action in whichever order it
/// likes, or neither. That is why the elements are here rather than being a detail of status: the
/// activation rules read them to know when everybody has chosen.
/// </remarks>
/// <param name="Id">What the player calls this platoon.</param>
/// <param name="Name">Its name on the table.</param>
/// <param name="Side">Whose it is.</param>
/// <param name="Kind">Which column of the confidence effects table it reads.</param>
/// <param name="IsCybertank">True for a vehicle that carries no confidence marker at all.</param>
/// <param name="Elements">What it is made of, in the order they are listed on the roster.</param>
public sealed record PlatoonDefinition(
    UnitId Id,
    string Name,
    SideId Side,
    DirtsideUnitKind Kind,
    bool IsCybertank,
    ImmutableArray<ElementDefinition> Elements)
{
    /// <summary>One of this platoon's elements by id.</summary>
    /// <param name="element">The element to look up.</param>
    /// <returns>The element, or null when it is not in this platoon.</returns>
    public ElementDefinition? Element(ElementId element) =>
        Elements.IsDefaultOrEmpty ? null : Elements.FirstOrDefault(part => part.Id == element);

    /// <summary>Compares two platoons by their contents, elements included.</summary>
    /// <param name="other">The platoon to compare against.</param>
    public bool Equals(PlatoonDefinition? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && Side == other.Side
        && Kind == other.Kind
        && IsCybertank == other.IsCybertank
        && StructuralEquality.Sequence(Elements, other.Elements);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Id, Name, Side, Kind, IsCybertank, StructuralEquality.SequenceHash(Elements));
}
