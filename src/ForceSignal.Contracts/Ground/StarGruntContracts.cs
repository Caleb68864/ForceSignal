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
/// <remarks>
/// The ladder below is the only copy of itself. The service builds its refusal of an off-ladder die
/// out of it, the web client imports the same numbers rather than repeating them, and a module test
/// holds it against the quality die enum whose face counts it mirrors - which is what lets this
/// assembly keep its distance from the module without the two drifting apart in silence.
/// </remarks>
public static class StarGruntWire
{
    /// <summary>Die face counts the quality ladder recognises.</summary>
    public static readonly int[] Ladder = [4, 6, 8, 10, 12];
}

/// <summary>One row of the band-width table: how wide a range band is for troops of one quality.</summary>
/// <param name="QualityDie">The firing unit's quality die, as a face count.</param>
/// <param name="Inches">How many inches one band is for those troops, off the players' own rulebook.</param>
public sealed record StarGruntBandWidthDto(int QualityDie, int Inches);

/// <summary>One row of the range-die table: what a target this many bands out rolls before cover.</summary>
/// <param name="BandsOut">Bands between firer and target, counting the first as one.</param>
/// <param name="Die">The die the target rolls, as a face count.</param>
public sealed record StarGruntRangeDieDto(int BandsOut, int Die);

/// <summary>
/// The numbers a StarGrunt shot reads off the rulebook's range page, and the cover shift a melee
/// reads, entered by the players.
/// </summary>
/// <remarks>
/// <para>
/// This app supplies none of them. The engine used to: a band was the firer's own quality die read as
/// inches, the target's die walked up the ladder one rung per band from the bottom, soft and hard
/// cover were worth one and two rungs, being dug in one more, and the reach was the ladder's length.
/// Each of those was a reading off somebody's page, and they are all here now, together.
/// </para>
/// <para>
/// Every part is optional, and an unentered part is absent rather than zero - null for the numbers,
/// a missing row for the tables. A shot that reads an entry nobody made is refused and names it; a
/// shot that reads only what was entered is settled. Zero is a real answer for a cover shift ("this
/// cover does nothing in our rules"), which is why the shifts are nullable rather than zero-as-blank.
/// </para>
/// </remarks>
/// <param name="BandWidths">How wide a band is, per quality of firing troops.</param>
/// <param name="RangeDice">The die a target rolls at each band out, before cover.</param>
/// <param name="EffectiveBands">
/// How many bands out small arms still have an effective shot at a target in the open, or null when
/// not entered.
/// </param>
/// <param name="SoftCoverShift">Rungs soft cover moves a die up, or null when not entered.</param>
/// <param name="HardCoverShift">Rungs hard cover moves a die up, or null when not entered.</param>
/// <param name="InPositionShift">
/// Rungs a target settled into its position moves a die up, on top of cover, or null when not entered.
/// </param>
/// <param name="MeleeCoverShift">
/// Rungs cover is worth to a defender in the first round of a melee, or null when not entered. The
/// engine had this as one rung of its own; a melee fought with the defenders in cover now reads it
/// here, and one fought in the open never asks.
/// </param>
/// <param name="LowestLeadershipValue">
/// The smallest number a record card in this game may carry as a Leadership Value, or null when not
/// entered. Both ends or neither: one bound on its own is refused rather than half-honoured.
/// </param>
/// <param name="HighestLeadershipValue">The largest, or null when not entered.</param>
public sealed record StarGruntRulesProfileDto(
    IReadOnlyList<StarGruntBandWidthDto>? BandWidths = null,
    IReadOnlyList<StarGruntRangeDieDto>? RangeDice = null,
    int? EffectiveBands = null,
    int? SoftCoverShift = null,
    int? HardCoverShift = null,
    int? InPositionShift = null,
    int? MeleeCoverShift = null,
    int? LowestLeadershipValue = null,
    int? HighestLeadershipValue = null);

/// <summary>Starts a new game.</summary>
/// <param name="Name">What to call it.</param>
/// <param name="Profile">
/// The range table this game is played on, off the players' own rulebook. Optional, and with no
/// fallback: a game without it plays until somebody fires, and then refuses the shot naming the entry
/// it needs. Optional rather than required because a mismatched stored row is skipped and not
/// migrated, and because a table can enter it partly - only the entries a shot reads are needed.
/// </param>
public sealed record CreateStarGruntGameRequest(string Name, StarGruntRulesProfileDto? Profile = null);

/// <summary>One figure on a record card.</summary>
/// <param name="ArmourDie">Its personal armour die, as a face count.</param>
public sealed record StarGruntFigureDto(int ArmourDie);

