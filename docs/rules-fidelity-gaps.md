# Rules Fidelity Gaps — ForceSignal vs Ground Zero Games Full Thrust

Scanned 2026-08-01 over five passes: movement, weapons and arcs, damage and defences,
fighters and ordnance, fleet points. Compared against the GZG rules notes in
`Ground Zero Games/Full Thrust/Rules` (which cite FTL, FT2, More Thrust, and Fleet Book 1).

The app declares its rules profile as `full-thrust-light-cinematic`, so **Full Thrust Light
is the fidelity target**. FTL is a clean subset of FT2 and includes: cinematic movement, the
three-phase turn with firing initiative, beam weapons, pulse torpedoes, fire control, hull
boxes, threshold checks, and 6 x 60 degree fire arcs with an aft blind spot.

Each gap below stands on its own: what the rules say, what the app does, why it matters at
the table, and where the code lives.

---

## Gap 1 — Threshold checks do not exist — FIXED 2026-08-01

**Rules** (`Damage/Threshold Check.md`): each time accumulated damage completes a hull *row*,
roll one die per surviving system. FTL kills a system on 1 at the first threshold, 1-2 at the
second, 1-3 at the third. If one attack crosses several rows, roll only for the worst threshold
reached and add 1 per extra row passed. Completing the fourth row destroys the ship.

**Was**: no threshold mechanic anywhere - the word did not appear in the source. Hull was a
single counter with no rows, and system damage only changed when a player hand-edited it.

**Now**: the hull is a damage track of four rows, as even as the hull allows with the remainder
weighted to the upper rows, so a 20-box hull is 5/5/5/5 and a 10-box hull is 3/3/2/2. When damage
completes a row, every surviving system rolls a die: drives, each fire control system, each screen
level, and each weapon mount. A system is knocked out on a 1 at the first threshold, 1-2 at the
second, 1-3 at the third. An attack that tears through more than one row rolls once against the
deepest row reached, one point worse per extra row. Completing the last row destroys the ship, so
no check is rolled for it, and a ship killed by the same damage rolls nothing.

Losses bite. A drive hit halves thrust and a second knocks the drives out, which immediately
limits what orders the ship can plot. A knocked-out mount cannot fire and is labelled in the
weapon picker. Screens drop a level per generator lost. Every check is logged with the faces it
rolled and what it cost, and the damage track in the UI marks the row boundaries so the table can
see where the next threshold sits.

**Timing**: the check waits until the firing ship is done, as the rules require, so one volley
earns one check against the deepest row it reached rather than a separate check per mount. Two
mounts that tear through two rows together roll once at the second threshold, one point worse for
the extra row - not a first-row check and then a second-row check.

A ship's fire ends when it says so ("Done Firing" in the console, `POST .../cease-fire`), and as a
backstop when another ship opens fire or the firing phase ends, so a check is never left unrolled.
The snapshot exposes `firingShipId` while a volley is open, and the console says a check is owed.

**Known edge**: a match exported mid-volley and restored loses the pending check. The damage is in
the snapshot; only the system-loss roll is skipped. Close the volley before exporting.

Also fixed a wrong roadmap claim: "Damage detail: threshold checks" had been ticked before any
threshold code existed.

Code: `ThresholdContracts.cs`, `FullThrustLightThresholdRules`, `ResolveThresholds` /
`SurvivingSystems` / `ApplySystemLoss` in `InMemoryMatchService`, `hullRowsOf` and `DamageMeter`
in `main.tsx`.

---

## Gap 2 — Four 90 degree fire arcs instead of six 60 degree arcs — FIXED 2026-08-01

**Rules** (`Core/Fire Arcs.md`): a ship has six 60 degree arcs, clockwise from dead ahead -
FORE, FORE STARBOARD, AFT STARBOARD, AFT, AFT PORT, FORE PORT. FT2 originally used four 90
degree arcs; Fleet Book 1 replaced them and six arcs is now standard, including in FTL.

**Was**: `FiringArc` was `Fore, Aft, Port, Starboard, All` - the superseded four-arc scheme.

**Now**: `FiringArc` is the six named arcs, clockwise from dead ahead: Fore, ForeStarboard,
AftStarboard, Aft, AftPort, ForePort. A mount carries a *set* of arcs rather than one, because a
battery bears through one, two, or three adjacent arcs and an all-round turret bears through
everything a weapon may fire through. The old `All` sentinel is gone: an all-round mount simply
lists every firable arc.

Data written by the old build still loads. `FiringArcJsonConverter` accepts the four-arc names for
a single arc, and `WeaponMountDto.Arc` is kept as a read-only compatibility field that is expanded
per mount: "Port" becomes both port arcs, "Starboard" both starboard arcs, "Aft" the two quarters
either side of the blind spot, and "All" every firable arc.

