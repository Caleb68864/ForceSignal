# Dirtside II Rules Fidelity Gaps

_First audit 2026-08-29, written from the code and the resolvers' own remarks, against the Dirtside II
mechanics as the vault digest at `Notes/Ground Zero Games/Dirtside II` describes them. Same treatment
`rules-fidelity-gaps.md` gave Full Thrust and `stargrunt-fidelity-gaps.md` gave StarGrunt._

No rule text, tables or numbers from the source are reproduced here. Each gap names what the code
does, what the rules require, and what to change - the decision, not the rule. Every number a
mechanic reads is the player's, off their own record card, and this document keeps it that way.

## Why this audit exists

Dirtside is the largest module in the repo and the one the shared sequence layer was shaped around,
and it has never been scanned. StarGrunt's scan found an engine in good shape and a game layer that
did not call most of it; Dirtside is the same story, with one difference worth saying up front. Its
game layer was built as a first playable slice - activation, movement, direct fire - and the
readiness warning at `src/ForceSignal.Api/Program.cs:397-399` already admits what is missing. This
document makes that admission specific, file by file, so a later scan does not re-find it.

Gaps 1, 2, 3, 12 and 13 were closed the same day the audit was written, by work package A of the
forge pass; their entries record what landed and what was deliberately left out.

Three things were compared for each mechanic: whether a resolver exists, whether `DirtsideGame`
calls it, and whether a player can reach it over HTTP and from the screen. Most gaps below are in
the second and third of those.

---

## Gap 1 - Close assault is built, tested and unreachable - FIXED 2026-08-29

**Severity: high.** The whole exchange was in `src/ForceSignal.Modules.Dirtside/Combat/CloseAssault.cs`:
the attacker's nerve to launch (`Launch`, line 270, refused outright when the ladder forbids it,
`MayLaunch` line 246), the defender's choice to stand or withdraw (`StandOrWithdraw`, line 310,
with the armour crew that breaks without a roll coming from `DirtsideConfidence.OnCloseAssaulted`),
the simultaneous round with casualties marked rather than removed (`ResolveRound`, line 201), the
defender-tests-first aftermath (`ResolveAftermath`, line 374) and the follow-through
(`FollowThrough`, line 429). Cover stops counting from the second round by keying validity on the
round number (`AssaultSide.ValidityForRound`, line 34). Every threat level is a parameter. Tests:
`tests/ForceSignal.Modules.Dirtside.Tests/CloseAssaultTests.cs` and `CloseAssaultNerveTests.cs`.

The seams around it were in place too. `DirtsideAction.CloseAssault` exists
(`Sequence/DirtsideAction.cs:32`), the policy's nerve gate refuses it for a unit that will not charge
(`Sequence/DirtsideActivationPolicy.cs:382-386`), and `DirtsideTurn.CloseAssaulted`
(`Sequence/DirtsideTurn.cs:105`) turns the defender's marker over without giving it a frame.

**Was**: everything above the module was missing. `DirtsideGame` had no assault command, the service
no method, the API no route, the screen no control. The readiness warning named it.

**Fixed.** `Game/DirtsideGame.Assault.cs` sequences the resolvers as written and does not
reinterpret them: `LaunchAssault` (line 120, the reaction test, refused without a die when the
ladder forbids the charge), `DefenderStands` (line 257, the confidence test, or the break without a
test that `OnCloseAssaulted` reports), `FightAssaultRound` (line 334), `ResolveAssaultAftermath`
(line 410, defender first) and `FollowThrough` (line 518, a pass wiping the open frame's steps
through `DirtsideTurn.FollowThrough`, `Sequence/DirtsideTurn.cs:68` - the second go the resolver
describes, without the transfer machinery). Each command has a `Why*IsRefused` companion returning
the same words the snapshot shows. The assault is tracked state on the game (`DirtsideGame.cs:65`),
staged so each step is refused unless it is the one owed, saved and restored at every stage, and an
activation cannot close mid-assault but may walk away from a follow-through. Every threat level and
both validity rows come in on the request.