/// <summary>One weapon on a record card.</summary>
/// <param name="Name">What the card calls it.</param>
/// <param name="ImpactDie">Its impact die, as a face count.</param>
/// <param name="IsSupport">True when it is a support weapon.</param>
/// <param name="SupportFirepowerDie">
/// The die it adds to a squad volley, as a face count. Zero when the card does not give one, which
/// is the ordinary case - most weapons never join a volley.
/// <para>
/// This defaulted to <c>6</c>, so every weapon anybody entered came back carrying a D6 it had never
/// been given: a die rating off a record card, written by the app, over the wire. It is the same
/// defect the exported armour die was. A weapon with no die here is refused by name if it is ever
/// asked to join a volley.
/// </para>
/// </param>
/// <param name="NeverJoinsSquadFire">True when it always needs an action of its own.</param>
/// <param name="IsCloseRange">True when it is effective only inside one band.</param>
public sealed record StarGruntWeaponDto(
    string Name,
    int ImpactDie,
    bool IsSupport = false,
    bool IsCloseRange = false,
    int SupportFirepowerDie = 0,
    bool NeverJoinsSquadFire = false);

/// <summary>Puts a unit on the table.</summary>
/// <param name="Id">How the game will name it. Must be unique in the game.</param>
/// <param name="Name">What the players call it.</param>
/// <param name="Side">Which side it belongs to.</param>
/// <param name="Level">Where it sits in the chain of command.</param>
/// <param name="QualityDie">Its quality die, as a face count.</param>
/// <param name="LeadershipValue">
/// Its Leadership Value, off its record card. Which numbers count as one is the game's own entry -
/// see <see cref="StarGruntRulesProfileDto.LowestLeadershipValue"/> - and a number this game's
/// profile does not hold is refused by name. This app ships no leadership ratings of its own.
/// </param>
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

/// <summary>Rolls to see whether troops have the nerve for a risky order.</summary>
/// <param name="UnitId">The unit being asked.</param>
/// <param name="ThreatLevel">
/// How much they are being asked to swallow, off the player's own table. Mission motivation does
/// not scale a reaction test.
/// </param>
public sealed record StarGruntReactionTestRequest(string UnitId, int ThreatLevel);

/// <summary>Declares that a unit's next move would take it out of cover or on to a located enemy.</summary>
/// <param name="UnitId">The unit.</param>
/// <param name="LeavesCover">True when the move is the risky sort.</param>
public sealed record StarGruntLeavesCoverRequest(string UnitId, bool LeavesCover);

/// <summary>Declares a close assault and rolls the attacker's nerve to make it.</summary>
/// <param name="AttackerId">The unit charging, which spends its whole activation on this.</param>
/// <param name="DefenderId">The single unit being charged.</param>
/// <param name="ThreatLevel">
/// What the charge asks of the attackers, off the player's own table. Supplied rather than looked
/// up here, like every other threat level in this app.
/// </param>
public sealed record StarGruntChargeRequest(string AttackerId, string DefenderId, int ThreatLevel = 0);

/// <summary>Rolls the defender's nerve to stand and receive a charge.</summary>
/// <param name="AttackerId">The unit charging.</param>
/// <param name="DefenderId">The unit being charged.</param>
/// <param name="Terror">
/// True when the attackers frighten people. Agreed between the players before the game, so it is
/// sent rather than looked up.
/// </param>
public sealed record StarGruntStandRequest(string AttackerId, string DefenderId, bool Terror = false);

/// <summary>One pair of figures, as the players have paired them off over the table.</summary>
/// <param name="AttackerShift">
/// How many die types the charging figure's close-combat weapon is worth, off the player's own
/// table. The app knows a weapon shifts the die and that the shift is open; it does not know what
/// any particular weapon is worth.
/// </param>
/// <param name="DefenderShift">What the receiving figure's weapon is worth.</param>
/// <param name="AttackerPowerArmour">True when the charging figure is in power armour.</param>
/// <param name="DefenderPowerArmour">True when the receiving figure is.</param>
public sealed record StarGruntMeleePairingDto(
    int AttackerShift = 0,
    int DefenderShift = 0,
    bool AttackerPowerArmour = false,
    bool DefenderPowerArmour = false);

/// <summary>Fights one round of melee, one exchange per pairing.</summary>
/// <param name="AttackerId">The charging unit.</param>
/// <param name="DefenderId">The receiving unit.</param>
/// <param name="Pairings">Who is fighting whom, which the players decide between them.</param>
/// <param name="DefendersInCover">True in the first round only, while the cover still counts.</param>
public sealed record StarGruntMeleeRequest(
    string AttackerId,
    string DefenderId,
    IReadOnlyList<StarGruntMeleePairingDto> Pairings,
    bool DefendersInCover = false);

