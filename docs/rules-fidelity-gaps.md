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

## Gap 1 — Threshold checks do not exist

**Rules** (`Damage/Threshold Check.md`): each time accumulated damage completes a hull *row*,
roll one die per surviving system. FTL kills a system on 1 at the first threshold, 1-2 at the
second, 1-3 at the third. If one attack crosses several rows, roll only for the worst threshold
reached and add 1 per extra row passed. Completing the fourth row destroys the ship.

**App**: no threshold mechanic anywhere - the word does not appear in the source. Hull is a
single counter (`HullDamage` / `HullMax`) with no rows, and `FireControlDamage`, `DriveDamage`,
`WeaponDamage` only change when a player hand-edits them through the damage panel.

**Why it matters**: this is the mechanism by which ships degrade in Full Thrust. Without it a
battle is a flat hull-point race - no drives knocked out, no firecon lost, no reason to focus
fire on a nearly-broken ship. It is the single largest divergence found.

**Note**: `docs/roadmap.md` currently lists "Damage detail: threshold checks" as complete.
That claim is wrong; what exists is manual system-damage counters and reminder text.

Code: `InMemoryMatchService.FireWeapon` (damage application), `ShipState` hull fields.

---

## Gap 2 — Four 90 degree fire arcs instead of six 60 degree arcs

**Rules** (`Core/Fire Arcs.md`): a ship has six 60 degree arcs, clockwise from dead ahead -
FORE, FORE STARBOARD, AFT STARBOARD, AFT, AFT PORT, FORE PORT. FT2 originally used four 90
degree arcs; Fleet Book 1 replaced them and six arcs is now standard, including in FTL.

**App**: `FiringArc` is `Fore, Aft, Port, Starboard, All` - the superseded four-arc scheme.

**Why it matters**: published ship data and SSDs are drawn in six arcs, so a real ship cannot be
transcribed into the app without distorting its firing coverage. Arcs also align with the
12-point course clock, which is how players read bearings at the table.

Code: `src/ForceSignal.Domain/Rules/CombatContracts.cs`, `firingArcs` in `main.tsx`.

---

## Gap 3 — No aft blind spot

**Rules** (`Core/Fire Arcs.md`): in Full Thrust Light **no weapon may fire out of the AFT arc** -
every weapon icon has that arc blacked in. Incoming fire can still come through it. (Fleet Book
optionally lets all-round turrets fire aft on a turn with no main-drive thrust; PDS may always
fire aft at fighters.)

**App**: `Aft` is a selectable mount arc and a legal firing arc, with no restriction.

**Why it matters**: the aft blind spot is what makes course and facing matter. Without it,
manoeuvring for position is largely pointless.

Code: `FiringArc.Aft`, `allowedFiringArcs` in `main.tsx`, arc check in `FireWeapon`.

---

## Gap 4 — Arc bearing is declared, never verified against geometry

**Rules**: a weapon may only fire at a target that is in a valid arc *during the firing phase*.

**App**: `FireWeapon` checks only that the declared arc matches the mount's arc
(`weapon.Arc != FiringArc.All && request.Arc != weapon.Arc`). Nothing checks that the target
actually lies in that arc relative to the attacker's course. A fore-only beam can hit a ship
dead astern by declaring "Fore".

**Why it matters**: both ships have map positions and the attacker has a course, so the true
bearing is computable. As it stands the only thing enforcing arcs is player honesty, which
defeats the purpose of having the app adjudicate.

Code: `InMemoryMatchService.FireWeapon` line ~999.

---

## Gap 5 — Fire control does nothing

**Rules** (`Defenses/Fire Control System (FCS).md`): each functioning FCS lets a ship engage
**one** target ship per turn; weapons split freely between targets but a single battery rolls
all its dice at one target. A ship that loses **all** FCS may not fire at all, even with working
weapons.

**App**: `FireControlDamage` is tracked and logged but never gates anything. There is no FCS
count on a ship, no target limit, and a ship with every firecon dead fires normally.