Code: `src/ForceSignal.Domain/Rules/CombatContracts.cs`, `FiringArcJsonConverter`,
`NormalizeArcs`/`ExpandLegacyArc` in `InMemoryMatchService`, `firingArcs`/`firableArcs` in `main.tsx`.

---

## Gap 3 — No aft blind spot — FIXED 2026-08-01

**Rules** (`Core/Fire Arcs.md`): in Full Thrust Light **no weapon may fire out of the AFT arc** -
every weapon icon has that arc blacked in. Incoming fire can still come through it. (Fleet Book
optionally lets all-round turrets fire aft on a turn with no main-drive thrust; PDS may always
fire aft at fighters.)

**Was**: `Aft` was a selectable mount arc and a legal firing arc, with no restriction.

**Now**: the aft arc is refused at three layers. Mount normalization strips it from any mount that
asks for it, the firing rules reject a solution whose target arc is aft regardless of the mount,
and the weapon editor never offers it. The arc still exists in the enum because incoming fire can
arrive through it and the bearing calculation has to be able to say "dead astern".

Code: `FiringArcs.Firable`/`CanFireThrough`, `FullThrustLightFiringRules.Validate`,
`firableArcs` in `main.tsx`.

---

## Gap 4 — Arc bearing is declared, never verified against geometry — FIXED 2026-08-01

**Rules**: a weapon may only fire at a target that is in a valid arc *during the firing phase*.

**Was**: `FireWeapon` checked only that the declared arc matched the mount's own arc. Nothing
checked that the target lay in that arc relative to the attacker's course, so a fore-only beam
could hit a ship dead astern by declaring "Fore".

**Now**: the arc is geometry, not a declaration. `FiringArcs.Bearing` works out which arc the
target sits in from the firing ship's course and the offset between the two ships, and that arc is
what the mount is held to and what the shot records. The client measures the same bearing and shows
it as a read-only readout instead of a picker, and disables Fire when the mount cannot bear.

`FireWeaponRequest.Arc` is now optional. When supplied it is a cross-check: a disagreement is
refused with a message naming the real bearing, which surfaces a client and a table that have
drifted apart rather than silently firing. Ship positions are editable, so the table can be
reconciled either way.

Range is still declared by the player - see gap 14 - because the physical table remains the source
of truth for distance. Arcs differ because an arc is discrete: there is no tolerance to apply, and
the app already knows the course it computed during movement.

Code: `FiringArcs.Bearing`, `BearingToTarget` and `FireWeapon` in `InMemoryMatchService`,
`bearingArc`/`arcBlocker` in `main.tsx`.

---

## Gap 5 — Fire control does nothing — FIXED 2026-08-01

**Rules** (`Defenses/Fire Control System (FCS).md`): each functioning FCS lets a ship engage
**one** target ship per turn; weapons split freely between targets but a single battery rolls
all its dice at one target. A ship that loses **all** FCS may not fire at all, even with working
weapons.

**Was**: `FireControlDamage` was tracked and logged but gated nothing. There was no firecon count,
no target limit, and a ship with every firecon dead fired normally.

**Now**: a ship carries a firecon count, and firing checks it twice. With none working the shot is
refused outright. Otherwise the ship may engage as many distinct target ships in a turn as it has
working firecons; any mount may fire at a target already engaged, but a fresh target beyond the
limit is refused with a message naming who the ship is already holding. Losing a firecon mid-turn
tightens the limit immediately without dropping targets already engaged.

The console shows the working firecon count beside the bearing and disables Fire with the reason,
so the limit is visible before a shot is attempted rather than only in an error.

**Not covered**: defensive systems bypassing fire control (point defence has its own), and needle
beams needing a firecon each. Both belong with the weapons that do not exist yet - gaps 10 and 13.

Code: `FireWeapon` in `InMemoryMatchService`, `workingFireControl` / `fireControlBlocker` in
`main.tsx`.

---

## Gap 6 — No firing initiative or ship-by-ship alternation — FIXED 2026-08-02

**Rules** (`Core/Initiative.md`, `Core/Sequence of Play.md`): the firing phase opens with an
initiative roll. The winner picks **one** ship and resolves **all** of its fire; the opponent
then picks one ship and fires it fully; play alternates one ship at a time. Damage applies
immediately, so a ship can be destroyed or lose weapons **before it fires back**.

**Was**: the firing phase was open - either player could fire any weapon on any ship at any time,
in any order, so whoever tapped the screen faster got the advantage.