The two model holes are filled: `ElementDefinition` carries `AssaultChits` and `KillThreshold`
(`Game/PlatoonDefinition.cs:61-63`), optional, and an element whose card does not say them cannot be
committed; a side with mixed thresholds is refused before anything is spent, as the resolver
insists (`CloseAssault.cs:477`). The platoon's die and leadership are gap 3.

Six routes under `/api/dirtside/games/{gameId}/assaults/*` and
`.../activations/current/recover-systems` (`src/ForceSignal.Api/Endpoints/GroundCombatEndpoints.cs:410-480`),
all behind the game token. Tests: `tests/ForceSignal.Modules.Dirtside.Tests/DirtsideGameAssaultTests.cs`
(a whole assault, every aftermath outcome, the heavy-casualty threat, a refused launch spending the
action and keeping the nerve, restart at every stage) and `DirtsideEndpointTests.cs` over HTTP with
real dice.

**Recorded, not built**, three judgements the wiring made where the resolver gave it nothing else:

- **Which stand dies is the game's choice**, in the order the stands were committed. The resolver
  reports losses as a count against a side, and letting the owner pick would need a second
  round-trip. The log names the stands removed so the table can take the right models off; an
  optional list on the aftermath request is the cheap way to hand the choice back.
- **A defender that gives way at the stand step is not marked Under Fire.** `AssaultDefence`
  carries no such flag - only `AssaultAftermath` does - so the game applies exactly what the
  resolver reports. The confidence drop is applied. If the rules mark a defender that withdraws
  before the exchange, the flag belongs on the resolver first.
- **Cybertanks neither launch nor receive an assault.** All four tests read a confidence marker a
  cybertank does not carry, and the resolvers have no cybertank path, so the game refuses with a
  reason rather than inventing one.

The screen's controls are work package B and are not in this tree.

## Gap 2 - Systems-down recovery is built and a systems-down element is silenced for the game - FIXED 2026-08-29

**Severity: high.** `Combat/SystemsDownRecovery.cs` is a pure function of two activation numbers,
the design and one die: refused on the activation the marker was placed (`CanAttempt`, line 66),
retryable forever, an easier number with backup systems (`Attempt`, line 77). Tests in
`SystemsDownRecoveryTests.cs`.

**Was**: nothing called it. `IsSystemsDown` was set by a shot and never cleared, so `Prepare`
refused that element's fire for the rest of the game (`Game/DirtsideGame.Fire.cs:115`). A
systems-down chit was a permanent kill of the vehicle's combat power, which is not the rule. The
status recorded *that* the marker was on, not *which activation* put it there, and the element had
no backup-systems flag.

**Fixed.** `RecoverSystems` (`Game/DirtsideGame.Turn.cs:188`) is an activation step that spends the
element's combat action (`DirtsideAction.RecoverSystems`, `Sequence/DirtsideAction.cs:44`, so the
existing policy gates apply unchanged). A shot that puts a marker on - the firer's or the target's -
stamps the activation it happened on (`DirtsideGame.Fire.cs:249`,
`ElementStatus.SystemsDownOnActivation` at `Game/PlatoonStatus.cs:31`); an existing marker keeps its
number. The activation number is the open frame's id (`CurrentActivationNumber`,
`DirtsideGame.Turn.cs:350`), minted from the session's never-resetting counter, so "strictly later"
holds across a turn boundary. A success clears both fields; a failure leaves them and the crew may
try again next activation. `HasBackupSystems` on the element (`Game/PlatoonDefinition.cs:61`) lowers
the number. `WhyRecoverSystemsIsRefused` (`:227`) feeds the snapshot's per-element availability.
Tests: `DirtsideGameRecoveryTests.cs` (refused on the activation the damage happened, allowed after,
failure keeps the marker, backup systems, nothing to recover, the number survives a save).

## Gap 3 - The platoon carries no quality die and no leadership value - FIXED 2026-08-29

**Severity: high, and it blocked gaps 1, 4, 5 and 6.** `PlatoonDefinition` recorded a side, a kind
and a cybertank flag. Every test the morale layer offers - `ConfidenceLadder.Test` and `React` in
`src/ForceSignal.Modules.GroundCombat/Morale/ConfidenceLadder.cs:105` and `:136`, and the
`AssaultantProfile` the assault resolver reads (`CloseAssault.cs:88-92`) - wants a quality die and a
leadership number, and the roster had nothing to hand them. The same shape as StarGrunt gap 11.

