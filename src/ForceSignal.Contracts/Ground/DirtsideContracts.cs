namespace ForceSignal.Contracts.Ground;

/// <summary>
/// The wire shapes for a Dirtside game.
/// </summary>
/// <remarks>
/// <para>
/// A platoon activates and the elements inside it decide, so almost everything here names an
/// element. A request that named only the platoon would have no rule to apply: there is no action
/// budget at that level.
/// </para>
/// <para>
/// No stats are supplied by any of this. Every number a client sends - gunnery, signature, armour,
/// chit counts, what colours a weapon's chits may count - is one the user read off their own record
/// card. This assembly ships none of them.
/// </para>
/// </remarks>
public static class DirtsideWire
{
    /// <summary>Range bands a shot can be measured at.</summary>
    public static readonly string[] Bands = ["Close", "Medium", "Long"];

    /// <summary>Gunnery levels an element can have.</summary>
    public static readonly string[] FireControls = ["Basic", "Enhanced", "Superior"];
}

/// <summary>Starts a new game.</summary>
/// <param name="Name">What to call it.</param>
public sealed record CreateDirtsideGameRequest(string Name);

/// <summary>
/// What one weapon system's chits may count, at one range band, off the record card.
/// </summary>
/// <param name="Colours">Which chit colours count: any of Red, Yellow, Green, or All.</param>
/// <param name="ValueScale">How the numbers read: Doubled, FaceValue or Halved.</param>
/// <param name="SpecialsCount">True when the special chits do something.</param>
/// <param name="IsIneffective">True when this weapon cannot harm this target at this band at all.</param>
public sealed record DirtsideValidityDto(
    string Colours = "All",
    string ValueScale = "FaceValue",
    bool SpecialsCount = true,
    bool IsIneffective = false);

/// <summary>One weapon system on an element.</summary>
/// <param name="Name">What the card calls it.</param>
/// <param name="ChitCount">How many chits each hit draws.</param>
/// <param name="Barrels">Weapons of the same type in the mount, fired together at one target.</param>
/// <param name="IsFixedMount">True when it is aimed by pointing the whole vehicle.</param>
/// <param name="Close">What its chits may count at close range.</param>
/// <param name="Medium">What its chits may count at medium range.</param>
/// <param name="Long">What its chits may count at long range.</param>
/// <param name="IsInterceptable">True when an area-defence gun could shoot down what it throws.</param>
// "Long" is the range band's name on the card and the JSON key the client already sends; the
// analyzer objects because it is also a type name, which is not what it means here.
#pragma warning disable CA1720
public sealed record DirtsideWeaponDto(
    string Name,
    int ChitCount,
    int Barrels = 1,
    bool IsFixedMount = false,
    DirtsideValidityDto? Close = null,
    DirtsideValidityDto? Medium = null,
    DirtsideValidityDto? Long = null,
    bool IsInterceptable = false);
#pragma warning restore CA1720

/// <summary>One vehicle, stand or model inside a platoon.</summary>
/// <param name="Id">How the game will name it. Must be unique in the platoon.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="FireControl">Its gunnery: Basic, Enhanced or Superior.</param>
/// <param name="Signature">How loud it is: 1 for the largest, up to 5 for the smallest.</param>
/// <param name="ArmourValue">The armour on the face most likely to be hit.</param>
/// <param name="Movement">How far it moves in one go, in the player's own units.</param>
/// <param name="Weapons">What it can shoot with.</param>
public sealed record DirtsideElementDto(
    string Id,
    string Name,
    string FireControl,
    int Signature,
    int ArmourValue,
    int Movement,
    IReadOnlyList<DirtsideWeaponDto> Weapons);

/// <summary>Puts a platoon on the table.</summary>
/// <param name="Id">How the game will name it. Must be unique in the game.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="Side">Which side it belongs to.</param>
/// <param name="Kind">Which column of the confidence table it reads: DismountedInfantry or Armour.</param>
/// <param name="IsCybertank">True for a vehicle that carries no confidence marker at all.</param>
/// <param name="Elements">What it is made of.</param>
public sealed record AddDirtsidePlatoonRequest(
    string Id,
    string Name,
    string Side,
    string Kind,
    bool IsCybertank,
    IReadOnlyList<DirtsideElementDto> Elements);

/// <summary>Settles who takes the first activation this turn.</summary>
/// <param name="Side">The side making the choice.</param>
/// <param name="TakeIt">True to go first, false to give it away.</param>
public sealed record ChooseDirtsideFirstActivatorRequest(string Side, bool TakeIt);

/// <summary>Opens an activation.</summary>
/// <param name="Side">The side activating.</param>
/// <param name="UnitId">The platoon being activated.</param>
public sealed record BeginDirtsideActivationRequest(string Side, string UnitId);

/// <summary>Moves one element.</summary>
/// <param name="ElementId">The element moving.</param>
/// <param name="OverHalfItsMovement">
/// True when the move covers more than half its movement, which spoils its shooting. Measured with a
/// tape at the table, so it is answered rather than computed.
/// </param>
public sealed record MoveDirtsideElementRequest(string ElementId, bool OverHalfItsMovement = false);

/// <summary>Declares that an element is sitting this activation out, and therefore the whole turn.</summary>
/// <param name="ElementId">The element standing down.</param>
public sealed record DirtsideStandDownRequest(string ElementId);

/// <summary>Switches an element's area-defence sensors on or off, spending its combat action.</summary>
/// <param name="ElementId">The element.</param>
/// <param name="Live">True to switch them on.</param>
public sealed record DirtsideSensorsRequest(string ElementId, bool Live);