/// <summary>Rolls what became of the figures a unit had downed, now the assault is over.</summary>
/// <param name="UnitId">The unit whose downed figures are being settled.</param>
/// <param name="Downed">How many of its figures went down.</param>
/// <param name="WonTheAssault">True when its side holds the ground at the finish.</param>
/// <param name="DeadUpTo">The highest roll that means dead, off the player's own table.</param>
/// <param name="WoundedUpTo">The highest roll that means wounded; above it is stunned.</param>
/// <param name="FateDie">
/// The die those bands are read against, as a face count, off the same table they came from. Zero
/// when the table did not say, which the game refuses rather than guessing at.
/// <para>
/// The engine threw a flat D6 here while taking the bands off the player, which is half a table: a
/// chart reading dead on 1-3 and wounded on 4-7 had its 8 to 10 stunned band made unreachable,
/// silently, because the die it was written for was never the die being rolled.
/// </para>
/// </param>
public sealed record StarGruntSettleDownedRequest(
    string UnitId,
    int Downed,
    bool WonTheAssault,
    int DeadUpTo = 0,
    int WoundedUpTo = 0,
    int FateDie = 0);

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
/// <param name="SupportWeapons">
/// Which of the unit's own support weapons are folded into this volley. Named rather than sent as
/// loose dice, so the game knows they have been used: a weapon folded in may not also fire on its
/// own that activation.
/// </param>
/// <param name="DistanceInches">How far apart the two units are, as measured at the table.</param>
/// <param name="Cover">The target's cover: None, Soft or Hard.</param>
/// <param name="InPosition">True when the target has settled into its ground.</param>
public sealed record StarGruntFireRequest(
    string FirerId,
    string TargetId,
    string WeaponName,
    int FirepowerDie,
    IReadOnlyList<string> SupportWeapons,
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
/// <param name="LeadershipValue">
/// Its Leadership Value, off its record card - or null when the card has not said, which a game
/// stored before units carried one reads back as. A screen showing a number here that nobody entered
/// is the defect; every roll in the game bar a shot is measured against it. Which numbers count as
/// one is this game's own entry, not this app's.
/// </param>
/// <param name="Fatigue">How worn it is, which caps its confidence.</param>
/// <param name="Figures">
/// The roster the player entered, at full strength, each figure with the armour die they chose.
/// Carried because it is the player's own data and nothing else on this snapshot holds it: a client
/// writing a force out to a file had nowhere to read an armour die from and wrote a number of its
/// own instead.
/// </param>
/// <param name="FiguresAlive">Figures still standing.</param>
/// <param name="FullStrength">Figures it started with, which is <paramref name="Figures"/>' count.</param>
/// <param name="FiguresWounded">Casualties the squad is carrying, out of the fight.</param>
/// <param name="IsLeaderDown">True once the squad leader has been hit.</param>
/// <param name="SuppressionMarkers">Suppression on it, from none to three.</param>
/// <param name="Confidence">How much fight it has left.</param>
/// <param name="IsDisorganised">True when it owes a reorganise before anything else.</param>
/// <param name="IsInCover">True when it has something to hide behind.</param>
/// <param name="NextMoveLeavesCover">True when its next move has been declared the risky sort.</param>
/// <param name="ReactionTestCleared">True when it has already found the nerve for that move.</param>
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
    int? LeadershipValue,
    string Fatigue,
    IReadOnlyList<StarGruntFigureDto> Figures,
    int FiguresAlive,
    int FullStrength,
    int FiguresWounded,
    bool IsLeaderDown,
    int SuppressionMarkers,
    string Confidence,
    bool IsDisorganised,
    bool IsInCover,
    bool NextMoveLeavesCover,
    bool ReactionTestCleared,
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
/// <param name="Profile">
/// The range table this game is played on, as the players entered it. On every snapshot, blank or
/// not, so a screen can say "this game has no range table" before the first shot is refused rather
/// than after.
/// </param>
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
    long Version,
    StarGruntRulesProfileDto? Profile = null);

/// <summary>What creating a game hands back.</summary>
/// <param name="GameId">The new game's id.</param>
/// <param name="Snapshot">Its opening state.</param>
/// <param name="Token">
/// The game token. Handed back here and nowhere else: every other route for this game requires it
/// in the <c>X-Game-Token</c> header, and no snapshot ever carries it. One token for the whole game,
/// because the screen is a hot seat - one device, both sides.
/// </param>
public sealed record StarGruntGameCreatedResponse(Guid GameId, StarGruntSnapshotDto Snapshot, string Token);