**Fixed**, because the assault wiring could not be right without it. `PlatoonDefinition` now
carries `Quality` and `LeadershipValue` (`Game/PlatoonDefinition.cs:129-130`), both optional and
both entered off the player's own card through `AddDirtsidePlatoonRequest`, carried in the snapshot
and the saved game. A platoon whose card does not say what it rolls can do everything it could
before; it cannot launch or receive an assault, and the refusal names the platoon rather than
defaulting a number - the content policy's line, held.

**Followed up 2026-09-12.** "Both entered off the player's own card" was half true: the number was
entered but nothing checked it was a number that could be on a card. A probe put 99 and -4 on a
command marker through `AddDirtsidePlatoonRequest` and the service stored both verbatim, to be added
to a threat level and rolled against. StarGrunt's service checked - against a bound written into its
own source, which is the opposite failure. Which numbers count as Leadership Values is now an entry
on each game's rules profile (`LowestLeadershipValue`, `HighestLeadershipValue`, both ends or
neither), read through one guard both engines share, and a value the game's profile does not hold is
refused naming the entry. A marker that says nothing is still a marker that says nothing: null is
never looked up, and the platoon's nerve is refused by name at the roll as before.

The hole is closed; what remains is callers. Only the assault reads the two numbers today, and the
confidence tests, reaction tests and infantry fire-effectiveness check that will read them next are
gaps 4, 5, 6 and 10, not this one. The screen's field for them is package B.

## Gap 4 - Confidence never changes - OPEN

**Severity: high.** `PlatoonStatus.Confidence` (`Game/PlatoonStatus.cs:49`) defaults to the top of
the ladder and nothing in the game writes it. The effects table is implemented, both columns and
their crossover (`Morale/DirtsideConfidence.cs:92-181`, with the counterintuitive reading recorded
in `ground-combat-plan.md`), and the activation policy reads it faithfully - a unit that has stopped
firing is refused a shot, one that will not advance is refused the move
(`DirtsideActivationPolicy.cs:368-418`). But outside a close assault - whose aftermath is now the
one thing that moves a marker (gap 1) - no unit ever leaves Confident, so those gates have all but
never closed. The platoon card shows a confidence level that is decoration, as StarGrunt's
was before gap 2 there.

There is no confidence-test command, service method or route. StarGrunt has all three
(`src/ForceSignal.Api/Endpoints/GroundCombatEndpoints.cs:137`).

**What should change**: the same shape as StarGrunt's fix. A test is not an action and is not tied
to an activation; it is taken the moment the trigger happens, to whichever unit it happened to, at a
threat level the player supplies. What the app owns is the procedure and the effects table; when a
test is owed stays the player's call. A cybertank is refused a test rather than passing one, which
is what its flag already means (`Sequence/DirtsideBoard.cs:20-30`).

## Gap 5 - Under Fire is never placed and never cleared - OPEN

**Severity: high.** `Morale/UnderFire.cs` says when a marker goes on (`MarksTarget`, line 35 - foot
is marked by being shot at, a vehicle only when something lands), what it costs (`OwesReactionTestToMove`,
line 46) and when it comes off (`After`, line 59 - the end of the unit's *own* activation, never the
turn). None of the three is called. `DirtsideGame.Fire.cs:187-250` writes destroyed, damaged,
immobilised and systems-down onto the target and never touches `PlatoonStatus.IsUnderFire`
(`Game/PlatoonStatus.cs:52`); `EndActivation` (`Game/DirtsideGame.Turn.cs:276`) clears nothing. The
one thing that now sets the marker is the side that falls back from an assault (gap 1), and nothing
takes it off again.

Two consequences. The policy's test-before-you-move gate
(`DirtsideActivationPolicy.cs:397-400`) never closes, and the die step the marker costs an infantry
firefight (`Combat/InfantryCombat.cs:194`) is never applied. This is StarGrunt gap 1 inverted: there
nothing could be unpinned, here nothing is ever pinned.