**Why it matters**: FCS is the whole reason to shoot at a cruiser's sensors instead of its hull,
and the target limit is what stops one ship spraying every mount at a different enemy.

Code: `InMemoryMatchService.FireWeapon`, `ShipState.FireControlDamage`.

---

## Gap 6 — No firing initiative or ship-by-ship alternation

**Rules** (`Core/Initiative.md`, `Core/Sequence of Play.md`): the firing phase opens with an
initiative roll. The winner picks **one** ship and resolves **all** of its fire; the opponent
then picks one ship and fires it fully; play alternates one ship at a time. Damage applies
immediately, so a ship can be destroyed or lose weapons **before it fires back**.

**App**: the Firing phase is open - either player may fire any weapon on any ship at any time,
in any order. Destroyed ships are correctly barred from firing, but nothing sequences who
shoots when.

**Why it matters**: initiative plus immediate damage is the tactical core of the fire phase.
Without ordering, "kill it before it shoots" - the main reason target priority exists - cannot
happen, and in practice whoever taps the screen faster gets the advantage.

Code: `InMemoryMatchService.FireWeapon`, `MatchPhase.Firing`.

---

## Gap 7 — A ship with no written order blocks the turn instead of drifting

**Rules** (`Movement/Movement Orders.md`): "No order = no change" - a ship with no order written,
or given impossible orders, simply continues on the **same course and velocity**. It still moves
its full velocity.

**App**: the turn cannot advance until *every* live ship has a locked and revealed order.
`CommitOrder` only reaches `OrdersLocked` when all live ship ids have commitments, and
`RevealOrder` only reaches `Movement` when all of them are revealed. A ship left unordered
deadlocks the turn rather than drifting.

**Why it matters**: with a dozen hulls on the table, most ships each turn are simply holding
course. The rules let you write nothing for those; the app demands an explicit order for every
one, every turn.

Code: `CommitOrder` and `RevealOrder` gates, `AdvanceTurn` movement loop.

---

## Gap 8 — Course change is not split half at the start and half at the mid-point

**Rules** (`Movement/Making Course Changes.md`): pivot half the total turn rounded **down**, move
half the velocity, pivot the remainder, move the rest. A 1-point turn therefore happens entirely
at the mid-point.

**App**: `FullThrustLightCinematicRules.Resolve` emits a full-length first segment on the
*starting* course, then pivots the whole turn, then moves the rest. For a 3-point turn at
velocity 10 from course 3, the rules run 5mu on course 2 then 5mu on course 12; the app runs
5mu on course 3 then 5mu on course 12. Final heading matches, **final position does not**.

**Why it matters**: position drives range, and range drives dice. This was already on the
roadmap as deferred; it should be treated as a play blocker rather than cosmetic.

Code: `src/ForceSignal.Modules.FullThrust/Movement/FullThrustLightCinematicRules.cs`.

---

## Gap 9 — A stationary ship cannot rotate on the spot

**Rules** (`Movement/Cinematic Movement.md`): a ship at velocity 0 may be rotated on the spot to
any course, spending no thrust and making no other move.

**App**: every turn costs thrust and is capped at half thrust, so a stopped hull or a thrust-0
station can never change facing.

Already on the roadmap. Code: `FullThrustLightCinematicRules.Validate`.

---

## Gap 10 — Pulse torpedoes are not modelled

**Rules** (`Weapons/Pulse Torpedoes.md`): FTL's second weapon. Range 30mu. Roll to hit by 6mu
band - 2+ at 0-6, 3+ at 6-12, 4+ at 12-18, 5+ at 18-24, 6 at 24-30 - then roll 1D6 for damage.
**Screens do not reduce it.**

**App**: every mount is a beam resolved through `FullThrustLightFiringRules`. There is no to-hit
step, no per-weapon-type damage model, and screens apply to everything.