**Now**: the phase opens with a die-off. Every player with a ship on the table rolls, the highest
takes the initiative, ties are re-rolled, and the rolls go in the log. The holder picks one ship and
fires all of it; firing out of turn is refused by name, and so is swapping to another ship while one
is mid-volley. Finishing that ship ("Done Firing") rolls its threshold checks and passes the turn,
and declining to fire at all ("Hold Fire") spends the ship's turn the same way. Play alternates a
ship at a time, skipping a player with nothing left to fire, until every ship has had its turn.

Because damage lands as it is rolled, a ship really can lose its guns - or its life - before its own
turn comes round, which is the point of the sequence.

The console shows whose turn it is and disables Fire with the reason, so the ordering is visible
rather than only enforced at the API.

**Known edge**: an exported snapshot carries no turn order, so a restored firing phase rolls a fresh
die-off rather than resuming the old one.

Code: `RollFiringInitiative` / `PassFiringInitiative` / `CanTakeFiringTurn` in
`InMemoryMatchService`, `firingTurnBlocker` in `main.tsx`.

---

## Gap 7 — A ship with no written order blocks the turn instead of drifting — FIXED 2026-08-02

**Rules** (`Movement/Movement Orders.md`): "No order = no change" - a ship with no order written,
or given impossible orders, simply continues on the **same course and velocity**. It still moves
its full velocity.

**Was**: the turn could not advance until every live ship had a locked and revealed order, so a
ship left unordered deadlocked the turn. The client hid this by fabricating a hold order for every
unplotted hull.

**Now**: plotting closes when each player says it is done, and a ship with no order written holds
the course and speed it already had - still travelling its full velocity, and logged as having held
course. Reveal only covers orders that were actually locked, and if nobody wrote anything the turn
goes straight to movement. The client's fleet lock commits only the ships the player actually
plotted and then declares, so a fleet needs orders only for the ships it is steering.

Code: `DeclareOrdersComplete` / `DriftingShipIds` / `AdvanceOrderEntryPhase` in
`InMemoryMatchService`, `lockOwnedOrders` in `main.tsx`.

---

## Gap 8 — Course change is not split half at the start and half at the mid-point — FIXED 2026-08-02

**Rules** (`Movement/Making Course Changes.md`): pivot half the total turn rounded **down**, move
half the velocity, pivot the remainder, move the rest. A 1-point turn therefore happens entirely
at the mid-point.

**Was**: the resolver ran a full-length first leg on the *starting* course, then pivoted the whole
turn at once. The heading came out right but by way of a path the ship never flew, so the final
position was wrong - and position drives range, which drives dice.

**Now**: both of the rulebook's worked examples come out right. A three-point turn at velocity 10
from course 3 pivots one point to course 2, runs 5, pivots two to course 12, runs 5. A single-point
turn at velocity 14 runs 7 on the original heading, then turns, then runs 7. A plotted sequence of
turns - a ForceSignal extension on top of the rules - gives each turn an equal share of the move and
splits it the same way, and legs that share a heading are merged so the trail carries no needless
kinks. The client's plotted-endpoint preview walks the same legs, so the preview matches the result.

Positions are also rounded to a thousandth of a unit: the sine of a straight-down course is not
exactly zero in floating point, and the residue was accumulating into coordinates like
20.000000000000001.

Code: `FullThrustLightCinematicRules.Resolve`, `EstimatePosition` in `InMemoryMatchService`,
`plannedSegments` in `main.tsx`.

---

## Gap 9 — A stationary ship cannot rotate on the spot — FIXED 2026-08-02

**Rules** (`Movement/Cinematic Movement.md`): a ship at velocity 0 may be rotated on the spot to
any course, spending no thrust and making no other move.

**Was**: every turn cost thrust and was capped at half thrust, so a stopped hull - or a thrust-0
station at any time - could never change facing.

**Now**: a ship at velocity 0 that makes no other move may rotate to any heading for free, ignoring
both the thrust cost and the half-thrust cap. Getting under way is still a manoeuvre and still has
to fit inside the thrust rating, and a rotation beyond a full circle is refused as nonsense.

Code: `FullThrustLightCinematicRules.Validate`.

---

## Gap 10 — Pulse torpedoes are not modelled — FIXED 2026-08-02

**Rules** (`Weapons/Pulse Torpedoes.md`): FTL's second weapon. Range 30mu. Roll to hit by 6mu
band - 2+ at 0-6, 3+ at 6-12, 4+ at 12-18, 5+ at 18-24, 6 at 24-30 - then roll 1D6 for damage.
**Screens do not reduce it.**