**What should change**: `Fire` applies `MarksTarget` to the target platoon after the shot, with the
kind off the roster and "damaged an element" off the result; `EndActivation` applies `After` to the
unit whose frame just closed. Both are one line each once gap 4 gives the test somewhere to go.

## Gap 6 - The reaction-test gate stands open, and the declarations it reads have no route - OPEN

**Severity: medium; depends on gap 4.** `PlatoonStatus` carries `NextMoveAdvancesOnTheEnemy`
and `ReactionTestCleared` (`Game/PlatoonStatus.cs:64` and `:67`), the policy refuses a move on them
(`DirtsideActivationPolicy.cs:391-418`), and `EndTurn` clears the cleared flag
(`Game/DirtsideGame.Turn.cs:331`). Nothing ever sets either: there is no route to declare that a
move advances on the enemy, no reaction-test command, and `ConfidenceLadder.React` is called only
for an assault's launch and follow-through. The same is true of `IsDisorganised` (`PlatoonStatus.cs:55`), which
the integrity gate reads (`DirtsideActivationPolicy.cs:357`) and no route sets.

StarGrunt went through exactly this (its gap 7): the seam was built and documented, the flags were
never set, the gate stood permanently open.

**What should change**: three declarations and one roll, all of which StarGrunt already has routes
for (`GroundCombatEndpoints.cs:172`, `:184`, `:196`). Whether a move advances or leaves cover is an
eyeball judgement, so it is declared; the test is then offered at a supplied threat level; failing
costs the move, never a level, and leaves the element to do something else - which the
one-move-one-action shape already allows without a `RefusedOrder` step. Under Fire and a shaken
ladder both want the same test and the rule that threats never stack means one roll at the higher
level clears both (the policy's remark at `:393-396` says so).

## Gap 7 - Opportunity fire never opens a window - OPEN

**Severity: high.** The policy's half is complete: the window request with eligibility frozen at
the trigger and no cap (`DirtsideActivationPolicy.cs:207-239`), the cost that spends the firer's
whole activation without forfeiting its side's next go (`:75`), and `DirtsideTurn.ReactWithOpportunityFire`
(`Sequence/DirtsideTurn.cs:23`). The shared layer can declare and decline a reaction
(`GroundCombatSequence.cs:355` and `:390`).

The board's half returns null: `DirtsideGame.OpportunityFireOpening` (`Game/DirtsideGame.cs:175`)
says so in its own remarks - "until the app can ask, nothing is watching and no window opens." So a
move is never interruptible, and a unit that held its activation to catch a mover gets nothing for
it. The readiness warning names it.

**What should change**: the move request carries who can see the mover - tape and eyeball, the
player's judgement, like every other measurement here - and the game turns that into an opening.
A reaction route answers the window with a fire command from the responding unit (through the same
`Fire` path, with `MovedOverHalf` on the mover already declared by the move), and a decline route
closes it. The snapshot needs to show an open window, which it cannot today.

## Gap 8 - Sensors can be switched on, and live sensors intercept nothing - OPEN, and narrowed to four sentences

**Severity: high, and actively misleading.** `SetAreaDefenceSensors` is reachable and spends the
element's one combat action (`Game/DirtsideGame.Turn.cs:159`, route at
`GroundCombatEndpoints.cs:386`, button at `DirtsideView.tsx:287-295`), `IsInterceptable` is on the
weapon card (`Game/PlatoonDefinition.cs:30`), and the interception window costs nothing so an
already-activated unit may answer it (`DirtsideActivationPolicy.cs:90`, `:241-267`). But
`InterceptionOpening` returns null (`Game/DirtsideGame.cs:183`) and, unlike opportunity fire, the
resolution itself is not built - there is no resolver in `Combat/` for the roll that shoots an
incoming round down.

So today "Sensors On" costs a combat action and buys nothing. A screen that offers it is offering a
trade with no other side.

