/**
 * The shapes the API speaks and the shapes the screen edits.
 *
 * Everything here mirrors a contract in `ForceSignal.Contracts`, except the handful of view-only
 * types at the bottom - the order draft, the ship form, the firing draft - which exist only while
 * a player is part-way through entering something and never leave the browser.
 */

export type TurnDirection = 'None' | 'Port' | 'Starboard';
export type WeaponKind = 'Beam' | 'PulseTorpedo' | 'NeedleBeam';
export type FiringArc = 'Fore' | 'ForeStarboard' | 'AftStarboard' | 'Aft' | 'AftPort' | 'ForePort';
export type ShipIconKey = 'escort' | 'frigate' | 'destroyer' | 'cruiser' | 'carrier' | 'dreadnought' | 'fighter-group' | 'station';
export type FighterStatus = 'Docked' | 'Airborne' | 'Recovering';
export type TurnManeuver = {
  direction: Exclude<TurnDirection, 'None'>;
  steps: number;
};
export type MovementSegment = {
  course: number;
  distance: number;
};
export type Participant = {
  id: string;
  displayName: string;
  role: string;
  isReady: boolean;
  isConnected: boolean;
  ordersComplete?: boolean;
};
export type Fleet = {
  id: string;
  ownerParticipantId: string;
  name: string;
  faction?: string;
  fleetColor: string;
};
export type Ship = {
  id: string;
  fleetId: string;
  name: string;
  className?: string;
  thrustRating: number;
  currentVelocity: number;
  currentCourse: number;
  positionX: number;
  positionY: number;
  hullMax: number;
  hullDamage: number;
  armorMax: number;
  armorDamage: number;
  fireControlMax: number;
  fireControlDamage: number;
  pointDefenseSystems?: number;
  fighterBays?: number;
  fighterBayDamage?: number;
  damageControlParties?: number;
  driveDamage: number;
  weaponDamage: number;
  screenRating: number;
  screenDamage?: number;
  weapons: WeaponMount[];
  isDestroyed: boolean;
  // The hull damage track, as boxes per row. Completing a row triggers a threshold check.
  hullRows?: number[];
  hullRowsCompleted?: number;
  iconKey: ShipIconKey;
  fighterEnduranceMax: number;
  fighterEnduranceUsed: number;
  fighterMaxRange: number;
  fighterStatus: FighterStatus;
  homeCarrierShipId?: string | null;
  pointsValue: number;
  // Worked out by the server. A screen rating is what the ship was built with; these are what it
  // still has, and the arithmetic that gets from one to the other lives in one place.
  effectiveScreens: number;
  workingFireControl: number;
  fighterReach: number;
  repairableSystems: SystemOption[];
};
export type WeaponMount = {
  id: string;
  name: string;
  attackDice: number;
  maxRange: number;
  arcs: FiringArc[];
  ammoMax: number;
  ammoUsed: number;
  reloadTurns: number;
  isDestroyed?: boolean;
  isNeedleKilled?: boolean;
  kind: WeaponKind;
};
export type OrdnanceMarker = {
  id: string;
  ownerParticipantId: string;
  name: string;
  markerType: string;
  sourceShipId?: string | null;
  targetShipId?: string | null;
  positionX: number;
  positionY: number;
  course: number;
  speed: number;
  enduranceRemaining: number;
  attackDice: number;
  maxRange: number;
  status: string;
};
export type OrderStatus = {
  shipId: string;
  ownerParticipantId: string;
  isCommitted: boolean;
  isRevealed: boolean;
  verificationFailed: boolean;
};
export type RevealedOrder = {
  shipId: string;
  velocityDelta: number;
  turnSteps: number;
  turnDirection: TurnDirection;
  turnManeuvers?: TurnManeuver[] | null;
};
export type MovementResult = {
  shipId: string;
  startingVelocity: number;
  startingCourse: number;
  endingVelocity: number;
  endingCourse: number;
  segments?: MovementSegment[] | null;
};
/**
 * Where a draft order would take a ship, answered by the server.
 *
 * The client works none of this out. The split-turn geometry lives in one place - the resolver
 * that will fly the turn - so a preview cannot disagree with what actually happens.
 */