**Was**: every mount was a beam. There was no to-hit step, no per-weapon-type damage model, and
screens applied to everything - which made screens strictly better than the rules allow.

**Now**: a mount declares its kind, and a pulse torpedo launcher resolves through its own rules: one
roll to hit against a number that worsens every 6mu - 2+ inside 6, out to a 6 at 30 - and then, on a
hit, a die whose face is the damage. Screens do not touch it, which the log says explicitly on a hit
so nobody wonders why a screened ship took six points. Range beyond 30mu is refused, and arcs and the
aft blind spot apply exactly as they do to a beam.

The firing console shows the number a tube needs at the plotted range before the shot, and the
after-shot readout reports what it needed and rolled. The cruiser preset carries a tube alongside its
beams, so the weapon is reachable without hand-building a ship.

Code: `FullThrustLightPulseTorpedoRules`, `WeaponKind` in `CombatContracts.cs`, resolver selection in
`InMemoryMatchService.FireWeapon`, `torpedoToHitNumber` in `main.tsx`.

---

## Gap 11 — Ordnance markers never attack — FIXED 2026-08-02

**Rules** (`Weapons/Salvo Missile Systems.md`): a salvo is announced in the fighter-movement
phase and a counter placed at the point of aim; after movement, if an enemy is within 6" the
salvo attacks it. One D6 sets how many of the 6 missiles arrive, PDS and screening fighters
intercept, then each survivor rolls a D6 for damage. Screens do not reduce it; armour halves it.

**Was**: markers drifted and expired but never resolved an attack, so they were a visual aid and a
missile-heavy fleet was unplayable.

**Now**: a salvo is thrown at a point of aim, and the launch is refused if that point is past the
launcher's reach - 24mu for a standard load, 36 for extended range. After the ships have moved, and
before anyone opens fire, every salvo on the table resolves: one die says how many of its six missiles
arrived, the target's point defence shoots some down, and each survivor rolls a die whose face is its
damage - so a six is six points, far past a beam die's two. A salvo with nothing inside 6mu of its
point of aim is wasted and says so.

Screens do not reduce a salvo at all. Armour *halves* it rather than absorbing it: half the total,
rounded up, goes on armour and the remainder straight to the hull even while armour boxes stand. And
because the damage lands like any other, a salvo can fill a hull row and set off a threshold check -
which is exactly what happened in the live check, where a salvo's damage cost the target a screen
generator before the fighters arrived.

Code: `FullThrustSalvoMissileRules`, `ResolveSalvoMissiles` / `ApplyMissileDamage` in
`InMemoryMatchService`, launch reach in `CreateOrdnanceMarker`.

---

## Gap 12 — Fighter attacks use a generic mount, not per-fighter dice — FIXED 2026-08-02

**Rules** (`Fighters/Fighter Attacks.md`, `Fighter Groups.md`): a group is 1-6 fighters, moves up
to 12mu in any direction with no written orders, and attacks a ship within **6mu in the
fighters' fore arc**, rolling **one die per surviving fighter**, scored like beam fire, with
screens applying. Attacked or attacking spends one endurance for the turn.

**Was**: a group's attack was an ordinary mount with a fixed dice count. Strength never fell as
fighters died, and endurance was spent by hand.

**Now**: a group rolls one die per *surviving* fighter. A group's hull boxes stand for its aircraft,
so losses come straight off its dice - a six-strong flight that loses four rolls two next time. The
6mu reach and the fore arc come from the mount and are enforced by the same firing path as any beam,
screens still protect the target from fighter fire, and a group with no fighters left cannot attack.

Endurance is spent automatically: one turn per turn of combat, whether the group attacks or is
attacked, counted once however much fighting happens in that turn. A group that has spent its last
turn of endurance is refused and told to go home and rearm.

Code: `SurvivingFighters` / `EffectiveAttackDice` / `SpendFighterEndurance` in `InMemoryMatchService`.

---

## Gap 13 — No anti-fighter or point defence fire — FIXED 2026-08-02

**Rules** (`Defenses/Point Defence System (PDS).md`): main batteries cannot engage fighters.
Ships mount PDS with their own fire control (bypassing FCS), 6" range, 1D6 per system - 1-3
nothing, 4-5 kills one, 6 kills two. Fleet Book adds a reroll on 6, and Class-1 beams may act as
secondary point defence instead of firing offensively.

**Was**: no point defence at all, so a fighter strike or a salvo was unopposed.

**Now**: a ship carries a number of point defence systems, each rolling a die against fighters and
missiles alike - 1 to 3 does nothing, a 4 or 5 kills one, a 6 kills two and rolls again, chaining
while the sixes last. They have their own fire control, so they do not draw on the ship's firecons,
they reach 6mu, and they may fire through the aft arc.

