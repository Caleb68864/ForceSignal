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
  // Which layer of the rules this match is played under. The layers replace parts of one another.
  rulesLayer?: string;
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
  figuresAlive: number;
  fullStrength: number;
  figuresWounded: number;
  isLeaderDown: boolean;
  suppressionMarkers: number;
  confidence: string;
  isDisorganised: boolean;
  isInCover: boolean;
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
};
export type StarGruntGameCreated = { gameId: string; snapshot: StarGruntSnapshot };
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