export type OrderPreview = {
  shipId: string;
  isValid: boolean;
  errors: string[];
  usableThrust: number;
  thrustSpent: number;
  maxTurnSteps: number;
  startingVelocity: number;
  startingCourse: number;
  endingVelocity: number;
  endingCourse: number;
  segments: MovementSegment[];
  /** The ship's current position, then the end of each leg. */
  path: TablePoint[];
  runsOffTable: boolean;
};
export type SystemOption = {
  key: string;
  label: string;
  kind: string;
  weaponId?: string | null;
};
/**
 * Whether a shot can be taken, and the numbers behind it, answered by the server.
 *
 * Held to the same checks firing is, so the console cannot offer a shot that would then be
 * refused. The client used to work this out itself and did it incompletely - it knew nothing of
 * ammunition, spent mounts, fighter endurance, or the fire control a needle beam claims.
 */
export type FiringSolution = {
  attackerShipId: string;
  targetShipId?: string | null;
  canFire: boolean;
  blocker?: string | null;
  targetArc?: string | null;
  mapRange: number;
  rangeDisagreesWithMap: boolean;
  toHitNumber?: number | null;
  workingFireControl: number;
  engagedTargetCount: number;
  targetScreens: number;
  needleTargets: SystemOption[];
};
export type FiringResult = {
  attackerShipId: string;
  targetShipId: string;
  weaponId: string;
  weaponName: string;
  turnNumber: number;
  range: number;
  rangeBand: string;
  arc: FiringArc;
  rawDice: number;
  rangePenalty: number;
  screenReduction: number;
  systemPenalty: number;
  damage: number;
  armorDamageApplied: number;
  hullDamageApplied: number;
  diceRolls: number[];
  weaponKind?: WeaponKind;
  toHitNumber?: number | null;
  isHit?: boolean | null;
};
export type MatchLogEntry = {
  sequence: number;
  timestamp: string;
  turnNumber: number;
  phase: string;
  category: string;
  message: string;
};
export type MatchSnapshot = {
  matchId: string;
  joinCode: string;
  name: string;
  phase: string;
  turnNumber: number;
  rulesProfileKey: string;
  tableWidth: number;
  tableDepth: number;
  participants: Participant[];
  fleets: Fleet[];
  ships: Ship[];
  orderStatuses: OrderStatus[];
  revealedOrders: RevealedOrder[];
  movementResults: MovementResult[];
  firingResults: FiringResult[];
  ordnanceMarkers: OrdnanceMarker[];
  matchLog: MatchLogEntry[];
  version: number;
  pointsLimit: number;
  // Every number this match is played against, supplied by the players. This app ships none.
  rules?: RulesProfile;
  // The ship part-way through its fire. Its threshold checks roll when the volley closes.
  firingShipId?: string | null;
  // Whose turn it is to pick a ship and fire it, and which ships have already had their turn.
  firingParticipantId?: string | null;
  activatedShipIds?: string[];
};
export type Session = {
  matchId: string;
  participantId: string;
  participantToken: string;
  joinCode: string;
};
export type MatchSeat = {
  participantId: string;
  displayName: string;
  role: string;
  isClaimed: boolean;
  fleetCount: number;
  shipCount: number;
};
export type PendingRestore = {
  matchId: string;
  joinCode: string;
  seats: MatchSeat[];
  note: string;
};
export type MatchRestored = {
  matchId: string;
  joinCode: string;
  reusedJoinCode: boolean;
  restoredPhase: string;
  lockedOrdersDropped: boolean;
  seats: MatchSeat[];
};
export type MatchIdentity = {
  matchId: string;
  joinCode: string;
  hasUnclaimedSeats: boolean;
};
export type DraftOrder = {
  velocityDelta: number;
  turnSteps: number;
  turnDirection: TurnDirection;
  turnManeuvers?: TurnManeuver[];
  salt: string;
};
export type ShipForm = {
  fleetName: string;
  faction: string;
  fleetColor: string;
  name: string;
  className: string;
  iconKey: ShipIconKey;
  thrustRating: number;
  currentVelocity: number;
  currentCourse: number;
  positionX: number;
  positionY: number;
  hullMax: number;
  armorMax: number;
  screenRating: number;
  fireControlMax: number;
  pointDefenseSystems: number;
  fighterBays: number;
  damageControlParties: number;
  weapons: WeaponMount[];
  fighterEnduranceMax: number;
  fighterEnduranceUsed: number;
  fighterMaxRange: number;
  fighterStatus: FighterStatus;
  homeCarrierShipId: string;
  pointsValue: number;
};
export type FiringDraft = {
  targetShipId: string;
  weaponId: string;
  range: number;
  // A needle beam names one system on the target; every other weapon ignores this.
  targetSystem?: string;
  targetSystemWeaponId?: string;
};
export type DamageState = Pick<Ship, 'hullDamage' | 'armorDamage' | 'fireControlDamage' | 'driveDamage' | 'weaponDamage'>;
export type TablePoint = {
  x: number;
  y: number;
};
export type FleetExportShip = {
  name: string;
  className: string;
  iconKey: ShipIconKey;
  thrustRating: number;
  initialVelocity: number;
  initialCourse: number;
  startX: number;
  startY: number;
  hullMax: number;
  armorMax: number;
  screenRating: number;
  fireControlMax: number;
  pointDefenseSystems: number;
  fighterBays: number;
  damageControlParties: number;
  weapons: WeaponMount[];
  fighterEnduranceMax: number;
  fighterEnduranceUsed: number;
  fighterMaxRange: number;
  fighterStatus: FighterStatus;
  homeCarrierShipId?: string | null;
  homeCarrierName?: string | null;
  pointsValue: number;
};
export type SavedFleet = {
  savedAt: string;
  fleet: FleetExport;
};
export type FleetExport = {
  schema: 'forcesignal-fleet-1';
  gameSystem: 'space-fleet-compatible';
  name: string;
  faction: string;
  fleetColor: string;
  ships: FleetExportShip[];
};
/// One job a damage control party can be put on.
export type RepairJob = { kind: string; weaponId?: string | null; parties: number };