An incoming fighter strike is met on the way in: kills come off the group's strength before its
surviving fighters roll, and a strike that is wiped out never attacks at all. A salvo is intercepted
the same way before its survivors roll damage. Allocation is declared before the dice, so kills past
the size of the threat are wasted and the log says how many were thrown away.

**Not covered**: area defence fire control, which would let a ship defend another within 6mu; and
Class-1 beams standing in as secondary point defence, which needs an interlock against firing them
offensively the same turn. Both are options on top of the base system.

Code: `FullThrustPointDefenseRules`, `ResolvePointDefenseAgainstFighters` in `InMemoryMatchService`.

---

## Gap 14 — Range is player-declared and never cross-checked — FIXED 2026-08-02

**Rules**: measure to and from the centre of each model's stand.

**Was**: the declared range was accepted with no comparison against the map at all.

**Now**: the range is still the player's to declare - the table remains the authority on distance and
a shot is never refused over it - but a disagreement is said out loud. The check is tied to range
*bands* rather than an arbitrary tolerance, because a band is what actually changes the dice: a beam
loses one every 12mu and a torpedo's to-hit number worsens every 6mu. A declared range is flagged
when it falls in a different band than the map measures, or when the two numbers are more than half a
band apart, which usually means a mistyped range or a ship nobody dragged to where it really sits.

The firing console says so before the shot, where it can still be fixed, and the shot records both
the map range and whether they disagreed so the log entry survives into the after-action record.

This is worth having precisely because arcs are now derived from map positions (gap 4): if the map is
trusted to decide which arc a target is in, a silent disagreement about distance is an inconsistency.

Code: `MapRangeBetween` / `RangeDisagreesWithMap` in `InMemoryMatchService`,
`rangeDisagreesWithMap` in `main.tsx`.

---

## Gap 15 — "Crippled at half hull" is an app invention — FIXED 2026-08-02

Full Thrust has no crippled state; ships fight at full effect until systems are knocked out by
threshold checks, then die when the last hull box goes. The app flags ships at or past half hull
as "crippled" in the contact card and the checklist.

**Now**: the word "crippled" is gone. The contact card reads "half hull" as a plain fact, and the
checklist says "N ships at or past half hull (watch list, not a rule)" so nobody plays a penalty that
does not exist. A ship still fights at full effect until a threshold check takes its systems, and
dies when the last hull box goes.

Code: `main.tsx` contact card and pre-turn checklist.

---

## Not gaps — verified correct this pass

- Beam dice by range band: Class N rolls N dice at 0-12mu, losing one per further 12mu band.
- Per-die damage: 1-3 miss, 4-5 one point, 6 two points.
- Screens downgrade dice rather than removing them: level 1 ignores 4s, level 2 caps hits at one,
  level 3 counts only 6s as one.
- Armour absorbs point-for-point before hull, with no threshold at the armour row.
- Turning allowance capped at half thrust rounded **up**, which matches FTL (Fleet Book rounds
  down; FT2 rounds up).
- Course clock: 12 points of 30 degrees, starboard adds, port subtracts, wrapping 12 to 1.
- Fleet matching by points per player, with ship points values, which is how Full Thrust matches
  forces (NPV, not mass).
- Reroll-on-6 is absent, which is **correct** for FTL and FT2 - it is a Fleet Book 1 optional
  rule. It becomes a gap only if the app adds a Fleet Book profile.
- Level-3 screens exist, correct for FT2. Fleet Book has no level 3, so this belongs behind a
  rules-layer switch if that profile is ever added.

---

## Suggested order of work to reach a playable match

1. ~~Six 60 degree arcs, aft blind spot, and a real bearing check (Gaps 2, 3, 4).~~ Done.
2. ~~Threshold checks with hull rows (Gap 1).~~ Done.
3. ~~FCS gating and one target per firecon (Gap 5).~~ Done.
4. ~~Drift for unordered ships (Gap 7).~~ Done.
5. ~~Half-and-half course execution and rotation at rest (Gaps 8, 9).~~ Done.
6. ~~Firing initiative and alternation (Gap 6).~~ Done.
7. ~~Pulse torpedoes (Gap 10).~~ Done.
8. ~~Ordnance attack resolution, fighter group strength, and point defence (Gaps 11, 12, 13).~~ Done.

Every gap found in the original scan is now closed. What remains are the options recorded inside the
entries above rather than gaps in the base game: area defence fire control, Class-1 beams as secondary
point defence, fighter morale and dogfighting, and a rules-layer switch for the profiles that differ
between FT2 and the Fleet Books.