**Why it matters**: FTL ships are pre-set generic designs that carry pulse torpedoes. Without
them, half of FTL's weapon list is missing and screens are strictly better than they should be.

Code: `IFiringResolver` / `FullThrustLightFiringRules` (single hard-wired beam profile).

---

## Gap 11 — Ordnance markers never attack

**Rules** (`Weapons/Salvo Missile Systems.md`): a salvo is announced in the fighter-movement
phase and a counter placed at the point of aim; after movement, if an enemy is within 6" the
salvo attacks it. One D6 sets how many of the 6 missiles arrive, PDS and screening fighters
intercept, then each survivor rolls a D6 for damage. Screens do not reduce it; armour halves it.

**App**: ordnance markers carry `AttackDice`, `MaxRange` and `Speed`, drift each turn, and expire
when endurance hits zero. They never resolve an attack against anything.

**Why it matters**: markers are currently a visual aid only. Any missile-heavy fleet is
unplayable without resolution, and armour/screen interaction is what makes missiles worth buying.

Code: `AdvanceOrdnanceMarkers` in `InMemoryMatchService`.

---

## Gap 12 — Fighter attacks use a generic mount, not per-fighter dice

**Rules** (`Fighters/Fighter Attacks.md`, `Fighter Groups.md`): a group is 1-6 fighters, moves up
to 12mu in any direction with no written orders, and attacks a ship within **6mu in the
fighters' fore arc**, rolling **one die per surviving fighter**, scored like beam fire, with
screens applying. Attacked or attacking spends one endurance for the turn.

**App**: a fighter group is a ship with `FighterEnduranceMax/Used`, `FighterMaxRange` and a status,
and its attack is an ordinary weapon mount with a fixed dice count. Group strength does not fall
as fighters die, the 6mu fore-arc restriction is not enforced, and endurance is spent manually.

Code: fighter fields on `ShipState`, `UpdateFighterOperations`.

---

## Gap 13 — No anti-fighter or point defence fire

**Rules** (`Defenses/Point Defence System (PDS).md`): main batteries cannot engage fighters.
Ships mount PDS with their own fire control (bypassing FCS), 6" range, 1D6 per system - 1-3
nothing, 4-5 kills one, 6 kills two. Fleet Book adds a reroll on 6, and Class-1 beams may act as
secondary point defence instead of firing offensively.

**App**: no PDS concept, so nothing stops a fighter strike and nothing intercepts ordnance.

Code: none - system absent.

---

## Gap 14 — Range is player-declared and never cross-checked

**Rules**: measure to and from the centre of each model's stand.

**App**: `FireWeaponRequest.Range` is typed by the player. The map offers "Use Map Range" but the
server accepts whatever number arrives and validates it only against the mount's maximum.

**Judgement**: defensible - the physical table is the source of truth, and that is this app's
stated stance. But the server already knows both positions, so it could warn when a declared
range disagrees with the map by more than a tolerance, which would catch transcription slips
without overriding the table.

Code: `InMemoryMatchService.FireWeapon`.

---

## Gap 15 — "Crippled at half hull" is an app invention

Full Thrust has no crippled state; ships fight at full effect until systems are knocked out by
threshold checks, then die when the last hull box goes. The app flags ships at or past half hull
as "crippled" in the contact card and the checklist.

**Judgement**: harmless as a table aid, but it should be labelled as an app convenience rather
than reading like a rule, so nobody plays a penalty that does not exist.

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

1. Threshold checks with hull rows (Gap 1) - biggest single fidelity win.
2. Six 60 degree arcs, aft blind spot, and a real bearing check (Gaps 2, 3, 4).
3. FCS gating and one target per firecon (Gap 5).
4. Drift for unordered ships (Gap 7).
5. Half-and-half course execution and rotation at rest (Gaps 8, 9).
6. Firing initiative and alternation (Gap 6).
7. Pulse torpedoes, then ordnance resolution and PDS (Gaps 10, 11, 13).