/**
 * StarGrunt II, which is its own game rather than a view of a match: no room code, no seats, one
 * device passed around the table.
 *
 * Every die is a face count - 4, 6, 8, 10, 12 - that the user typed off their own record card. The
 * app ships no stats, and nothing here works out whether an action is legal: that arrives on the
 * unit, from the same checks the server enforces.
 */
export type StarGruntFigure = { armourDie: number };
export type StarGruntWeapon = {
  name: string;
  impactDie: number;
  isSupport: boolean;
  isCloseRange: boolean;
  supportFirepowerDie: number;
  neverJoinsSquadFire: boolean;
};
export type StarGruntWeaponLegality = { name: string; canFire: boolean; blocker?: string | null };
export type StarGruntUnit = {
  id: string;
  name: string;
  side: string;
  level: string;
  qualityDie: number;
  leadershipValue: number;
  fatigue: string;
  // The roster the player entered, at full strength, each figure carrying the armour die they
  // chose. Export reads the dice from here: there is nowhere else on a snapshot that holds them.
  figures: StarGruntFigure[];
  figuresAlive: number;
  fullStrength: number;
  figuresWounded: number;
  isLeaderDown: boolean;
  suppressionMarkers: number;
  confidence: string;
  isDisorganised: boolean;
  isInCover: boolean;
  nextMoveLeavesCover: boolean;
  reactionTestCleared: boolean;
  hasActivated: boolean;
  weapons: StarGruntWeapon[];
  canActivate: boolean;
  activationBlocker?: string | null;
  weaponLegality: StarGruntWeaponLegality[];
};
export type StarGruntSnapshot = {
  gameId: string;
  name: string;
  turnNumber: number;
  phase: string;
  sides: string[];
  activeSide?: string | null;
  activatingUnitId?: string | null;
  firstActivationChooser?: string | null;
  units: StarGruntUnit[];
  log: string[];
  version: number;
  // The range table this game is played on, as the players entered it. Empty is a real answer:
  // this app ships no range table, and a game whose entries are missing is told so before it fires.
  profile?: StarGruntRulesProfile | null;
};

/**
 * The numbers a StarGrunt shot reads off the rulebook's range page, and the cover a melee reads, as
 * the players entered them.
 *
 * Nothing here is this app's. A row nobody entered is absent and a number nobody entered is null -
 * not zero, which is a real answer for a cover shift - and the server refuses whatever would have
 * read it, naming the entry.
 */
export type StarGruntRulesProfile = {
  bandWidths: { qualityDie: number; inches: number }[];
  rangeDice: { bandsOut: number; die: number }[];
  effectiveBands?: number | null;
  softCoverShift?: number | null;
  hardCoverShift?: number | null;
  inPositionShift?: number | null;
  meleeCoverShift?: number | null;
};
/**
 * What this device needs to get back into a ground game: the id names it, and the token - minted
 * once, on create, and never returned again - is what every later request must present. One token
 * covers both sides, because these are hot-seat screens on a single device.
 */
export type GameHandle = { gameId: string; token: string };
// Which game a device is running, and which tab of the Full Thrust workspace it is on. Both are
// kept in storage so a refresh lands back where the player was.
export type GameMode = 'fullthrust' | 'stargrunt' | 'dirtside';
export type BattleView = 'ships' | 'map' | 'log';
export type StarGruntGameCreated = GameHandle & { snapshot: StarGruntSnapshot };
export type FeatureFlags = { starGrunt: boolean; dirtside: boolean };
/** A force as it is written to a file, so it survives the game it was built for. */
export type StarGruntForceFile = {
  formatVersion: number;
  side: string;
  units: {
    id: string;
    name: string;
    level: string;
    qualityDie: number;
    leadershipValue: number;
    fatigue: string;
    figures: StarGruntFigure[];
    weapons: StarGruntWeapon[];
  }[];
};