**What should change**: an interception resolver (the sensor's die against the incoming weapon,
with the player's numbers), an opening built from the watchers the fire request names, and a
reaction route. Until then the screen should say what the button does not yet do.

**Revisited 2026-09-11, and deliberately not closed.** `IsInterceptable` now reaches the roster from
a checkbox on the add-platoon form - it had been a write-only field with no client able to fill it
in, so it arrived `false` for every weapon in every game - and that is the whole of what could be
finished here without inventing rules. **The resolution does not exist and cannot be guessed:**
nothing says when a defender may declare, who may answer and at what reach, what is rolled and
against what, what a success does to the shot, or whether one element may do it more than once a
turn. The reach in particular is a number and belongs on the rules profile beside the die tables,
not in the code. Four sentences from the owner would close all of it; a plausible-looking resolver
would be the `Class-2 Beam` defect again.

**Revisited again 2026-09-11, and wired to the edge of its rules.** Everything above that is not a
rule is now built and reachable, and one sentence of it was wrong:

- **"So today 'Sensors On' costs a combat action and buys nothing"** is no longer true, and the
  worse version of it was never quite right either. The game-level command did not push a frame and
  roll nothing - `InterceptionOpening` returns null, so no window ever opened and
  `CanDeclareReaction` refused first, with **"No window is waiting for an answer."** That sentence
  reads as *wait, and one will come*, and none ever can. The defect was a misleading refusal, not a
  silent one.
- **The reach is on the rules profile**, as this document said it should be: `areaDefenceReach`,
  entered on the create screen, validated, round-tripped, and read by eligibility. Nothing compares
  it with a distance, because what it is measured *against* is one of the four missing sentences.
- **`POST /api/dirtside/games/{id}/interceptions`** answers, always with a 400 naming the most
  specific true reason - the element's gates, then the reach, then the four rules.
- **`canIntercept` / `whyItCannotIntercept`** are on every element snapshot, and an Intercept button
  sits beside Sensors once they are live.

**Searched exhaustively before any of it was written** - all 14 branches, the whole history with
pickaxes and deleted-file scans, every doc, comment, UI string and test name. No interception
resolver has ever existed in this repository, and the procedure is described nowhere. The "sensor's
die against the incoming weapon" line above is a proposal in this document, not a rule, and is
retracted three paragraphs later.

What the repo *does* settle, and so what is no longer an open question: **who may answer** (alive,
systems up, sensors paid for, already-activated is fine) and **what it costs** (nothing; the combat
action was the price). Still open, and only a rulebook can close them: when a defender may declare,
what is rolled and against what, what a success does to the shot, and whether one element may do it
more than once a turn.

## Gap 9 - Indirect fire is not built - OPEN

**Severity: known and previously recorded.** `DirtsideAction.ObserveForIndirectFire` exists
(`Sequence/DirtsideAction.cs:29`) and `DirtsideSteps.Act` accepts it, but no game command wraps
it, no route offers it, and there is no artillery at all: no fire mission queue, no observer link,
no deviation, no resolution. The plan's `Support/` layer is `[TODO]` (`ground-combat-plan.md`), and
the readiness warning names it. Recorded here so the list is complete; it is a subsystem, not a
wiring job.

## Gap 10 - Infantry fire as vehicles - OPEN

**Severity: high for any game with foot troops.** A platoon whose kind is `DismountedInfantry`
reads the right column of the confidence table, and that is where the difference ends. Its elements
are `ElementDefinition`s with fire control, signature and armour, and they shoot through
`DirectFire.Resolve` - the two-stage vehicle path, with a to-hit roll and a total against armour.

The infantry rules are built and tested and never called: the fire-effectiveness check that decides
how much of the unit is shooting (`Combat/InfantryCombat.cs:183` and `:208`), the per-stand draws
that are never pooled (`ResolveFirefight`, line 290), the reach-or-exceed kill threshold with no
damaged state (`IsDestroyed`, line 367), specials forced off against stands and on against
soft-skins (`Against`, line 331), the anti-vehicle rocket (`AntiVehicleRocket`, line 351) and what
happens to stands riding a transport that is hit (`ResolveRiders`, line 384). Tests in
`InfantryCombatTests.cs`. `InfantryStand` (line 67) is not on the roster.

**What should change**: an infantry element is a stand, not a vehicle - firefight chits, assault
chits and a kill threshold off the card - and a platoon of them fires through a firefight command
that takes the effectiveness check first. Which stands make up a partial unit's firing half is the
player's choice (the resolver's remark at lines 203-205 says so). Mounting and dismounting, and riders
taking casualties when the transport is hit, come with it.

