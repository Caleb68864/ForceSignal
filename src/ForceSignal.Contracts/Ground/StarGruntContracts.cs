namespace ForceSignal.Contracts.Ground;

/// <summary>
/// The wire shapes for a StarGrunt game.
/// </summary>
/// <remarks>
/// <para>
/// Dice cross the wire as face counts - 4, 6, 8, 10, 12 - rather than as a named type. The quality
/// ladder lives in the ground-combat module, and this assembly deliberately depends on nothing but
/// the domain, so borrowing the enum here would drag a module into the contract layer for the sake
/// of five integers that are already the face counts.
/// </para>
/// <para>
/// No stats are supplied by any of this. Every die a client sends is one the user typed off their
/// own record card.
/// </para>
/// </remarks>
public static class StarGruntWire
{
    /// <summary>Die face counts the quality ladder recognises.</summary>
    public static readonly int[] Ladder = [4, 6, 8, 10, 12];
}

/// <summary>Starts a new game.</summary>
/// <param name="Name">What to call it.</param>
public sealed record CreateStarGruntGameRequest(string Name);

/// <summary>One figure on a record card.</summary>
/// <param name="ArmourDie">Its personal armour die, as a face count.</param>
public sealed record StarGruntFigureDto(int ArmourDie);

/// <summary>One weapon on a record card.</summary>
/// <param name="Name">What the card calls it.</param>
/// <param name="ImpactDie">Its impact die, as a face count.</param>
/// <param name="IsSupport">True when it is a support weapon.</param>
/// <param name="IsCloseRange">True when it is effective only inside one band.</param>
public sealed record StarGruntWeaponDto(string Name, int ImpactDie, bool IsSupport = false, bool IsCloseRange = false);

/// <summary>Puts a unit on the table.</summary>
/// <param name="Id">How the game will name it. Must be unique in the game.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="Side">Which side it belongs to.</param>
/// <param name="Level">Where it sits in the chain of command.</param>
/// <param name="QualityDie">Its quality die, as a face count.</param>
/// <param name="LeadershipValue">Its Leadership Value, 1 to 3, where 1 is best.</param>
/// <param name="Fatigue">How worn it is: Fresh, Tired or Exhausted.</param>
/// <param name="Figures">The figures in it.</param>
/// <param name="Weapons">What it is carrying.</param>
public sealed record AddStarGruntUnitRequest(
    string Id,
    string Name,
    string Side,
    string Level,
    int QualityDie,
    int LeadershipValue,
    IReadOnlyList<StarGruntFigureDto> Figures,
    IReadOnlyList<StarGruntWeaponDto> Weapons,
    string Fatigue = "Fresh");

/// <summary>Settles who takes the first activation this turn.</summary>
/// <param name="Side">The side making the choice.</param>
/// <param name="TakeIt">True to go first, false to give it away.</param>
public sealed record ChooseFirstActivatorRequest(string Side, bool TakeIt);

/// <summary>Opens an activation.</summary>
/// <param name="Side">The side activating.</param>
/// <param name="UnitId">The unit being activated.</param>
public sealed record BeginStarGruntActivationRequest(string Side, string UnitId);

/// <summary>Spends one action on something that is not shooting.</summary>
/// <param name="Action">The action taken: Move, Dash, Reorganise and so on.</param>
public sealed record StarGruntStepRequest(string Action);

/// <summary>An action a named unit takes that needs nothing but the unit.</summary>
/// <param name="UnitId">The unit acting.</param>
public sealed record StarGruntUnitActionRequest(string UnitId);

/// <summary>
/// Puts a unit's nerve to the test after something bad has happened to it.
/// </summary>
/// <param name="UnitId">The unit under strain.</param>
/// <param name="ThreatLevel">
/// How serious it was, read off the player's own threat table. Not supplied by this app: the table
/// is rated against the force's mission motivation and belongs to the user's rules.
/// </param>
public sealed record StarGruntConfidenceTestRequest(string UnitId, int ThreatLevel);

/// <summary>Spends a command element's action steadying a subordinate.</summary>
/// <param name="RallyingUnitId">The command element, which spends the action.</param>
/// <param name="RalliedUnitId">The unit being steadied, which does the rolling.</param>
public sealed record StarGruntRallyRequest(string RallyingUnitId, string RalliedUnitId);

/// <summary>Declares whether a unit has scattered out of integrity.</summary>
/// <param name="UnitId">The unit.</param>
/// <param name="IsDisorganised">True when it is out of integrity and owes a reorganise.</param>
public sealed record StarGruntDisorganisedRequest(string UnitId, bool IsDisorganised);

/// <summary>Declines to activate anything.</summary>
/// <param name="Side">The side passing.</param>
public sealed record StarGruntPassRequest(string Side);