/** What one beam die scores against one level of screening. */
export type BeamDamageEntry = { dieFace: number; screenLevel: number; damage: number };

/** What one point-defence die shoots down. */
export type PointDefenseEntry = { dieFace: number; kills: number };

/** What a landed fighter group's turnaround roll means for it. */
export type TurnaroundEntry = { dieFace: number; isGroundedForGame: boolean; turnsBeforeRelaunch: number };

/**
 * Every number a match is played against, off the players' own rulebook and record cards.
 *
 * ForceSignal ships none of these. There is deliberately no default profile here or on the server:
 * a blank one is what a new match starts from, and the server refuses to play against an incomplete
 * one rather than filling the gaps in with somebody's published numbers.
 */
export type RulesProfile = {
  name: string;
  dieFaces: number;
  beamDamage: BeamDamageEntry[];
  beamRangeBandWidth: number;
  maxScreenLevel: number;
  torpedoMaximumRange: number;
  torpedoBandWidth: number;
  torpedoBestToHit: number;
  needleBeamRange: number;
  needleSystemKillRoll: number;
  enhancedNeedleBeams: boolean;
  needleHullDamageRoll: number;
  thresholdRows: 'FixedRows' | 'ByShipClass';
  thresholdRowCount: number;
  escortRowCount: number;
  cruiserRowCount: number;
  maxPartiesPerJob: number;
  repairRollWithOneParty: number;
  repairBestRoll: number;
  fighterMoveAllowance: number;
  carrierRatesFollowBays: boolean;
  trueCarrierAllowance: number;
  otherShipAllowance: number;
  carrierTurnaroundRoll: boolean;
  turnaround: TurnaroundEntry[];
  pointDefenseRange: number;
  pointDefenseKills: PointDefenseEntry[];
  pointDefenseChainOnFace: number;
  missilesPerSalvo: number;
  salvoAttackRadius: number;
};

/** A profile with nothing filled in - the starting point for entering your own numbers. */
export const blankRulesProfile: RulesProfile = {
  name: '',
  dieFaces: 0,
  beamDamage: [],
  beamRangeBandWidth: 0,
  maxScreenLevel: 0,
  torpedoMaximumRange: 0,
  torpedoBandWidth: 0,
  torpedoBestToHit: 0,
  needleBeamRange: 0,
  needleSystemKillRoll: 0,
  enhancedNeedleBeams: false,
  needleHullDamageRoll: 0,
  thresholdRows: 'FixedRows',
  thresholdRowCount: 0,
  escortRowCount: 0,
  cruiserRowCount: 0,
  maxPartiesPerJob: 0,
  repairRollWithOneParty: 0,
  repairBestRoll: 0,
  fighterMoveAllowance: 0,
  carrierRatesFollowBays: false,
  trueCarrierAllowance: 0,
  otherShipAllowance: 0,
  carrierTurnaroundRoll: false,
  turnaround: [],
  pointDefenseRange: 0,
  pointDefenseKills: [],
  pointDefenseChainOnFace: 0,
  missilesPerSalvo: 0,
  salvoAttackRadius: 0,
};

/** What has happened to one Dirtside element, as the table sees it. */
export type DirtsideElementState = {
  id: string;
  name: string;
  isDestroyed: boolean;
  isDamaged: boolean;
  isSystemsDown: boolean;
  // A Mobility chit. It will never move again, though it may still fire.
  isImmobilised?: boolean;
  movedOverHalf: boolean;
  areaDefenceSensorsLive: boolean;
  // How far it may go now, in the player's own units: the number off its record card, halved by the
  // server while it carries a DMG marker. Rendered rather than recomputed - the halving is a rule,
  // and rules live on the server.
  movement: number;
  // True when it has said what it is doing in the open activation. The activation cannot close
  // until every element still on the table has, because sitting out gives up the whole turn.
  hasChosen: boolean;
  // The move and the combat action are separate: an element that has moved may still shoot.
  hasMoved: boolean;
  hasTakenCombatAction: boolean;
  hasStoodDown: boolean;
  weapons: string[];
  // Off the record card. Null when the card does not say, in which case the element cannot go into
  // or receive a close assault - nothing is defaulted.
  hasBackupSystems?: boolean;
  assaultChits?: number | null;
  killThreshold?: number | null;
  // Whether its crew could try to get a Systems Down marker off right now, and if not, why not.
  canRecoverSystems?: boolean;
  whyItCannotRecoverSystems?: string | null;
  // Whether this element is eligible to answer an area-defence interception - alive, systems up, a
  // combat action already spent on live sensors, and a reach entered on the game's rules profile.
  //
  // Eligibility, not capability. Answering is refused even when this is true: nothing in this app
  // can resolve an interception, because what it rolls and what a success does to the shot are rules
  // nobody has written down. This says which vehicle would answer if it could, which is what the
  // combat action bought.
  canIntercept?: boolean;
  whyItCannotIntercept?: string | null;
};