## Gap 11 - Defensive posture cannot be declared - FIXED 2026-09-11

**Severity: medium.** `HitResolution.PostureDie` gave a target behind cover, evading, hull down or
turret down a second die, and the target keeps the better of that and its signature rather than
adding. `Prepare` read it from `ElementStatus.Posture` - and nothing set it. There was no field on
`DirtsideFireRequest`, no route, no control. **Every target in every game was in the open.**

**What changed**: declared per shot, on `FireCommand` and `DirtsideFireRequest` beside the measured
band, with a control beside the band's. That was this note's own recommendation and it holds up:
whether the target is hull down from *this* firer is a table judgement of exactly the kind the range
already is. `ElementStatus.Posture` is **removed** - with the declaration on the command it had no
reader either, and the stored shape would have had to answer when a posture clears, which nothing
on the table knows.

Two things came with it. The die each posture is worth is a row on the game's rules profile, so a
posture nobody entered a die for refuses the shot by name rather than guessing - and the refusal
lands before the step is taken, so it costs the element nothing. And `TurretDown` has a row of its
own instead of falling through a `_` arm, which is the first time that posture has been distinct
from hull down anywhere but the log.

## Gap 12 - Immobilised is said in the log and written nowhere - FIXED 2026-08-29

**Severity: medium.** `DamageOutcome.Immobilised` was worked out (`Combat/DamageResolution.cs:188`)
and the log described it (`Game/DirtsideGame.Fire.cs:276`), but `ElementStatus` had no flag for it
and `Apply` wrote only destroyed, damaged and systems-down. An immobilised element moved normally
on its next activation.

**Fixed.** `ElementStatus.IsImmobilised` (`Game/PlatoonStatus.cs:32`) is set when a Mobility chit
counts (`DirtsideGame.Fire.cs:230`) and never comes off. `MoveElement` refuses it by name
(`Game/DirtsideGame.Turn.cs:113`, through `WhyMoveIsRefused` at `:108` so the screen gets the same
words); it may still fire, which is the point of the chit. The element DTO carries `isImmobilised`.
Test: `DirtsideGameTests.cs:231`.

## Gap 13 - Firing first and then moving over half escapes the penalty - FIXED 2026-08-29

**Severity: medium, and a real gap rather than a reading.** The resolver's own contract is "has
moved, *or will move*, more than half its movement" (`Combat/DirectFire.cs:10`,
`HitResolution.cs:120-122`). The game set `MovedOverHalf` only when the move was taken and read it
at the shot, so move-then-fire was penalised and fire-then-move was not, and the element was free to
order them that way. The screen's checkbox rides on the move.

**Fixed.** `FireCommand` and `DirtsideFireRequest` gain `WillMoveOverHalf`
(`Game/DirtsideGame.Fire.cs:36`, `src/ForceSignal.Contracts/Ground/DirtsideContracts.cs:165`). A
shot that declares it is penalised as the resolver asks (`Fire.cs:129`) and sets `MovedOverHalf` on
the element at the shot (`:202-204`), so the log says it fired on the move. A later move over half
by an element that fired without declaring is refused (`Game/DirtsideGame.Turn.cs:118`, `HasFired`
at `:130`): the declaration is the price of the shot, and it cannot be paid afterwards. Tests:
`DirtsideGameTests.cs:201` and `:217`. The screen's control for the declaration is package B.

## Gap 14 - Declarations are resolved one at a time, not all made before any dice - RECORDED

`DirectFire` has a volley form that takes every declaration first and resolves them in order,
refusing rather than re-pointing a shot at a target already killed (`Combat/DirectFire.cs:123-134`,
`:230-285`). The game uses the single-shot form (`Game/DirtsideGame.Fire.cs:61`) and enforces the
binding rule only at the point a later shot names something dead (`:115`, worded at `:183`). So a
player sees the
first shot land before choosing the second target, which the rules do not allow.