/// <summary>Fires one weapon at another unit.</summary>
/// <param name="FirerId">The unit shooting.</param>
/// <param name="TargetId">The unit being shot at.</param>
/// <param name="WeaponName">Which of the firer's weapons.</param>
/// <param name="FirepowerDie">The small-arms firepower die, as a face count, off the user's table.</param>
/// <param name="SupportDice">One face count per support weapon joining the volley.</param>
/// <param name="DistanceInches">How far apart the two units are, as measured at the table.</param>
/// <param name="Cover">The target's cover: None, Soft or Hard.</param>
/// <param name="InPosition">True when the target has settled into its ground.</param>
public sealed record StarGruntFireRequest(
    string FirerId,
    string TargetId,
    string WeaponName,
    int FirepowerDie,
    IReadOnlyList<int> SupportDice,
    decimal DistanceInches,
    string Cover,
    bool InPosition = false);

/// <summary>Whether one weapon may fire, and why not.</summary>
/// <param name="Name">The weapon.</param>
/// <param name="CanFire">True when a volley would be allowed.</param>
/// <param name="Blocker">Why it would not be, in the words the command would refuse with.</param>
public sealed record StarGruntWeaponLegalityDto(string Name, bool CanFire, string? Blocker);

/// <summary>A unit as the table sees it.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="Side">Which side it is on.</param>
/// <param name="Level">Its command level.</param>
/// <param name="QualityDie">Its quality die, as a face count.</param>
/// <param name="LeadershipValue">Its Leadership Value, 1 to 3, where 1 is best.</param>
/// <param name="Fatigue">How worn it is, which caps its confidence.</param>
/// <param name="FiguresAlive">Figures still standing.</param>
/// <param name="FullStrength">Figures it started with.</param>
/// <param name="FiguresWounded">Casualties the squad is carrying, out of the fight.</param>
/// <param name="IsLeaderDown">True once the squad leader has been hit.</param>
/// <param name="SuppressionMarkers">Suppression on it, from none to three.</param>
/// <param name="Confidence">How much fight it has left.</param>
/// <param name="IsDisorganised">True when it owes a reorganise before anything else.</param>
/// <param name="IsInCover">True when it has something to hide behind.</param>
/// <param name="HasActivated">True when it has already gone this turn.</param>
/// <param name="Weapons">What it is carrying.</param>
/// <param name="CanActivate">True when it could be activated right now.</param>
/// <param name="ActivationBlocker">Why it could not be.</param>
/// <param name="WeaponLegality">Whether each weapon may fire, and why not.</param>
public sealed record StarGruntUnitDto(
    string Id,
    string Name,
    string Side,
    string Level,
    int QualityDie,
    int LeadershipValue,
    string Fatigue,
    int FiguresAlive,
    int FullStrength,
    int FiguresWounded,
    bool IsLeaderDown,
    int SuppressionMarkers,
    string Confidence,
    bool IsDisorganised,
    bool IsInCover,
    bool HasActivated,
    IReadOnlyList<StarGruntWeaponDto> Weapons,
    bool CanActivate,
    string? ActivationBlocker,
    IReadOnlyList<StarGruntWeaponLegalityDto> WeaponLegality);

/// <summary>
/// The whole state of a game, as a client needs to draw it.
/// </summary>
/// <remarks>
/// Carries the legality of every action alongside the state, so a client never works out for itself
/// whether something is allowed. That answer comes from the same checks the commands enforce.
/// </remarks>
/// <param name="GameId">The game.</param>
/// <param name="Name">What it is called.</param>
/// <param name="TurnNumber">Which turn this is.</param>
/// <param name="Phase">Where the turn has got to.</param>
/// <param name="Sides">The sides in play.</param>
/// <param name="ActiveSide">Whose go it is, when a turn is under way.</param>
/// <param name="ActivatingUnitId">The unit part-way through an activation, if any.</param>
/// <param name="FirstActivationChooser">The side entitled to choose who goes first, when that is pending.</param>
/// <param name="Units">Everyone on the table.</param>
/// <param name="Log">What has happened, oldest first.</param>
/// <param name="Version">Bumped on every change, so a client can drop a stale answer.</param>
public sealed record StarGruntSnapshotDto(
    Guid GameId,
    string Name,
    int TurnNumber,
    string Phase,
    IReadOnlyList<string> Sides,
    string? ActiveSide,
    string? ActivatingUnitId,
    string? FirstActivationChooser,
    IReadOnlyList<StarGruntUnitDto> Units,
    IReadOnlyList<string> Log,
    long Version);

/// <summary>What creating a game hands back.</summary>
/// <param name="GameId">The new game's id.</param>
/// <param name="Snapshot">Its opening state.</param>
public sealed record StarGruntGameCreatedResponse(Guid GameId, StarGruntSnapshotDto Snapshot);