/** A Dirtside platoon as the table sees it. */
export type DirtsidePlatoonState = {
  id: string;
  name: string;
  side: string;
  kind: string;
  isCybertank: boolean;
  confidence: string;
  isUnderFire: boolean;
  isDisorganised: boolean;
  hasActivated: boolean;
  canActivate: boolean;
  // Why not, in the words the command would refuse with, so a disabled button never invents its own.
  whyItCannotActivate?: string | null;
  elements: DirtsideElementState[];
  // Off the command marker: D4 to D12, and the leadership value. Null when the card does not say,
  // in which case the platoon can do everything except launch or receive an assault.
  qualityDie?: string | null;
  leadershipValue?: number | null;
};

/** A close assault part-way through being fought, as the table sees it. */
export type DirtsideAssaultState = {
  attackerUnitId: string;
  defenderUnitId: string;
  // What is owed next: AwaitingDefender, AwaitingRound, AwaitingAftermath or AwaitingFollowThrough.
  stage: string;
  // The round about to be fought, or just fought, counting from one.
  round: number;
  // The committed elements still standing. The defender's list is empty until it has stood.
  attackerElementIds: string[];
  defenderElementIds: string[];
};

/** A whole Dirtside game as the table sees it. */
export type DirtsideSnapshot = {
  gameId: string;
  name: string;
  turnNumber: number;
  phase: string;
  sides: string[];
  activeSide?: string | null;
  activatingUnitId?: string | null;
  elementsStillToChoose: string[];
  canEndActivation: boolean;
  whyActivationCannotEnd?: string | null;
  // The assault being fought, or null when none is.
  assault?: DirtsideAssaultState | null;
  units: DirtsidePlatoonState[];
  log: string[];
  version: number;
  // What this game's chit pot holds, so the table can read back the counts it is playing on.
  chitPot?: DirtsideChitPot | null;
  // The dice this game is settled with, as the players entered them. Empty tables are a real
  // answer: this app ships no dice, and a game whose rows are missing is told so before it fires.
  profile?: DirtsideRulesProfile | null;
};

/** One row of a die table: a key off the record card, and the die it rolls. */
export type DirtsideDieRow = { key: string; die: string };

/**
 * Every die a Dirtside game is settled with, off the players' own rulebook.
 *
 * Nothing here is this app's. A row nobody entered is absent rather than zero, and the server
 * refuses the shot that would have read it, naming the row. So an empty table is not a broken
 * profile - it is a profile whose owners have not needed that row yet.
 */
export type DirtsideRulesProfile = {
  fireControl: DirtsideDieRow[];
  posture: DirtsideDieRow[];
  signature: DirtsideDieRow[];
  systemsDownRecoveryDie?: string | null;
  systemsDownRecoveryRoll: number;
  systemsDownRecoveryRollWithBackup: number;
  // How far an area-defence system reaches, in the table's own units. The one piece of interception
  // that is a number rather than a rule, and so the one piece this app can hold. Zero is "not
  // entered", and an element whose game has not got one is not called eligible to intercept.
  areaDefenceReach: number;
};

/** How many chits of one colour and number the pot holds. Counted off the user's own sheet. */
export type DirtsideNumericalChits = { colour: string; value: number; count: number };

/** How many of one special chit the pot holds. Counted off the user's own sheet. */
export type DirtsideSpecialChits = { special: string; count: number };

/**
 * Everything in the chit pot. Every count here is the user's, off their own counter sheet - except
 * where `isBuiltInDefaultGuess` says otherwise, which is the server admitting the numbers are its
 * own guess rather than anybody's reading of a sheet.
 */
export type DirtsideChitPot = {
  numericals: DirtsideNumericalChits[];
  specials: DirtsideSpecialChits[];
  isBuiltInDefaultGuess: boolean;
};

/** A Dirtside game that has just been started. */
export type DirtsideGameCreated = GameHandle & { snapshot: DirtsideSnapshot };