**Judgement**: recorded, not fixed. The game is a hot-seat screen and a declare-everything-then-roll
flow is a real change to its shape - a declaration list on the activation, resolved on one command.
Worth doing once opportunity fire (gap 7) exists, because a window opening mid-volley is where the
order of declaration and resolution actually matters. Until then the refusal at `:115` keeps the
cost of a wasted shot real for anyone who declares honestly.

## Gap 15 - The chit pot is not configurable over the wire - OPEN

**Severity: medium.** `ChitPotComposition` says of itself that it is configuration rather than a
rule, and that its default special counts are guesswork a player who has counted their own sheet
should replace (`Chits/ChitPotComposition.cs:5-20` and `:39-54`). The plan's content policy says the
same (`ground-combat-plan.md`). The service builds `new ChitPot()` with the default
(`src/ForceSignal.Application/Ground/DirtsideGameService.cs:102`); no request, route or control
lets a game supply its own, and the composition is not in the snapshot or the saved game.

Every damage probability in the game rides on that default.

**What should change**: a composition per game - numerical counts per colour and per value, special
counts - set when the game is created or before the first turn, carried in the saved game so a
restore draws from the same pot, and shown on the screen so the table can check it against the
counters in the box.

## Gap 16 - The screen sends one validity row for all three bands, and one element for all - OPEN

**Severity: medium; a client gap, not an engine one.** The API takes a full card - a row per band
with colours, value scale, whether specials count and whether the weapon is ineffective
(`DirtsideValidityDto`), plus barrels, fixed mount and interceptable. The screen builds one row
from a colour picker and sends it for close, medium and long alike (`DirtsideView.tsx:460`,
`:479-481`), offers no value scale, specials or ineffective, no interceptable flag, one weapon per
element, and stamps every element in the platoon from the same form (`:467-483`).

Two mechanics the engine gets right therefore never show at the table. A damaged firer's shot
resolves a band worse and reads validity at *that* band (`Combat/EffectiveRangeBand.cs`) - but with
three identical rows the shift changes only the to-hit die. And the weapon family that scales chit
values rather than gating colours (`Chits/ChitValidity.cs:3-9`) cannot be entered at all.

**What should change**: a card editor with three rows, the scale and the two flags, and elements
that differ within a platoon. The API needs nothing.

## Gap 17 - Damaged halves movement, and the app only shows the marker - RECORDED

`DamagedEffects.Movement` (`Combat/DamageResolution.cs:271`) halves the figure with a named rounding
choice, and `ElementDefinition.Movement` (`Game/PlatoonDefinition.cs:59`) is carried for it. Nothing
reads either, because distance is the player's tape. The band shift half of being damaged *is*
applied (`EffectiveRangeBand`), and the screen marks the element damaged.

**Judgement**: kept as is, with one improvement worth making: show the halved figure on the card so
the table does not do the arithmetic. Not a rules divergence.

## Gap 18 - One armour value per element, not per facing - RECORDED

`ElementDefinition.ArmourValue` is "the armour on the face most likely to be hit"
(`Game/PlatoonDefinition.cs:39`), and the shot compares against it. The rules give armour by facing
and the face hit is a table judgement. **Judgement**: recorded. The right fix is the plan's
`GroundCombat/Design` layer, which stores the front and derives the rest, with the shot declaring
the facing the way it declares the band. Until then a player enters the face they expect to be
shot on.

---

## Not gaps - checked and correct

- **Stage one.** The die comes from the sight, the band and shooting on the move, with no modifiers
  added to a roll; running off the bottom of the ladder is no shot rather than a worse one; the
  target keeps the better of its signature and posture dice rather than adding.
- **The multiple mount.** The target rolls once and every barrel rolls against that score
  (`DirectFire.RollMount`); every barrel that hit is resolved, including on a target the first
  barrel killed, because a mount fires together.
- **The pot.** Without replacement within one resolution, whole again before the next, structurally
  (`ChitPot.Draw` shuffles a fresh copy every call). Invalid chits consume their slot and are never
  redrawn. Ineffective short-circuits before a chit is drawn and is not the same as "no valid
  colours". A zero is a valid chit.