/// <summary>Fires one element's weapon at one designated element.</summary>
/// <param name="ElementId">The element firing.</param>
/// <param name="Weapon">Which of its systems, by the name on the card.</param>
/// <param name="TargetUnitId">The platoon being shot at.</param>
/// <param name="TargetElementId">The element designated, before any dice.</param>
/// <param name="MeasuredBand">The band the tape says the shot falls in: Close, Medium or Long.</param>
public sealed record DirtsideFireRequest(
    string ElementId,
    string Weapon,
    string TargetUnitId,
    string TargetElementId,
    string MeasuredBand);

/// <summary>Declines to activate anything.</summary>
/// <param name="Side">The side passing.</param>
public sealed record DirtsidePassRequest(string Side);

/// <summary>What has happened to one element, as the table sees it.</summary>
/// <param name="Id">The element.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="IsDestroyed">True when it is out of the battle.</param>
/// <param name="IsDamaged">True when it carries a DMG marker.</param>
/// <param name="IsSystemsDown">True when it is doing nothing until its systems are back.</param>
/// <param name="MovedOverHalf">True when it has moved more than half its movement this turn.</param>
/// <param name="AreaDefenceSensorsLive">True when it may intercept for the rest of the turn.</param>
/// <param name="HasChosen">True when it has said what it is doing in the open activation.</param>
/// <param name="HasMoved">True when it has spent its move this activation.</param>
/// <param name="HasTakenCombatAction">True when it has spent its one combat action.</param>
/// <param name="HasStoodDown">True when it sat this activation out, and so the whole turn.</param>
/// <param name="Weapons">What it can shoot with.</param>
/// <remarks>
/// The move and the combat action are reported separately because they are separate: an element
/// that has moved may still shoot, and one that has shot may still move. A single "has chosen" flag
/// is enough to know whether the activation can close and not enough to drive a screen - which is
/// how a screen came to move one vehicle and then quietly fire a different one.
/// </remarks>
public sealed record DirtsideElementStateDto(
    string Id,
    string Name,
    bool IsDestroyed,
    bool IsDamaged,
    bool IsSystemsDown,
    bool MovedOverHalf,
    bool AreaDefenceSensorsLive,
    bool HasChosen,
    bool HasMoved,
    bool HasTakenCombatAction,
    bool HasStoodDown,
    IReadOnlyList<string> Weapons);

/// <summary>A platoon as the table sees it.</summary>
/// <param name="Id">The platoon.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="Side">Whose it is.</param>
/// <param name="Kind">Which column of the confidence table it reads.</param>
/// <param name="IsCybertank">True when it carries no confidence marker.</param>
/// <param name="Confidence">Where its confidence marker stands.</param>
/// <param name="IsUnderFire">True when it carries an Under Fire marker.</param>
/// <param name="IsDisorganised">True when it may only move to close its ranks.</param>
/// <param name="HasActivated">True when its marker is face down for the turn.</param>
/// <param name="CanActivate">True when it could be activated right now.</param>
/// <param name="WhyItCannotActivate">Why not, in the words the command would refuse with.</param>
/// <param name="Elements">What it is made of.</param>
public sealed record DirtsidePlatoonStateDto(
    string Id,
    string Name,
    string Side,
    string Kind,
    bool IsCybertank,
    string Confidence,
    bool IsUnderFire,
    bool IsDisorganised,
    bool HasActivated,
    bool CanActivate,
    string? WhyItCannotActivate,
    IReadOnlyList<DirtsideElementStateDto> Elements);

/// <summary>A whole Dirtside game as the table sees it.</summary>
/// <param name="GameId">Which game.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="TurnNumber">Which turn it is.</param>
/// <param name="Phase">Where the turn has got to.</param>
/// <param name="Sides">The sides in play.</param>
/// <param name="ActiveSide">Whose go it is, or null.</param>
/// <param name="ActivatingUnitId">The platoon part-way through its activation, or null.</param>
/// <param name="ElementsStillToChoose">
/// Who the open activation is waiting on. The activation cannot close until this is empty, because
/// an element that sits out has given up its go for the whole turn and has to say so.
/// </param>
/// <param name="CanEndActivation">True when the open activation could be closed now.</param>
/// <param name="WhyActivationCannotEnd">Why not, in the words the command would refuse with.</param>
/// <param name="Units">Everyone on the table.</param>
/// <param name="Log">What has happened, in the order it happened.</param>
/// <param name="Version">Bumped whenever anything changes, so a client can tell.</param>
public sealed record DirtsideSnapshotDto(
    Guid GameId,
    string Name,
    int TurnNumber,
    string Phase,
    IReadOnlyList<string> Sides,
    string? ActiveSide,
    string? ActivatingUnitId,
    IReadOnlyList<string> ElementsStillToChoose,
    bool CanEndActivation,
    string? WhyActivationCannotEnd,
    IReadOnlyList<DirtsidePlatoonStateDto> Units,
    IReadOnlyList<string> Log,
    int Version);

/// <summary>A game that has just been started.</summary>
/// <param name="GameId">Which game.</param>
/// <param name="Snapshot">Its opening state.</param>
/// <param name="Token">
/// The game token. Handed back here and nowhere else: every other route for this game requires it
/// in the <c>X-Game-Token</c> header, and no snapshot ever carries it. One token for the whole game,
/// because the screen is a hot seat - one device, both sides.
/// </param>
public sealed record DirtsideGameCreatedResponse(Guid GameId, DirtsideSnapshotDto Snapshot, string Token);