- **Stage two.** Short of armour nothing, level with it damaged, past it gone. Specials fire
  regardless of the total except once the numbers have already knocked the target out. The firer's
  own systems-down rewrites the shot as never fired, whatever else the draw held, and the game
  applies it to the firer before it looks at the target.
- **Damage is a flag, not a counter.** A second damaging hit does not halve anything twice, and the
  band shift is computed once and read by both stages.
- **The activation shape.** One move and one combat action per element, in either order, or
  neither; standing down is a real step that closes the activation honestly and is refused after
  the element has acted; a fixed mount fires before moving, never after; the activation will not
  close until every surviving element has chosen, and the refusal names them.
- **The interrupt costs.** Opportunity fire spends the responder's whole activation and does not
  forfeit its side's next go; interception costs nothing and is open to a unit that has already
  gone. A confidence test is not a window (settled in the policy's remarks and in
  `ground-combat-plan.md`).
- **The confidence columns cross over** - shaken armour balks first, broken infantry stops
  advancing while broken armour goes home. Verified against the source twice and recorded in the
  plan. Do not "fix" it.
- **Cybertanks** carry no marker and every nerve gate is switched off in one place.
- **The pass rule, the fewer-units-chooses rule and the deadlock at the end of a finished turn**
  are the shared layer's and are covered by its tests.
- **Rounding choices are named** where the rules are silent: halved chit values and halved movement
  both round toward zero, each with its reasoning beside the constant.

## Deliberately not built

Recorded so a later scan does not re-find them as if they were oversights.

- **Air defence** as a window, and aircraft, VTOLs and grav vehicles as things with their own
  movement and attack rules. The plan lists the window; nothing depends on it yet.
- **The vehicle design system** - size class, capacity, armour by facing, points - which is the
  shared `Design/` layer the plan has as `[TODO]`, and which gap 18 waits on.
- **Electronic warfare and detection**: ECM, sensor ratings for spotting, hidden units and dummy
  markers. Line of sight is tape and eyeball here, as it is at the table.
- **The force-wide penalty for losing the command element**, which the plan's comparison table
  records as Dirtside's flat command model. There is no command element on the roster.
- **Engineering, minefields and terrain effects on movement.** Distance and cover are the player's.
- **Transport as a tracked state** - mounting, dismounting, riders - beyond the casualty resolver
  gap 10 names.
- **Campaign rules and the link to Full Thrust** (ortillery, landings).

## Suggested order of work

1. ~~**Gaps 1 and 2** - close assault and systems recovery.~~ **Done** 2026-08-29, as work package
   A of the forge pass, with three recorded judgements under gap 1.
2. ~~**Gap 3** - a quality die and leadership value on the platoon.~~ **Done**; the callers are the
   items below.
3. **Gaps 4, 5 and 6** - confidence tests, Under Fire, reaction tests and the declarations. The
   engines exist and the game has to call them; the same day's work StarGrunt's first three gaps
   were, and the difference between a screen that tracks a game and one that plays it.
4. ~~**Gaps 11, 12 and 13** - posture, immobilised and the fire-then-move penalty.~~ **Done**;
   12 and 13 on 2026-08-29, and 11 on 2026-09-11 as a field on the shot and a row on the profile,
   exactly the size this list predicted.
5. **Gap 10** - infantry as stands. The largest piece after the interrupts, and the one most tables
   will hit first.
6. **Gap 7** - opportunity fire. The window machinery is built and tested in the shared layer; what
   is needed is the "who can see" input, one resolver, and the reaction route.
   ~~**Gap 8** - interception.~~ **Wired 2026-09-11 as far as its rules go**: the reach, the
   eligibility, the route and the refusal. It is not waiting on work any more - it is waiting on
   four sentences from a rulebook, and nobody should start a resolver before they arrive.
7. **Gaps 15 and 16** - the pot and the card editor.
8. **Gap 9** - indirect fire, as its own piece of work.

Gaps 14, 17 and 18 are recorded judgements and need nothing until the shape around them changes.

And the thing worth doing before any of it: play a turn with two platoons, an assault and a
systems-down chit in the pot. Everything above is verified by reading the code and by the module,
service and endpoint tests, and the screen has been clicked through, but no Dirtside game has been
played end to end.
