# ForceSignal Roadmap

> **Audit 2026-09-08.** A five-pass re-scan (notes in `vault/`, gitignored;
> cross-project view in `../../ROADMAP.md`) checked this file's boxes against
> the code rather than trusting them. **They hold up** — including
> "Spectator/public table display", which is implemented as `publicMode`
> (`ForceSignal.Web/src/main.tsx:73,376,1910`, `style.css:1714`) and removes
> hidden information at the data layer, not just visually. An automated pass
> that grepped for "spectator" reported it missing; that was wrong, and the
> box stays checked.
>
> Baseline on that date: `dotnet build` clean with warnings-as-errors,
> `dotnet test` 1289 passing across 8 projects. Web tests were *not* run,
> because `scripts/verify.ps1` never invokes them even though
> `package.json:10` defines `test`.
>
> The audit's headline is not on this list at all: `docs/rules-fidelity-gaps.md`
> records that no two-device match has been played end to end since the turn
> structure changed, `scripts/two-player-smoke.py` exists and is referenced
> from nowhere, and several defects found by reading code are exactly what
> that script would surface — most sharply, joining a second room still
> displays the first one's state (`ForceSignal.Web/src/main.tsx:1164-1171`).

## Closed from the 2026-09-08 audit

Seven findings, on `fix/audit-2026-09-08`. Every one carries a test that was
confirmed to fail before its fix and pass after. Baseline going in: 1289 .NET
tests across 8 projects, 0 warnings with warnings-as-errors; web tests were not
runnable in CI at all. Coming out: 1297 .NET tests and 133 web tests, both gated.

- [x] **The board is let go of with the match, not after it.** The version high
      water mark that `applySnapshot` drops stale snapshots against is per match,
      and every room starts again at version 1, so it has to come down with the
      match. `clearSession` did reset it, but only at the moment of leaving, and
      the screen refetches the whole snapshot on every hub notification without
      cancelling. A response still in the air when the player taps Leave put the
      old match's version back, and the next room's first snapshot was then read
      as stale and dropped - the join succeeded and the screen went on showing
      the previous room's code. Clearing now happens in `clearLocalMatchState`,
      which is the first thing create, join and restore do.
      Test: `Web/src/main.test.tsx`.
      *The audit described this as a plain create-then-join failure. It is not:
      the create/join form only renders with no session, and the only route to no
      session already cleared the board. The defect is real, but it needs the
      late-response race to reach it, which is what the test drives.*
- [x] **Readiness needs a rules profile.** A match keeps `RulesProfile.Empty`
      until its owner fills one in, and nothing downstream refuses to resolve
      against that - a beam matches no row of a table that is not there and
      scores nothing - so a table that skipped the step played a whole game of
      volleys that all came back zero. `SetReady` now refuses; standing down
      stays unconditional. Tests: `InMemoryMatchServiceRulesProfileTests`.
- [x] **A room code comes from a space worth having.** Three slots of thirty-two
      words is 32,768 codes; a server holding a few hundred live matches is one a
      script walks into a seat in within a couple of hundred tries. Now sixty-four
      words in four slots, 16,777,216 codes. The word list is append-only, because
      a code already read aloud has to keep working.
      Test: `InMemoryMatchServiceHardeningTests`.
- [x] **A mount fires only so many barrels.** The count arrives off the wire and
      was floored at one and left alone above, where it became the trip count of
      the dice loop in `DirectFire` - inside the service lock, so one request
      stopped every other game on the server. `StarGruntGame.Assault.cs:270`
      clamps the identical path with a comment saying exactly this. The ceiling
      now lives with the others in `GroundGameGuards`.
      Test: `GroundGameHardeningTests` (fails pre-fix with `OutOfMemoryException`).
- [x] **CI runs the web tests, honours the lockfile, and fails on a CVE.**
      `scripts/verify.ps1` never invoked `npm run test`, used `npm install` where
      the Dockerfile already used `npm ci`, and ran `dotnet list --vulnerable`
      through a helper that gates on an exit code the command never sets. Turning
      the web tests on immediately found two test files failing: recent Node
      versions define their own experimental `localStorage`, and vitest's jsdom
      environment leaves it in place rather than installing jsdom's working one.
- [x] **An Under Fire marker comes off again.** It went on in the assault
      aftermath and came off nowhere at all. `UnderFire.After` exists and says
      when it lapses - the end of the marked unit's own activation, never the end
      of the turn - and nothing called it, so a platoon that lost an assault owed
      a reaction test before every move it made for the rest of the game.
      Tests: `DirtsideGameAssaultTests`, including that a defender marked during
      somebody else's activation keeps its marker.
- [x] **A database file that will not open costs the persistence, not the
      server.** The store is built in a DI factory and the host resolves the match
      service at startup to report what it could not restore, so a corrupt or
      unwritable SQLite file stopped the server coming up rather than failing one
      match. The three factories now share a helper that falls back to memory and
      logs at Error with the path; the file is left alone so it can be recovered.
      Test: `ApiHardeningTests` (fails pre-fix with `SQLite Error 26: file is not
      a database` out of the DI factory).

**Nothing is still open from that audit. Corrected 2026-09-11.** This paragraph
listed ten rows as outstanding and every one of them had already shipped, most of
them in the follow-up section further down this same file. Checked against the
code rather than against the other rows: ship position editable during
Reveal/Movement (the four commitment fields are frozen per ship in
`InMemoryMatchService.cs:596-613`), locked orders silently overwritten,
`docker-smoke.ps1` asserting the wrong persistence mode, unbounded restored
dice-roll arrays (`MaxDiceRollsPerFiringResult`,
`InMemoryMatchService.Restore.cs:56`), ground game creation with no rate limit,
`SequenceGuards` unguarded on the write path, the rules-profile editor seeded
blank, "finished plotting" being final, the hardcoded 12/24/36 range-band label,
and the web handlers with no double-submit guard — which was eleven handlers
rather than the twelve the audit counted.

A list of open rows that are all closed is worse than no list: it teaches the
next reader that the open rows here are the ones still to do, when the file's
own later sections already say otherwise.

ForceSignal is currently focused on being a session-based tabletop helper for in-person games. Online play and durable persistence remain future options after real table testing.

## Near-Term Tabletop Priorities

- [x] Fighter operations: launch/recover, group strength, endurance, max operating range, carrier assignment, and map range visualization.
- [x] Ordnance and ammo: missile/salvo markers, limited-use weapons, reload counters, and launched ordnance map objects.
- [x] Carrier operations: assigned/airborne/recovering group summaries and damage-adjusted launch/recovery reminders.
- [x] Table safety tools: end-of-turn checklist, unresolved fire reminders, fighter endurance reminders, quick undo, and log/snapshot export prompts.
- [x] Measurement helpers: range bands, weapon arcs, fighter range rings, ordnance markers, and movement path preview with final position/course.
- [x] Physical table workflow: print/export ship cards, compact tablet-friendly controls, public table display mode, and pass-and-play privacy.
- [x] Damage detail: damage-control counters, critical system hit markers, and half-hull/dead-in-space
      reminders. This item previously also claimed threshold checks; it should not have. The app
      tracks system damage as counters a player edits by hand - there is no hull-row threshold roll.
      See `rules-fidelity-gaps.md` gap 1.

## Weapon Rule Fidelity

- [x] Beam falloff matches the rulebook: a beam loses one die per full 12mu band, so a Class N
      beam rolls N dice at 0-12, N-1 at 12-24, N-2 at 24-36. The earlier profile dropped a die
      every 6mu, which made light mounts useless past close range. Range band labels follow the
      same 12mu bands.
- [x] Per-die damage rolls and screens that downgrade dice. The app rolls one die per remaining
      class: 1-3 miss, 4-5 score one, 6 scores two. Screens downgrade the roll rather than
      removing dice - level 1 ignores 4s, level 2 caps every hit at one, level 3 scores only on
      a 6 - so a single-die mount can still hurt a screened ship. Every shot records the faces it
      rolled in the battle log and the firing console for table audit.

## Play Blockers — Rules Fidelity

Found by scanning the code against the GZG rules notes; each item is written up in full in
`rules-fidelity-gaps.md`. The app targets Full Thrust Light, and these are the FTL mechanics
that are missing or wrong, in the order they should be fixed to reach a real match.

- [x] Threshold checks (gap 1). The hull is four rows; completing one rolls a die per surviving
      system - drives, each firecon, each screen level, each mount - lost on 1, then 1-2, then 1-3
      by depth, one point worse per extra row torn through in a single attack. Losses bite: drives
      halve then die and limit what orders can be plotted, a knocked-out mount cannot fire, screens
      drop a level per generator. Checks are logged with their rolls, and the damage track marks the
      row boundaries. The check waits for the firing ship to finish, so one volley earns one check
      against the deepest row it reached - declared with "Done Firing", and closed automatically
      when another ship fires or the phase ends.
- [x] Six 60 degree fire arcs (gap 2). Fore, fore starboard, aft starboard, aft, aft port, fore
      port. A mount now bears through a *set* of arcs, so a battery covering three adjacent arcs or
      an all-round turret can both be transcribed. Fleets and snapshots written with the old four
      arcs still load, expanded per mount.
- [x] Aft blind spot (gap 3). No weapon may fire through the aft arc: mount normalization strips
      it, the firing rules refuse it whatever the mount, and the editor never offers it.
- [x] Verify arc bearing against geometry (gap 4). The arc a target lies in is computed from the
      firing ship's course and the two positions, and the mount is held to it. An arc supplied by
      the client is treated as a cross-check and a disagreement names the real bearing.
- [x] Fire control gating (gap 5). A ship with no working firecon cannot fire at all, and each
      working firecon holds one target ship for the turn - any mount may add to a target already
      engaged, but a fresh one beyond the limit is refused. The console shows the count and the
      reason before a shot is attempted.
- [x] Drift for unordered ships (gap 7). Plotting closes when each player says it is done, and a
      ship with no order holds its course and speed while still travelling its full velocity. Reveal
      covers only what was actually locked, and a turn nobody plotted goes straight to movement.
- [x] Firing initiative and alternation (gap 6). The firing phase opens with a logged die-off, the
      winner fires one ship completely, and play then alternates a ship at a time - out-of-turn fire
      and mid-volley ship swaps are refused, holding fire spends a ship's turn, and players with
      nothing left to fire are skipped. Damage still lands as it is rolled, so a ship can lose its
      guns before its own turn arrives.
- [x] Pulse torpedoes (gap 10). A mount declares its kind, and a torpedo launcher rolls to hit -
      2+ inside 6mu, a point worse every 6mu, out to a 6 at 30 - then takes a damage die's face as
      damage. Screens do not reduce it. The console shows the number needed before the shot.
- [x] Range cross-check (gap 14). The table still decides distance and a shot is never refused over
      it, but a declared range that falls in a different band than the map measures - or is more than
      half a band off - is flagged in the console before the shot and recorded in the log.
- [x] Half hull is labelled a watch list rather than a rule (gap 15). Full Thrust has no crippled
      state, so the app no longer implies one.
- [x] Ordnance attack resolution (gap 11). A salvo is thrown at a point of aim within the launcher's
      reach and strikes at the end of movement: one die for how many of its six missiles arrive, point
      defence shooting some down, then a die per survivor whose face is its damage. Screens do not
      reduce it and armour halves it, and the damage can fill a hull row like any other.
- [x] Fighter group strength (gap 12). A group rolls one die per surviving fighter, loses dice as its
      aircraft die, and spends a turn of endurance automatically for any turn it is in combat.
- [x] Point defence (gap 13). Each system rolls a die at fighters and missiles inside 6mu - 4 or 5
      kills one, a 6 kills two and rolls again - with its own fire control, firing through any arc.
      Kills come off a fighter group before it attacks, and off a salvo before it strikes.

## Second-Scan Rules Gaps

A second pass over the rules areas the first scan did not reach - carrier operations, anti-fighter
defences, damage control, needle beams - found six more divergences. Each is written up in full in
`rules-fidelity-gaps.md`.

- [x] Main batteries cannot engage fighter groups (gap 16). Only fighters may fire on fighters; a
      warship is not even offered a group as a target, and point defence answers a strike instead.
- [x] Fighter groups are flown, not plotted (gap 17). A group moves to any point within 12mu once a
      turn, in any direction, ending up facing the way it flew. It takes no orders, never holds up
      plotting, and never drifts.
- [x] Carrier launch and recovery follow the rules (gap 18). A carrier must hold course and speed to
      launch or recover, works two groups a turn if it is a true carrier and one otherwise, deploys a
      group onto itself, and refuses a rendezvous the group cannot reach or a deck that is full. Ships
      host groups by having bays rather than by being carrier-classed.
- [x] Fighter bays are threshold systems (gap 19). Each bay rolls its own die, and a bay knocked out
      costs capacity and takes any group still sitting in it.
- [x] Damage control (gap 20). Parties work between turns: one repairs on a 6, three on a job need 4 or
      better, and they roll once between them. Drives come back in halves. Parties die at thresholds and
      stay dead. Screens and bays are not yet repairable - see the follow-ups below.
- [x] Needle beams (gap 21). Name a system, roll one die, and a 6 takes it - no hull damage, screens
      ignored, 9mu reach, and a firecon tied up that can direct nothing else that turn.
- [x] Recorded the four-row threshold choice (gap 22), with the reasoning where the constant lives.

### Follow-ups the second round created

- [x] Screens and fighter bays record what the ship was built with plus what has been shot away, so
      damage control can restore them and the table still sees what is actually generating.
- [x] Needle-beam losses are permanent. A needled system is cut out rather than broken, tracked per
      system and held against the repairable total, and a needled mount is labelled as such.
- [x] A rules-layer switch. A match is played under one layer, settled during fleet setup and carried
      in the snapshot, and the numbers that differ follow from it: the Fleet Book layer caps screens at
      level two, flies fighter groups 24mu instead of 12, and reaches 12mu with a needle instead of 9.
      Switching down brings existing screens with it.
- [x] The layer reaches the rules. Firing and threshold resolvers take the profile as an argument
      rather than capturing one, because the resolvers are built once per service while the layer is
      per-match state that the owner can still change during fleet setup. Two more differences are
      now wired and tested against both layers: **the enhanced needle beam** puts a point into the
      hull on a 5 or a 6 and that point ignores armour, and **flight operations** run at one group
      per operational bay out, half the bays back, with a turnaround roll on recovery.
- [ ] **Threshold rows by class** is *not* a Fleet Book difference and is deliberately not wired.
      Four rows for every hull is the Fleet Book rule and is what both shipped layers use; rows by
      class band - an escort two, a cruiser three, a capital four - is the second edition rule, and
      ForceSignal has no second-edition profile to hang it on. The seam exists
      (`RulesProfile.ThresholdRows`, `ShipClassBands`, `FullThrustLightThresholdRules.RowCountFor`),
      so adding a second-edition layer is a matter of selecting it. Whether to add that layer is a
      separate decision: it also changes screens, needle reach, fighter moves and ship points.

## Movement Rule Fidelity

- [x] Free rotation at rest: a ship at velocity 0 that makes no other move rotates to any heading
      for free, ignoring the thrust cost and the half-thrust cap, so a thrust-0 station can come
      about. Getting under way still costs thrust as usual.
- [x] Split course changes across the move: a plotted turn pivots half at the start, rounded down,
      and the remainder at the mid-point, running half the velocity between the two pivots - which
      is what puts the ship where the rulebook's worked examples say it ends up. A plotted sequence
      of turns takes an equal share of the move each and splits the same way.

## Ground Combat

Both engines are behind feature flags and default to off. Each has its own numbered fidelity audit;
the roadmap lists only what a table can reach.

- [x] StarGrunt II plays end to end on one device: activation, fire, suppression, confidence, rally,
      reaction tests and close assault. See `stargrunt-fidelity-gaps.md`.
- [x] A force goes out to a file and comes back, because a platoon is a lot of dice to type. Both
      directions refuse rather than invent, by name: export turns away a snapshot that carries no
      roster, and import turns away a file that leaves out a quality, armour or impact die. A file
      is still allowed to be incomplete wherever that costs nobody a number - a unit with no weapons
      is a command element, and a weapon with no support firepower die is most weapons.
- [x] The close-assault settle-up asks which die the player's own table reads its bands against,
      rather than throwing a D6 at bands written for something else (2026-09-11).

### Dirtside II

- [x] The activation sequence: a platoon activates, each element takes one move and one combat
      action in either order or stands down, fixed mounts fire before moving, the activation closes
      only when every element has chosen.
- [x] Direct fire: two-stage hit and chit-draw damage, twin mounts against one target roll, binding
      declarations, a damaged firer's shot resolved a band worse, the firer's own systems-down
      rewriting the shot.
- [x] Close assault - launch, stand or withdraw, simultaneous rounds, aftermath and follow-through -
      reaching the table 2026-08-29 (gap 1).
- [x] Systems-down recovery as an activation step, refused on the activation the marker was placed
      and retryable after, reaching the table 2026-08-29 (gap 2).
- [x] A quality die and leadership value on the platoon, optional and off the player's card,
      reaching the table with close assault 2026-08-29 (gap 3).
- [x] Immobilised is recorded on the element and refuses a move by name; a shot declares whether the
      element will move over half, is penalised for it, and an undeclared later move is refused
      (gaps 12-13, 2026-08-29).
- [ ] Confidence tests, Under Fire markers and reaction tests, all built in the module and not yet
      called by the game outside a close assault's aftermath (gaps 4-6).
- [ ] Opportunity fire and area-defence interception. The windows and their costs are built and the
      board never opens one: `DirtsideGame.InterceptionOpening` returns null unconditionally
      (`DirtsideGame.cs:194`), so no route can reach the feature (gaps 7-8). **Half of this row was
      corrected on 2026-09-09:** "Sensors On buys nothing" is no longer true - the engine's own
      interception refuses an element whose sensors are dark. What remains open is the window, not
      the cost of it. Opening one before the rest is wired would deadlock a game rather than enrich
      it, which is why the opening is deliberately still null.
- [ ] Infantry as stands with the firefight rules rather than as vehicles (gap 10).
- [ ] Defensive posture declared on the shot (gap 11).
- [x] A configurable chit pot, entered on the create screen and carried per game (gap 15,
      2026-09-09). The composition is the players', optional for one release only, and a game
      falling back to the built-in default says so on every snapshot.
- [ ] A full validity-card editor on the screen (gap 16).
- [ ] Indirect fire (gap 9).

## Later Production Options

- [x] Browser/local snapshot recovery for tabletop use through local autosave, explicit snapshot export, and full-state restore with seat claiming.
- [x] Durable match storage. Matches are written to a SQLite file as they change, and rebuilt at
      startup, so a restart - or a crash - resumes the game instead of ending it. The internal
      state is persisted rather than the exported snapshot, which matters: a snapshot reissues ids,
      omits participant tokens and omits the commitment hashes behind locked orders, so recovering
      through it would hand every device new ship ids, make everyone claim their seat again, and
      throw away any order already locked. Persisting the real state makes a restart invisible - and
      a locked order still reveals, because the salt was never on the server to lose. SQLite rather
      than a database server: one process, one table of players, one laptop. `IMatchStore` is the
      seam if this ever becomes hosted.
- [x] Spectator/public table display with hidden information removed.
- [ ] Reconnect-safe online play once the tabletop workflows are proven. Both halves are now in
      place — the client reconnects, rejoins the notification group and resyncs, and the server
      keeps the match across a restart — so what remains is real-world testing rather than a
      missing piece.

## Closed 2026-09-09 (audit follow-up, second pass)

- [x] **The game is rolled on the die the players said they were playing on.** Every one of the eight
      Full Thrust resolvers took a `Func<int>` that produced 1-6, and applied `RulesProfile.DieFaces`
      *afterwards* as a clamp on the result. That is not a die. A table playing d10s could never roll
      above a 6, so the rows they had entered for 7 through 10 were dead and a needle set to kill on
      8+ never killed anything; a table playing d4s got the mirror image, with 4, 5 and 6 all folding
      onto the 4 so their top result came up **50%** of the time instead of 25% (measured, not
      estimated -- see `DieFacesTests`). The die source is now `Func<int, int>`: face count in, face
      out, which is the shape `QualityDiceRoller` has had all along and which was never carried to
      its siblings. The clamp stays as a guard against a misbehaving injected source, exactly as
      `QualityDiceRoller` does, but it is no longer how the die gets its size.
      **The engine does not rescale anything, deliberately.** Every threshold in a profile --
      `NeedleSystemKillRoll`, `RepairRollWithOneParty`, `TorpedoBestToHit`, `PointDefenseChainOnFace`,
      the beam damage rows, the turnaround rows -- is a number the player read off their own card in
      their own die's terms, and `RulesProfile.Validate` already refuses a row naming a face the die
      does not have. So the numbers were never wrong; only the die was. Rolling it correctly makes
      each of them mean what the player meant, where rescaling would reinterpret them.
      The service's own firing-initiative die-off was a ninth site and the worst of them -- it
      clamped to a literal `6` rather than to the profile at all -- so a d10 table settled who shoots
      first on a d6 and was never told.
- [x] **The range-band label is counted off the player's own band width.** It had 12, 24 and 36
      written into it, so a table banded every 10 read "close" on a shot that had already lost a die.
      It now uses the same arithmetic the firing rules use, and the weapon's kind picks the width,
      because a pulse torpedo bands differently from a beam. The log is what a table checks a
      disputed volley against, so a label that disagrees with the dice beside it is worse than none.
- [x] **The Dirtside chit pot is the players'.** Closed 2026-09-09; see "Chit pot: the decision, and
      what shipped" below.

## Chit pot: the decision, and what shipped

**The owner's call: optional on the create request, carried per game, `Default` kept for one release
behind a readiness warning that says out loud that its counts are a guess, then removed.** This
section used to hold three open questions; all three are answered, and this is the answer.

`ChitPotComposition`'s own doc said the composition "belongs where a player can look at it and
replace it rather than buried in a static initialiser they have to read the source to find", and then
put 50/25/25 numericals and 6/6/3/3 specials in exactly a static initialiser. The module was never
the problem -- `Of`, `FromCounts`, `WithSpecials` and `ChitPot(composition)` were all already there.
Nothing above the module exposed them.

**1. Does the default survive? For one release, and never silently.** Full Thrust's policy is that
there is no default at all, and the guessed special counts argue for the same here. But removing it
outright retires every stored Dirtside game and every game already open on a table, so it stays --
and pays for staying by being labelled everywhere it can be. `/ready` now carries a second Dirtside
warning naming the guess in as many words: that the special counts (Mobility, Systems Down, Boom) are
"a guess, not a published distribution", that every damage probability in such a game rests on numbers
nobody counted, that `chitPot` on create is how to play on your own sheet, and that the fallback is
kept for one release and then removed. Every snapshot carries `chitPot.isBuiltInDefaultGuess`, and the
screen renders it in red beside the turn number rather than only at the start -- a table arguing about
a draw should not have to ask whoever pressed Start Game what is in the bag.

**2. Per game or per server? Per game.** `IChitPot` stays; what was injected became
`Func<ChitPotComposition, IChitPot>`. One pot is built per game from that game's counts when it is
created or restored, and held on the service's `Held` record beside the token -- not per command,
because a pot holds the shuffle's randomness and rebuilding it every draw would hand each resolution
a fresh source. `Fire` and `FightAssaultRound` take the pot off the game they are playing, through a
`CommandWithPot` sibling of `Command`. The old shape was a process-wide singleton: every table on the
server drew from one bag, and none of them could say what was in it. `ScriptedChitPot.Handing(...)`
is the test-side factory; the module's own doubles are untouched.

**3. What happens to saved games? Nothing -- they open.** The composition is stored in a **new
optional `settings` field on `GroundGameRecord`**, not by bumping `FormatVersion`. That was the item
worth getting right: a mismatched format is *skipped, not migrated*, so a required field would have
retired every stored ground game on the machine, and the operator would have learned about it from a
table asking where their game went. Optional costs nothing -- a row written before the field simply
reads back without it and falls back exactly as a create request carrying no pot does, flagged as the
guess. The field is opaque to the wrapper, which is shared with StarGrunt and has no business knowing
what is in it. `DirtsideChitPotTests.AGameStoredBeforeThePotWasCarriedStillOpens` builds a
pre-change row by hand and asserts it loads with `SkippedSaves` empty; anything genuinely unreadable
still goes through `SkippedSave`, which the host logs at startup.

**Content policy.** The engine owns procedures; the player owns every number those procedures read.
The wire vocabulary (`DirtsideWire.ChitColours`, `ChitSpecials`) is which chits exist, which is the
engine's; how many of each are in the bag is the player's, off their own counter sheet. The create
screen's pot editor starts empty and adds rows by hand rather than laying out a grid of the values a
sheet "has" -- how far the numbers run is the sheet's business. `toChitPotInput` sends nothing at all
when nothing was counted, rather than a plausible bag. The one number this app still invents is the
fallback, and it is now impossible to play on it without being told.

Still open: **the on-screen validity-card editor** (gap 16). The pot half of gap 15 shipped.

## Closed 2026-09-11 (posture, the waiting list, and where interception stops)

*Branch `fix/posture-and-waiting-list`, taken after the die tables landed.*

- [x] **A defensive posture can be declared, and the die-shift mechanic runs.**
      `HitResolution.PostureDie` had been wired into `DirtsideGame.Fire` since
      Dirtside had a screen, and nothing in production ever moved a target off
      `None` — there was no field on any contract to carry one — so **every
      target in every game was in the open** and the whole mechanic was inert.
      It is declared **per shot**, on `FireCommand` beside the measured band,
      which is the shape `docs/dirtside-fidelity-gaps.md` had already argued
      for: whether a vehicle is hull down is a statement about *this* firer's
      line of sight rather than a property of the vehicle, and two people settle
      it by looking across a table, exactly as they settle the range.
      `ElementStatus.Posture` is **gone** — with the declaration on the command
      it had no reader either, and a stored field would have had to answer when
      a posture clears, which nothing on the table knows. Wired to the
      player-supplied ladder rather than to literals, so a posture whose row
      nobody entered refuses the shot by name and costs the element nothing.
      `TurretDown` has a row of its own now instead of falling through a `_`.
- [x] **`ElementsStillToChoose` is rendered.** Computed on every snapshot since
      the screen existed and read by nothing in either language, so a table
      learned who was holding the activation up by asking each other or by
      hovering a disabled button. It names them — the wire carries ids and
      nobody at a table calls a vehicle `alpha-2` — and falls back to the id on
      version skew, because an unfamiliar name beats a blank line. `hasChosen`
      renders beside the two flags it is built from rather than instead of them.
- [x] **`IsInterceptable` stops being write-only**, as far as it honestly can. A
      checkbox on the add-platoon form, through `DirtsideWeaponInput`, to the
      roster. The field had existed on the server since Dirtside had an API with
      no client able to send it, so it arrived `false` for every weapon in every
      game.

### Interception: finished up to the rules, and stopped there

**The resolution rules do not exist in this engine.** Checked directly rather
than inferred: `src/ForceSignal.Modules.Dirtside/Combat/` has no interception
resolver of any kind, and `DirtsideGame.Turn.cs:196` `InterceptWithAreaDefence`
pushes a reaction frame onto the sequence and rolls **nothing** — no dice, no
effect on the incoming shot, no outcome. What exists is sequencing and gating:
the window constant, the reaction frame, and a sensor check that refuses an
element whose area-defence sensors were never switched on.

So finishing it means deciding what interception *does*, which is inventing
rules — the same class of defect as the invented `Class-2 Beam`. **Stopped, with
exactly what is missing:**

1. **When the window opens.** `DirtsideGame.InterceptionOpening` returns null on
   purpose and says why: an open window refuses every step until answered, and
   with no route to answer *or decline* it would deadlock the game. So the
   routes come first — and a decline route alone would produce windows that can
   only be declined, which is the shape already rejected here.
2. **Who may answer.** Presumably an element with live sensors within some
   reach of the incoming round's path. **That reach is a number nobody has
   entered**, and there is no field for it on any contract.
3. **What is rolled.** Nothing in the engine says. A quality die against a
   target number? An opposed roll against the firer? A chit draw? All three are
   procedures this app would be making up.
4. **What a success does.** The shot is stopped entirely, or degraded, or the
   chit count reduced. Nothing says.
5. **What it costs.** The sensor check comments say interception itself costs
   nothing because the capability was bought earlier — that part *is* decided.
   Whether a second interception in the same turn is free is not.

**Four sentences from the owner would close it:** when a defender may declare,
what the interceptor rolls and against what, what a success does to the shot,
and whether one element may do it more than once a turn. The reach in (2) is a
number and belongs on the rules profile beside the die tables, not in the code.

## Closed 2026-09-11 (the Dirtside die tables, and the icon attribution)

*Two owner decisions, answered and acted on. Branch `fix/dirtside-die-tables`.*

### The Dirtside die tables are the players'

The decision was **carry them on the rules profile** — the only one of the three
options that changes what the repository ships rather than how it describes
itself. Documenting an exemption would have disclosed the exposure; silence
removed neither.

`HitResolution` held three tables the module's whole combat model rested on: a
fire-control level was worth a D6, a D8 or a D10; a posture a D6 through a D12;
and a signature indexed the quality ladder, so signature 1 was a D12. They are
now `DirtsideRulesProfile`, entered per game, carried in the stored row's
optional settings field beside the chit pot, reported on every snapshot, and
typed into a Die tables fieldset on the create screen.

- [x] **A missing row refuses and never substitutes.** `Solve` names the row —
      *"the die a Superior fire control rolls"* — and `ShotSolution` carries
      `IsMissingFromProfile`, so a gap in what was typed is told apart from a
      ladder that ran out. **The refusal lands before the step is taken**, in
      `Fire` and in `RecoverSystems` both, so a shot the game cannot settle is
      not charged to the element: every other refusal there is something the
      player could have known, and this one is a gap in what they typed.
- [x] **Only the rows a shot reads are required.** The over-strict trap this
      project has paid for twice. A table whose vehicles are all one gunnery
      grade and never go to ground enters three lines, not twelve, and
      `OnlyTheRowsThisShotReadsHaveToBeThere` and
      `APartlyFilledProfileSettlesTheShotsItHasRowsFor` are the controls that
      must be accepted.
- [x] **Two more dice went with them**, because the widened guard can see them
      and an exemption for either would have been a fiction: `SystemsDownRecovery`'s
      D6 and its 6/3 targets, and `InfantryCombat.ResolveRiders`' D6 and its 6/3
      thresholds. The second has no production caller, so its numbers are
      parameters rather than profile fields.
- [x] **Storage is optional inside the already-optional settings blob**, and read
      in its own `try` so a profile this version cannot parse costs the profile
      and not the pot. A mismatched `FormatVersion` is skipped rather than
      migrated, so a required field would have retired every stored Dirtside
      game on the machine. `AGameStoredBeforeProfilesExistedStillOpens` pins it.
- [x] **The guard follows, over both engines.** `EngineDiceContentPolicyTests`
      moved to `ForceSignal.Application.Tests` and now scans StarGrunt *and*
      Dirtside. It also learned to see a die table written as arithmetic —
      `QualityDice.Ladder[5 - signature]` was a complete table invisible to a
      scan looking only for `QualityDie.Dn`, and a guard that can be walked
      around by subtraction is not a guard.

**What widening the scan uncovered, and what was done about it.** StarGrunt's
`RangeBands.cs` reads the target's range die as `QualityDice.Ladder[bandsOut - 1
+ posture.Shifts]`. That the walk is one rung per band, and that it starts at the
bottom, are readings off a rulebook exactly as Dirtside's three tables were — and
the ladder's length then sets maximum effective range, so it is load-bearing.

It is **not** in the exemption list. It is in a second list, `NotYetThePlayers`,
kept apart from `NotARulesDie` on purpose: an entry in the first is a claim that a
die is not a rules number, and that claim cannot be made honestly here. *"An
exemption list that launders a violation is worse than no list"* — so the
violation sits under a heading that says what it is. Fixing it means giving
StarGrunt a profile of its own (the band-to-rung walk, the band width in inches
and the posture shifts all come off one page, and splitting one out would leave a
half-entered table), which is the next content-policy item and is scoped out of
this pass.

**Still this app's numbers, and not covered by a die guard:** nothing now, in the
dice. The two *number* pairs that went with the dice were the last of them in the
Dirtside module.

### The unit icon attribution is true

The decision was **make it true**, and the check before removing anything was the
load-bearing part. The repo records CC BY 3.0 in three places and nothing else
anywhere; that licence asks for credit wherever the work is **distributed**, and
a repository distributes the sheet whether or not a screen draws from it. So
deleting the credit while the files remain would have turned an over-generous
statement into a breach. **The credit stays; the claim changed** — the artwork
ships and is credited, and no screen draws one yet — in `main.tsx`,
`ATTRIBUTION.md`, the sheet's own header and the README.

Wiring the icons was the other honest option and was not taken: no ground DTO
carries an icon key, so it needs a wire field plus a picker plus a place in the
element row, and which icon a unit gets is a product decision rather than
plumbing.

**The guard for it was a false green twice and had to be rebuilt**, which is
worth keeping:

- The claim was **prose, and prose reflows**. The check matched the sentence *"no
  screen draws one yet"*, JSX had wrapped it across two source lines, and so the
  string appeared nowhere — both branches passed against nothing. The panel
  states its claim as `data-icon-status="shipped-not-shown"` now, an attribute,
  which cannot be line-wrapped.
- The detector **answered yes to a sentence about itself**: it counted any
  mention of `UnitIcon`, and `main.tsx`'s own comment about the credit names the
  module. It reads import statements now.
- The control was **structurally invisible**: it asserted the detector could see
  this test's own import of `UnitIcon.tsx`, and `import.meta.glob` never includes
  the file that calls it. It runs against `ShipCard` instead, a sibling that
  really is imported.

Proven by planting a real production import of the icon module and watching the
guard turn red where it had previously left eleven tests green.

## Closed 2026-09-11 (W3 — reconciling scan 2's last three open rows)

*Branch `fix/w3-reconcile-and-finish`, nine commits on top of `95b289d`. Baseline coming out:
`dotnet build` clean with warnings-as-errors, **1444** .NET tests across 8 projects, **275** web tests,
`tsc --noEmit` and `eslint` clean, both gate scripts passing, and `scripts/two-player-smoke.py` run
locally against a dev pair with zero problems. Going in it was 1423 .NET and 262 web.*

The cross-project roadmap still listed three grouped rows from scan 2 as open: `2F6-2F13`,
`2F19-2F23` and `2F14-2F17, 2F24, 2F25`. **Each item was checked against the code before anything was
done to it, because eight of the twenty had already shipped** in the content-policy rounds and the W2
sweep — the same failure this file was corrected for once already, a list of open rows that were
mostly closed. Each item below has one of three outcomes.

### 2F6-2F13 — one of eight was still open

| # | outcome | evidence |
|---|---|---|
| F6 sparse CSV import invents a stat block | **already done** | `41f07b8`. `normalizeFleetExportShip` reads a missing column as `unentered`, from a constant rather than from the form. |
| F7 fighter endurance 6, reach 24, salvo reach 24 | **already done** | `4883420`. All four sites now clamp from zero, and the salvo refusal only fires against a reach the player entered. |
| F8 `Class-2 Beam` guard is a blacklist of one string | **already done** | `41f07b8`. A rule about shape — the placeholder carries no digit — which the `Class-3 Beam` mutation fails. |
| F9 `forceFormatVersion` asserted against itself | **already done** | `999f646`. A literal `1` plus a hand-kept v1 fixture. |
| F10 four ordnance ceilings diverged 1.67-2x | **still open — fixed** | `9ff783c`, below. |
| F11 settings without `chitPot` swaps the pot for the guess | **wrong as written, now** | `WriteSettings` always writes the pot, the built-in one included and flagged, so no row this service writes lacks one. A foreign or hand-edited row still falls back, flagged as the guess — which the scan itself checked in both directions and judged sound. `fdffb7d` separately made a *mis-shaped* blob cost only the settings. |
| F12 an import with no weapons is handed Rifles | **already done** | `3ec09aa`. The file is refused with every gap named. |
| F13 ship card states four player-owned numbers | **already done** | `b3b74f7`. |

### 2F19-2F23 — three of five were still open

| # | outcome | evidence |
|---|---|---|
| F19 power armour hardcoded `false` | **already done** | `42c237d`, two checkboxes. |
| F20 transient reload error strands the Dirtside screen | **still open — fixed** | `8b71188`, below. |
| F21 a ground vocabulary lives in a `.tsx` | **already done** | `20c23f6`. Both lists are in `groundVocabulary.ts` and the gate compares them. |
| F22 a background failure overwrites the refusal | **still open — fixed** | `8b71188`, below. |
| F23 README is PowerShell only | **still open — fixed** | `0195755`, below. |

### 2F14-2F17, 2F24, 2F25 — all six were still open

| # | outcome | evidence |
|---|---|---|
| F14 a CI check returns on its first line | **still open — fixed** | `c1a50b4`. |
| F15 the two `/status` literals contradict `/ready` | **still open — fixed by removal** | `bd91b3c`, together with the separate F15 removal row. |
| F16 unused `configuration` from the forbidden source | **still open — fixed** | `c1a50b4`. |
| F17 legacy arc hyphen stripped on the server only | **still open — fixed** | `b67a404`. |
| F24 accessibility regression in the Dirtside screens | **still open — mostly fixed** | `9129e95`. Two parts left, below. |
| F25 three copies of "a match becomes live" | **still open — fixed** | `edb080a`. |

### What landed

- [x] **Four ordnance ceilings had drifted, all wider than the server's** (F10). The client allowed
      120 / 48 / 48 / 240 where the service clamps 72 / 24 / 24 / 120, so a restored snapshot drew a
      48-dice salvo the server halved the moment anything touched it. `ordnanceBounds.json` holds
      them once; `ordnanceBounds.test.ts` reads them back through `normalizeMatchSnapshot` and
      `OrdnanceBoundsParityTests` drives both create and update, since changing one of the server's
      three copies is the dullest way past a guard written against create alone.
- [x] **A hyphenated arc name meant two mounts** (F17). `Fore-Port` resolved to ForePort on the
      server and fell back to Fore on screen. `legacyArcCases.json` holds thirteen spellings for both
      languages, including that the blind spot is never produced, that an explicit arc list still
      wins, and that `Fore/Port` falls back rather than being guessed at.
- [x] **The two `/status` routes are gone** (F15, and the −6 F15 removal row). Compile-time literals
      whose only provable fact was that the flag was on — which `/api/features` answers from the
      same `FeatureFlags` instance — and whose `"playable"` had already drifted from `/ready`'s own
      Dirtside warning. The three test files probe the create route now. The removal is asserted
      with the flag **on**, because a 404 with it off proves nothing.
- [x] **The deployment gate says which checks ran** (F14). The overlay check is kept, since the
      overlay will be added again, but the gate now prints `5 of 6 ran` and names the one that had
      nothing to examine. Controls: an overlay copied from `appsettings.json` exits 1, a real one
      exits 0 with 6 of 6.
- [x] **`ReadDeploymentWarnings` no longer takes an `IConfiguration`** (F16). It never read it, and
      the call site passed the pre-build configuration this file warns about twice.
- [x] **One `Register` against one `Forget`** (F25). `CreateMatch`, `LoadPersistedMatches` and
      `RestoreMatch` each wrote the four registration steps by hand and `CreateMatch` skipped
      `IndexMatch`. Harmless there; also the rule becoming "four steps, except when it is three".
      `MatchRegistrationTests` holds all three entrances to the same four properties; dropping
      `IndexMatch` reddens the two with something to index, dropping `Persist` reddens all three.
- [x] **The Dirtside reopen offers Try Again** (F20). A 500 on reload used to leave a disabled
      "Reopening last game..." and one live control, Forget Last Game. The handle is kept and a
      retry re-runs the effect; a 404 is still forgotten and gets no retry.
- [x] **A background refresh failure goes to the activity line** (F22). The success path had been
      fixed for exactly this reason and the failure path had not. An expired session still takes
      the message line, because that is the session ending rather than a report.
- [x] **The Dirtside screens name what each button acts on** (F24). Per-element Move, Stand down,
      Sensors, Recover systems and Aim, each platoon's Activate and each chit row's Remove carry
      the name in their accessible label. Refusals that lived only in a `title` on a disabled
      button are rendered as text, and the assault round is a live region.
- [x] **The README runs on the machine CI runs on** (F23). Prerequisites with the Node 22.4 reason,
      `pwsh ./scripts/verify.ps1`, `dotnet test` / `npm test` / `npm run lint`, and POSIX blocks.

Every fix above carries a control that had to fail before the fix, and each guard a control that had
to be accepted after it.

### Deliberately left

- **Two parts of F24.** Focus is not moved when an assault stage unmounts the form holding it, so
  the next Tab starts at the top of the document; and there is still no `<form>` element anywhere,
  so Enter and a tablet's Go key submit nothing. Both touch every screen's submit path and are a
  design pass rather than a tail edit.
- **Interception**, and **StarGrunt's band-to-rung walk** in `RangeBands.cs`. Both are the owner's —
  see "Interception: finished up to the rules, and stopped there" and `NotYetThePlayers`.

## Closed 2026-09-11 (W2 — the half-wired sweep)

*Five passes looking for unwired and half-wired things, then fixing them. Six commits on top of
`5175801`. Baseline coming out: `dotnet build` clean with warnings-as-errors, **1397** .NET tests
across 8 projects, **243** web tests, `tsc --noEmit` and `eslint` clean,
`scripts/check-ground-vocabulary.py` passing, and `scripts/two-player-smoke.py` run locally against a
dev pair with zero problems and zero console errors. Going in it was 1391 .NET and 233 web.*

- [x] **A ship at rest can be turned again.** `FullThrustLightCinematicRules` carves out a stationary
      ship by name, twice — `MaxTurnSteps` returns 12 and `Validate` lets `rotatingAtRest` past both
      the thrust-spend check and the turn cap — because a ship at velocity zero that is not
      accelerating rotates to any heading for free. The client's `maxLegalTurn` took no velocity at
      all, so it could not tell a stationary ship from one under way and answered ceil(thrust/2) for
      both. That number draws the compass's PORT and STARBOARD LIMIT, caps its slider, trims a plot
      in `clampDraftForShip`, and is what the map tests before answering *"has no turn points left"*.
      Proven by calling both sides: the resolver answered 12 for a thrust-4 ship at rest, the client
      answered 2. **Not an edge case** — `defaultShipForm.currentVelocity` has opened at zero since
      the content-policy work, so every ship is created in exactly this state, and with thrust also
      unentered `maxLegalTurn(0, 0)` was 0 and a new ship could not be turned at all. An instance of
      shape 6: the blanking was right and it made a rare carve-out the default state.
      `turnLimitCases.json` now holds the client and the resolver to each other across the language
      boundary, with a second test checking the ceiling describes what `Validate` will accept.
- [x] **The firing console states the fire control it has already spent.**
      `FiringSolutionDto.EngagedTargetCount` and `.TargetScreens` were computed, put on the wire and
      declared at `types.ts:176-177`, and read by nothing in either language — while every sibling
      field on that DTO is rendered. A ship directs one target per working fire control system and
      `PrepareShot` refuses past it, so the readout was showing the capacity and hiding the usage and
      the only way to learn a firecon was spent was to pick a target and be refused. Both now render,
      through one `firingReadoutNotes` in `lib/rules.ts` rather than a third copy of a readout that
      was already written out twice.
- [x] **Power armour can be declared.** `CloseAssault.Fight` doubles a figure's melee score for it,
      the flag runs end to end from `MeleePairingDto` to `Combatant` — and `StarGruntView`, the only
      caller of `fightMelee` in the app, sent a literal `false` for both sides with no control
      anywhere. A table fielding power-armoured troopers fought every melee at half strength. Unlike
      the support-weapon flags beside it there was no import workaround either, because the pairings
      are built in the component at click time. Two checkboxes, matching how the panel already
      applies one set of values across N pairings. Per-pairing values stay unreachable and are a
      separate, larger piece of work.
- [x] **The ship form's bounds and the service's clamps are held to each other.** Seven numbers, each
      written once as a `min`/`max` in `ShipCard.tsx` and once as a `Math.Clamp` in the service, with
      nothing tying the pair. They agree today; `shipFormBounds.json` is what keeps them agreeing.
      The firecon cap of `6` that prompted this is **a shape bound, not a rules number** — no
      `RulesProfile` field carries it, the profile editor has no cell for it, and fire control is
      limited by what a hull can carry rather than by a flat ceiling — and the reason is now written
      down rather than inferred, because *"it has always been 6"* is not an argument.
- [x] **The last two ground vocabularies join the gate, and the gate can see them.** `valueScales`
      and `chitColourSets` lived in `DirtsideAssaultPanel.tsx` beside their own dropdowns, absent
      from `DirtsideWire` and `groundVocabulary.ts` while the server parses both by name. **The
      reason nothing noticed is the useful half:** the gate's local-redeclaration scan matched
      `^const <name>` and both are `export const`, so it looked straight past them and reported the
      vocabularies single-sourced. Its name list was also maintained separately from its own
      comparisons. Both fixed. `ChitColourSets` is held to `ChitColours` as a **subset** rather than
      an equality, with the reason: it is a `[Flags]` enum carrying `None` and every pair, and
      widening what the screens offer is a UI decision nobody has made (still open, see #12).
- [x] **`CarrierOperation` removed.** A two-member enum reached by nothing — no property typed with
      it, nothing constructing one, no test naming it. A word-boundary search across the repository
      returns exactly one line, its own declaration; a substring search returns more and all of them
      are `FullThrustCarrierOperationRules` and `ResolveCarrierOperation`, which is presumably how it
      survived earlier passes. Removed rather than wired because the launch-or-recovery distinction
      is already carried by the `bool isLaunch` its would-be caller takes.

### Found by W2 and deliberately left, with the evidence

*So the next sweep need not re-derive them. Each is a feature someone started or a decision that is
not a mechanical edit, not an oversight.*

- **`DefensivePosture` is a reader with no writer.** `HitResolution.PostureDie` branches on
  `SoftCover`, `Evading` and `HullDown` and is genuinely wired into `DirtsideGame.Fire`, but nothing
  in production ever sets `ElementStatus.Posture` to anything but `None`: there is no `posture` field
  on any contract, so the client cannot send one. The whole posture die-shift mechanic is inert, and
  `TurretDown` has no arm in `PostureDie` either. Wiring it is a wire field plus a UI plus a decision
  about when a posture is declared — the same class as interception, and a feature rather than a
  tail item.
- **`UnitIcon.tsx` is a subsystem with no entry point.** An eighteen-icon component, a 23k sprite
  sheet in `assets/`, a `.unit-icon-svg` rule in `style.css`, a licensed artwork set, and
  `UnitIcon.test.ts` as its **only** importer — searched across `src/`, `tests/` and `scripts/` for
  both `UnitIcon` and `unitIcon` in `.ts`, `.tsx`, `.cs`, `.py`, `.json`, `.html` and `.css`. No
  ground screen renders one and no ground DTO carries an icon key. It is also the one item here with
  a **live user-facing consequence**: `main.tsx:1294-1302` credits game-icons.net in the app's notice
  panel and `ATTRIBUTION.md` documents the set as used, while the module is never imported and so
  never reaches the bundle. Wiring it needs either a wire field and a picker, or a client-side
  derivation like `normalizeShipIconKey`; removing it discards a curated, licensed, tested asset set.
  **Left for an owner decision precisely because the cheap answer is not obviously right**, and
  flagged because the credit is a statement about the app that is not currently true.
- **`ShipDto.HullRowsCompleted`** — computed by `FullThrustLightThresholdRules.RowsCompletedFor` on
  every snapshot and read by no client code; its sibling `hullRows` *is* rendered. A one-line render
  once somebody decides where the damage track should say it.
- **`ShipClassBand.Capital`** — produced by `FromIconKey` for dreadnoughts and carriers, and
  `RowCountFor` has no arm for it, so it falls to the same default as an unrecognised band.
  `RulesProfile` has `EscortRowCount` and `CruiserRowCount` and no `CapitalRowCount`, so this is a
  missing profile field rather than a missing branch.
- **`ShotRefusal`'s four reasons** — each assigned at its own call site and only ever consumed as
  `Refusal == None`; the human-readable distinction travels separately as free text. Harmless today,
  and worth knowing before someone adds a fifth expecting it to be branched on.
- **`StarGruntAction.GoInPosition`** — reachable from the UI and accepted by the server, with no arm
  in `Cost`, `IsLeaderAction`, `MayRepeat` or `AsSuppressedAction`. Taking it spends the activation
  and does nothing else; "in position" is declared per shot instead.
- **Ten `FiringResultDto` fields the client never reads** (`WeaponName`, `TurnNumber`, `Range`,
  `RangeBand`, `Arc`, `RawDice`, `RangePenalty`, `SystemPenalty`, and `MapRange`/`RangeDisagreed`
  which are not even mirrored on the TS type). Four of them are read back on the restore path, so
  they are not dead — they are an after-action review nobody has built the screen for.

### Checked and found properly wired

- **The namespace-import escape hatch hides nothing.** `libSurface.test.ts` excuses a module whole
  once anything does `import * as api`, and says so — 42 functions across `dirtsideApi.ts` and
  `starGruntApi.ts` sit inside that exemption. Every one of them is really spelled by an importer as
  `api.<name>`. That was the largest stated hole in the app's reachability guard and it is empty.
- **Every contracts field was walked in both directions.** 96 records, 577 fields, writers and
  readers classified separately, because a name count cannot tell them apart — the code that writes
  a DTO field mentions it exactly as often as code that reads it. Six candidates came out; two were
  the already-recorded `HasChosen`/`ElementsStillToChoose` and four are above.
- **`UnitIcon.tsx` is the only production module unreachable from `main.tsx`.** The import graph was
  walked from the entry point; everything else in `src/` is reachable.
- **Thirty-one enums are fully produced and consumed**, including `TurnDirection`, `ShipSystemKind`,
  `QualityDie`, `FiringArc`, `WeaponKind`, `ChitColour`, `ChitSpecial`, `HitEffect`, `AssaultStage`
  and `ConfidenceLevel`.
- **The feature flags and every configuration key are read**, and the three deployment keys are
  already cross-checked against `.env.example` and `docker-compose.yml` by a CI script.
- **The six vocabularies the gate already covered are identical** between `DirtsideWire`/
  `StarGruntWire` and `groundVocabulary.ts`, line for line.

## Closed 2026-09-10 / 2026-09-11 (scan 2, and the content policy)

*Twenty-seven commits across four rounds, on top of `f422f2f`. Measured baseline coming out, on
2026-09-11: `dotnet build` clean with warnings-as-errors, **1391** .NET tests across 8 projects,
**233** web tests, `tsc --noEmit` and `eslint` clean, and `scripts/two-player-smoke.py` run locally
against a dev pair with zero problems and zero console errors. Going in it was 1357 .NET and 166
web.*

### Data loss and availability

- [x] **Force export wrote a D6 over every armour die the player picked.** Both halves of a bad
      thing at once: a rules number this app does not own, written into the player's own file, and
      silent loss of what they had typed. The round trip could not see it, because import read back
      whatever export had written. The client had nowhere to read an armour die *from* - the
      snapshot did not carry one - so this was a three-layer fix rather than a discarded local
      field: `StarGruntUnitDto` carries `Figures`, the client type matches, and export writes them.
- [x] **An unreadable profile import blanked the profile the player had typed**, and so did a valid
      file of the wrong kind. Both doors are gated; the valid-JSON-wrong-file door is the likelier
      mistake and the original finding had not named it.
- [x] **A profile file missing most of itself blanked the rest.** `looksLikeProfile` accepted any
      object carrying two known field names, and `readProfile` then spread it over a blank, so
      `{ name, dieFaces }` wiped the other twenty-eight numbers. What is refused is the **loss**,
      not the incompleteness - see the withdrawn claim below.
- [x] **The saved-profile store handed back zeros and then wrote them down.**
- [x] **The force Export button silently did nothing on a skewed snapshot.** `toForceFile` threw out
      of a bare `onClick` outside the `run()` wrapper, so nothing downloaded, nothing was said, and
      the button looked exactly like a button that had worked.
- [x] **`/ready` reported the reality of one store out of three.** A file whose matches table was
      healthy and whose `dirtside_games` table was not said `sqlite` with no warning while every
      Dirtside game went to memory. Each store now records what it turned out to be as it is opened,
      and a split answers `mixed`.
- [x] **A readable database with a wrong-shaped table stopped the host**, for Full Thrust and both
      ground engines.
- [x] **A mis-shaped `settings` blob retired a perfectly readable game.**
- [x] **`/ready` reported what each store was at startup and never looked again** (2026-09-11). The
      third time this endpoint has been fixed for reporting something other than the present truth:
      first the configuration rather than what opened, then one store's reality as the state of
      three, and now one instant's reality as the session's. A volume unmounted mid-session or a
      disk that fills produces a 503 per write while readiness says `sqlite`. `ReportingMatchStore`
      wraps each engine's store and records the change in both directions; the recovery half is the
      control, because a report that latched on the first failure would be wrong for the rest of the
      session.

### Content policy — what closed

The project's defining constraint is *the engine owns procedures, the player owns every number those
procedures read*. This is where it stood on 2026-09-09 and where it stands now.

- [x] **The new-ship form opens unentered.** Thrust 4, velocity 8, hull 12, armour 4, firecons 2,
      point defence 1, damage control 2 and a screen are gone. Zero rather than a smaller default:
      the server's clamps floor each of these exactly as they floor a mount nobody filled in. Course
      and position keep their values and had to argue for it - a heading has no zero on a 12-point
      clock, and where a model sits on the felt is settled with a tape measure.
- [x] **The fleet import stopped reading its fallback off that form**, so a missing column reads
      back unentered rather than as whatever the new-ship form happened to open on. Text still falls
      back; numbers do not.
- [x] **The fleet import stopped trimming screens to a ceiling this app invented.** The flat `3` was
      `RulesProfile.MaxScreenLevel` - the player's number - so a table playing to 5 lost two levels
      off every ship in their own file, silently, on the way in.
- [x] **The invented `Class-2 Beam` mount, and the guard that could not see it renamed.** The old
      guard was a blacklist of one string; `'Class-3 Beam'` walked past it with the whole suite
      green. It is now a rule about shape: a weapon class in this game is named by a number, so a
      placeholder carrying a digit is a reading off somebody's card whatever the digit is.
- [x] **The map stopped refusing a fighter group against a number nobody entered.**
      `constants.ts`'s `fighterMoveAllowance = 12` is deleted and the allowance comes off the
      snapshot. A zero allowance refuses nothing, because the server is the authority and inventing
      a limit is the defect.
- [x] **The server's fighter endurance 6, fighter reach 24 and salvo reach 24.** The salvo one was
      the sharp one: not a stored default but a **refusal**, telling a player their point of aim was
      "past the 24 this salvo can reach" when nobody had entered 24.
- [x] **The ship card and the map stated four of the player's own numbers as fact** - "inside 6mu",
      the repair odds, "takes a system on a 6", and a "24 for a standard load, 36 for extended range"
      that `constants.ts` said in as many words this app had stopped shipping - in tooltips and
      captions a player cannot overwrite.
- [x] **The fighter boxes still stated the 6 and the 24 the engine had stopped inventing**
      (2026-09-11). `value={form.fighterEnduranceMax || 6}` and `value={ship.fighterMaxRange || 24}`,
      on four controls across two screens. On the map it contradicted the caption a line above,
      which reads the real zero. `min={1}` was the other half: a table that plays no endurance rule
      and typed 0 had it clamped back up to 1 and committed to a server that accepts zero.
- [x] **A ship posted with no fire control was given one.** `CreateShipRequest.FireControlMax = 1`
      and its twin on the update request: a default parameter on a contract record **is** the wire
      contract, because the JSON deserialiser fills a missing property from it.
- [x] **Every weapon anybody entered came back carrying a D6 it had never been given.**
      `StarGruntWeaponDto.SupportFirepowerDie = 6`, in three places at once - the DTO, the module's
      `WeaponProfile`, and the client's add-unit panel, which hard-coded a 6 onto a weapon it also
      hard-codes as not being a support weapon.
- [x] **A salvo that said nothing about itself flew for one turn.**
      `CreateOrdnanceMarkerRequest.EnduranceRemaining = 1` and its twin.
- [x] **The force import answered a missing die with one of its own** (2026-09-11). Quality die 8,
      impact die 8, armour die 6 - and a unit that listed no weapons at all was issued one, called
      Rifles, with an impact die to go with it. `dieFrom` takes no fallback now; the file is turned
      away with every gap named rather than importing a roster with numbers in it nobody entered.
- [x] **The add-a-squad panel shipped a record card under a caption saying it did not** (2026-09-11).
      The StarGrunt screen's own caption reads "Transcribed off your own record card. No stats are
      supplied here", and the form opened on quality D8, Leadership 2, eight figures, armour D6 and
      an impact die of 10. The fire panel opened on a D8 of firepower and a range of ten inches, and
      the confidence test opened on threat level 2 beside a caption saying the threat level is the
      one your own table gives the event.
- [x] **The dice the StarGrunt engine chose for itself** (2026-09-11). Four, none of them on the
      wire or on a type: the armour of a unit with no roster (`QualityDie.D6`, reachable through a
      saved game written before figures were on the definition, and the die that decides whether
      each hit kills); `UnitDefinition.QualityDie = QualityDie.D8`, which on a JSON restore is the
      contract; the flat D6 the downed were settled on while the *bands* came off the player, which
      made a D10 chart's stunned band unreachable; and two D12 placeholders filling a non-nullable
      slot on a refusal.

### Content policy — the three guards

Each names no bad value. Each enumerates and demands a written classification, so a **new** number
fails the suite until somebody argues for it.

| guard | what it can see | what it found |
|---|---|---|
| `contentPolicy.test.ts` + `contentPolicy.render.test.tsx` (web) | the numbers the client stores, and the numbers it says - text, tooltips and now the values in its controls | the ship form, the ordnance draft, the fleet import, the mount label, the ship card and map prose, the StarGrunt screen's three forms, the fighter boxes |
| `WireDefaultsContentPolicyTests` (API) | every numeric constructor parameter of every contract record, by reflection | five non-zero wire defaults, all real |
| `EngineDiceContentPolicyTests` (StarGrunt module) | `QualityDie.Dn` in the module's own source, outside comments | four die literals in method bodies and property initialisers |

The third exists because the first two cannot see a number chosen inside a method body: it is on no
type, on no wire, and in no metadata, so reading the source is the only instrument that reaches it.
It is scoped to the StarGrunt module rather than to every engine, on that module's own stated
contract that it ships no table at all.

### Claims withdrawn or corrected

- **"Blanking `defaultShipForm` needs a browser."** Wrong, and it deferred the item for a round. The
  question was what the server does with a body full of zeros, and a test that posts exactly what
  the blanked form posts answers it: accepted, hull floored to 1, everything else genuinely zero.
- **"Refuse a profile import that is incomplete."** An over-strict fix *is* a content-policy
  violation. Requiring all thirty fields refused a legitimate seven-field profile and broke a CI
  job: a table that does not play the torpedo and salvo rules writes a good file without them, and
  refusing it is this app deciding which optional rules a table has to use. What is refused is the
  **loss** - a file that would blank something already on the form - named field by field.
- **"Delete the support firepower die."** Deleting it outright would have made the die function
  refuse every rifle, because `Die()` refuses a face count off the quality ladder and zero is not on
  it. The answer was a nullable die refused *by name* only when actually asked to join a volley.
- **The armour-die proof was right and its evidence was not.** The original probe built a unit
  carrying `figures: [{ armourDie: 12 }]` through a cast, at a commit where neither the DTO nor the
  client type had such a field - so it asserted against an object the real client could never have
  held. The finding stood; the proof was replaced.
- **The `/ready` three-store proof was a false green by construction.** Its four assertions were all
  true of a healthy stack too, so it could not have distinguished the two states. The conclusion was
  right and the probe could not have shown it; rewritten to assert the fallback is real on disk
  before claiming anything about readiness.
- **The `Class-2 Beam` blacklist and the `forceFormatVersion` tautology were guards that could not
  fail.** The version test asserted `toForceFile(...).formatVersion === forceFormatVersion`, and
  `toForceFile` stamps the field from that same constant, so bumping it to 2 - exactly the change
  that strands every file already written - left 166 tests green.
- **Two library surfaces offered names nobody took.** Four in `rulesProfile.ts` and nine more across
  six other modules. An export nobody imports is a decision nobody made: it offers a second way to
  read the player's numbers, one of which coerces. Eight of the nine stopped being public;
  `assaultStages` stays because `scripts/check-ground-vocabulary.py` reads it out of the source in
  CI and matches on the `export` keyword, which a widened regular expression would have got wrong.

### Content policy — what remains, plainly

*"This app ships no rules numbers" is the kind of claim that rots quietly, so this is the list as of
2026-09-11, not a summary of it.*

- **The Dirtside die tables in `HitResolution.cs` — open, and the largest remaining item.** A fire
  control level is worth a D6, a D8 or a D10; a defensive posture is worth a D6, a D8, a D10 or a
  D12; and a target's signature indexes the quality ladder directly. These are readings off
  somebody's card in exactly the sense this policy is about. They are left because that module's
  whole combat model is built on them, so removing them is a design change rather than an edit — and
  because an exemption list that launders a violation is worse than no list, they are recorded here
  and in a note at the foot of `EngineDiceContentPolicyTests` rather than exempted. **The new engine
  guard is deliberately scoped to StarGrunt and does not cover this.**
- **The Dirtside fallback chit pot.** Unchanged: the counts are a documented guess, kept for one
  release behind a `/ready` warning that says so in as many words and a red line on every snapshot,
  then removed. The two invariants that are policy-safe — the firer/target ordering and the specials
  being a minority — are guarded; the counts themselves are deliberately not pinned, because pinning
  them would be this app asserting a number it does not own.
- **`fleetIo.ts`'s `screenLevelCeiling = 9`.** Not the rule — the rule is the profile's
  `MaxScreenLevel`, and the file's number is now read as written and clamped by the server against
  it. What is left is a bound on the *shape* of the number: the profile editor's beam grid draws at
  most that many screen columns, so a file claiming more is malformed rather than generous.
- **The `min`/`max` bounds on numeric inputs**, and the matching clamps in the service. Deliberately
  outside the render guard: they bound what can be typed rather than stating what was, and the
  server's own comment calls the fighter pair "a bound on abuse, not a rule". Worth revisiting only
  if one of them is ever found to be a rulebook number in disguise, as the screens ceiling of 3 was.
- **The ground vocabularies** (`DirtsideWire.ChitColours`, `ChitSpecials`, `StarGruntWire.Ladder`,
  `assaultStages`). Which chits and dice *exist* is the engine's; how many of each and which one
  anything uses is the player's. Not a gap.

## Closed 2026-09-09 (maintainability tail)

*The rows left over from `vault/maintainability-2026-09-09.md` after the two earlier passes. Every
claim was re-derived before acting; two of them were wrong, and one was wrong in a way that made the
real defect worse than reported.*

- [x] **An empty origins list in `appsettings.json` was disabling both CORS fallbacks (#20).** The
      finding said an empty JSON array "yields no config children" and therefore overrides nothing.
      It yields a **non-null empty array** from the binder, so
      `GetSection("Cors:AllowedOrigins").Get<string[]>()` was never null, the `??` chain never fell
      through, and neither read underneath it could fire unless something had already put a value on
      that same key. A declaration that configured nothing was silently switching off the fallbacks
      below it. `NonEmpty` makes each read fall through only when it found no origins rather than no
      key. The empty array stays, naming the key for whoever opens the file, and is now harmless.
- [x] **The CORS fallback read a variable no document names (#20).** Every other `FORCESIGNAL_` name
      in `.env.example` is honoured directly by the code as well as through compose's interpolation --
      `FORCESIGNAL_MATCH_DB` by `ReadMatchDatabasePath`, the two `FORCESIGNAL_FEATURES_` names by
      `FeatureFlags`. CORS read `FORCESIGNAL_CORS_ALLOWED_ORIGINS`, which appears in no README, no
      `.env.example`, no compose file and no test, so an operator who set the documented
      `FORCESIGNAL_WEB_ORIGIN` and started the API without compose was refused startup citing a
      setting they had supplied. It now reads `FORCESIGNAL_WEB_ORIGIN`.
      `ProductionHost_WithTheDocumentedWebOriginVariable_StartsRatherThanRefusing` fails on both
      halves of the previous state.
- [x] **The invented `Class-2 Beam` is gone from all four places (#17).** The scan named three; the
      fourth was the server's, at `InMemoryMatchService.Normalization.cs`, and it was the worst of
      them because it was invisible -- a ship created with an empty weapons list came back armed with
      a class name, a damage rating of two dice and a reach of twenty-four, and nothing told the
      player the server had written the stats for them. A ship that named no mounts now has none.
      The client's three copies collapse to one `newWeaponMount()` holding a placeholder label and
      the floors the server's own clamps impose, which mean "not entered".
      `constants.ts`'s own header already said a starting point the player is "expected to replace"
      is still a number this app shipped.
- [x] **Three dead members removed (#19), each re-verified.** `FeatureFlags.IsFullThrustOnly` (only
      callers were two assertions in `FeatureFlagTests`; the gate reads `StarGrunt` and `Dirtside`
      directly). `DirtsideBoard.RemoveElement` (zero references anywhere in the repository, not even
      a test). `DamagedEffects.CanFireAt` -- reported as "a written, tested rule never applied", which
      is **wrong**: the rule is applied, through `EffectiveRangeBand.CanFire`, which is what
      `DirectFire.Resolve` asks and which consults `DamagedEffects.Band`. `CanFireAt` was a second
      way to ask the same question with no caller but its own test; the wrapper went and the coverage
      moved to the property the resolver actually reads.

**Verified dead and deliberately left, with the evidence, so the next pass need not re-derive it:**

- `DirtsideElementStateDto.HasChosen` and `DirtsideSnapshotDto.ElementsStillToChoose`. Both are
  written by `DirtsideGameService` and read by no production code in either language -- the screen
  uses `hasMoved`/`hasTakenCombatAction`/`hasStoodDown` and `canEndActivation`/
  `whyActivationCannotEnd` instead, which is what `HasChosen`'s own remarks say it should. Left
  because removing a snapshot field is a wire change and the better answer for
  `ElementsStillToChoose` is probably to render it, the way the order preview's verdict was.
- `DirtsideWeaponDto.IsInterceptable` and `WeaponDefinition.IsInterceptable`: a complete write-only
  chain -- copied DTO to definition at `DirtsideGameService.cs`, read nowhere, and not even sent by
  the client, whose `DirtsideWeaponInput` does not carry the field. Its doc defends keeping it as
  "the player's own reading of their card", which is not true while no client sends it. Left because
  interception cannot be resolved by this API at all (see the area-defence entry above), so the
  honest options are to wire the whole feature or to drop the field, and neither is a tail item.
- ~~`GET /api/stargrunt/status` and `GET /api/dirtside/status`~~ -- **removed 2026-09-11** (W3,
  `bd91b3c`), once a later pass was already in the three test files. Kept below as it was written:
  no client call, no script, no
  healthcheck; the client asks `/api/features`. Used only by tests as a 200-vs-404 probe for the
  feature flag, which `/api/features` already answers. They also state engine readiness a third time
  (`"in-development"`, `"playable"`) beside `/api/features` and the `/ready` warnings, which is the
  drift this repository has paid for elsewhere. Left because removing them is a three-file test
  rewrite rather than a tail item.
- ~~`defaultShipForm`'s remaining stat numbers in `constants.ts`~~ -- **shipped 2026-09-10, and the
  reason for leaving it was wrong.** The claim was that blanking them needs the create-ship form
  driven in a browser to see what a zero does. It does not: the question is what the *server* does
  with a body full of zeros, and
  `ContentPolicyTests.AShipThatEnteredNothingComesBackWithNothingEntered` sends exactly what the
  blanked form sends. The ship is accepted, hull floored to 1 and every other number genuinely zero.
  No browser, no 500, no invented value. What actually needed a browser was only the *feel* of a form
  full of zeros, which is a design question and blocked nothing. See the content-policy section
  below.
- Roughly 32 further engine rules with tests and no Application caller (infantry combat entire,
  opportunity fire, reaction fire, `ForfeitPriority`, `DeclineReaction`). These are the unfinished
  slices the readiness warnings already name out loud, not dead code.

## Closed 2026-09-08 (audit follow-up)

*Maintainability pass, 2026-09-09. Eleven items from `vault/maintainability-2026-09-09.md`, added
here rather than under a heading of their own because they are the same kind of work.*

- [x] **`.env.example` reaches the container.** Five of its variables were interpolated by nothing,
      so setting them did nothing, and `Proxy__*` and `Features__*` were not in the file at all
      although the README says to set them. Compose hands `.env` to a service only through `${...}`,
      so every documented name is now interpolated. `scripts/check-deployment-config.py` holds the
      two files to each other in both directions and fails on the previous state.
- [x] **`/health` carries the security headers.** It was the one nginx location without the include,
      because it declares a `Content-Type` of its own and nginx drops the parent's `add_header`
      lines the moment a location declares one -- the trap the include file's own comment describes.
      Checked statically, and read off the running container by `docker-smoke.ps1`.
- [x] **The Docker smoke test is a gate.** CI never passed `-IncludeDocker`, so the only assertion
      that the volume and the configured database path take effect -- readiness saying `sqlite`
      rather than falling back to memory -- had never run. It runs on every push.
- [x] **The two-player smoke test is a gate.** `scripts/two-player-smoke.py` is the only check that
      hidden orders lock and reveal independently on two devices and that a phase turning over on
      one reaches the other without a reload. It now has its own CI job.
- [x] **`Cors__AllowedOrigins=a,b` is read.** Reported as unreachable code; it was worse. The
      environment provider rewrites `__` to `:` before configuration is asked, so the literal key
      could never match, and the section read above it cannot bind a scalar to `string[]` either --
      so the API refused to start outside Development citing a setting the operator had set.
- [x] **The wire vocabularies are the single source.** `DirtsideWire` and `StarGruntWire` read as
      canonical and had no consumer: the client typed its own copies out beside the dropdowns, the
      server validated against the engine enums, and the refusal messages carried a third copy as
      literal text. The messages are built from them now, tests hold them against the enums, the
      client imports one module, and `scripts/check-ground-vocabulary.py` crosses the language
      boundary the tests cannot.
- [x] **Area-defence sensors buy something.** The combat action set a flag that no rule read, so an
      element that had spent nothing could intercept as freely as one that had. The engine's own
      interception now refuses an element whose sensors are dark; `InterceptionOpening` stays null
      and says accurately why -- an open window blocks every step and no route answers one, so
      opening one would deadlock the game rather than enrich it.
- [x] **A damaged element reports the movement it has.** `DamagedEffects.Movement` was written,
      tested and applied to nothing, so a damaged vehicle advertised the distance it could cover
      when it was whole. The snapshot carries the halved figure and the screen renders it.
- [x] **The order preview's verdict is shown.** `IsValid` and `Errors` exist so a player can find
      out why a plot will not lock before pressing Lock; both were fetched and thrown away.
- [x] **`FeatureFlags` describes the gate it has.** Its remark claimed three checks and named one on
      match creation that no code performs -- a match carries no ruleset to check against.
- [x] **StarGrunt's readiness warning names its slice boundary**, as Dirtside's already did:
      transferring an activation to a subordinate and reaction fire are in the engine, tested, and
      unreachable from this API.
- [x] **Three members with no callers removed**: `MatchHub.LeaveMatchGroup` (reachable code with no
      caller, since a hub method is remotely invokable), `ChitPot.Composition`, and the redundant
      `appsettings.Development.json`. `RepairContracts.cs:14` was reported dead and is not.

- [x] **A locked order cannot be moved out from under itself.** `UpdateShipProfile` wrote position,
      velocity and course with no guard at all, so a player could watch the reveal and then
      reposition. The four commitment fields are now frozen from the moment *that ship* locks until
      the turn is executed. Keyed on the ship's own commitment rather than on the match phase, which
      is what the first attempt got wrong: a ship locks while the match is still in `OrderEntry` --
      the phase only turns over when everyone has locked -- so a phase test left open the whole
      interval between the first lock and the last, which is exactly the interval a player sitting
      on a locked order would use. Name, hull and points stay editable; a typo noticed mid-turn
      should not have to wait a turn. Firing is deliberately not covered.
- [x] **A locked order can no longer be destroyed by editing its draft.** The server holds a hash
      and nothing else, by design, so the local draft is the only copy of the plaintext. Editing it
      between the lock and the reveal meant `Verify` failed and -- once anyone else had revealed --
      the order was discarded and the ship held course and speed, with nothing telling the player
      they had lost a manoeuvre. Every plotting control routes through `updateDraft`, so the guard
      sits there; "Copy Fleet" now skips locked ships and says how many it left alone, rather than
      invalidating a squadron's commitments in one tap. The rule lives in `lib/orders.ts` so the two
      callers cannot drift apart on what "locked" means.
- [x] **The client no longer ships ship stat blocks.** `constants.ts` carried seven named classes
      with hull, armour, screens, fire control, point defence, thrust and weapon mounts, plus
      per-weapon maximum ranges, and `ShipCard` clamped a torpedo to one shot and a published reach
      on kind switch. Presets now name a class and pick an icon and stop there. The ranges were
      doubly wrong to hold here: they are published numbers, *and* they were already the player's --
      `torpedoMaximumRange` and `needleBeamRange` are fields on the rules profile, so the constants
      were a second copy nobody had entered and nobody could edit. See the README's Content Policy.
- [x] **Readiness reports the persistence it has, not the one it asked for.** A database file that
      will not open leaves the server running on memory -- deliberately, so one bad file does not
      end every game on the machine -- but `/ready` read the configured path and said "sqlite"
      anyway. So the operator believed their games survived a restart, and the container's own
      healthcheck agreed with them, right up until the restart that ended all of them. It now asks
      the store the match service was actually handed, and warns when the two disagree. The path
      stays out of the warning: that route answers anyone who can reach it, and the API log already
      names the file for whoever has to recover it.
- [x] **`docker-smoke.ps1` passes on a healthy stack.** It asserted `persistence == "in-memory"` and
      demanded at least one warning, from a stack whose compose file mounts a volume and configures
      sqlite and therefore has nothing to warn about -- so it failed on exactly the deployment it
      exists to check. Both assertions were inverted rather than dropped: sqlite, and *no* storage
      warning, which is precisely the fallback above. Fixing the script alone would have left the
      readiness report untrustworthy; fixing readiness alone would have left the script wrong.
- [x] **A restored shot's dice have a ceiling.** Every other collection on the untrusted restore
      path was capped and this one was not, which left one shot holding however many dice the file's
      author felt like -- kept for the after-action review and re-serialised into every snapshot
      thereafter. Refused by name beside the others rather than truncated, so the file is rejected
      instead of silently altered.
- [x] **"Finished plotting" can be taken back.** It was reset only by `AdvanceTurn`, so a tap meant
      for something else handed the turn over with unordered ships holding course and the only way
      back was to play the turn out. It can now be withdrawn for as long as it means nothing -- while
      order entry is still open. Once the last admiral declares, the orders are sealed and re-opening
      the turn is the table's business rather than one player's. The flag rides on the existing
      request and defaults to true, so a client that only ever says "I am done" is unchanged. The
      client half matters as much: "Lock Fleet Orders" declares the plotting closed in the same tap,
      so **Resume Plotting** now appears beside it while -- and only while -- the declaration still
      means nothing. Orders already locked stay locked; a lock is a promise.
- [x] **`scripts/two-player-smoke.py` runs, and passes.** `docs/rules-fidelity-gaps.md` recorded that
      no two-device match had been played end to end since the turn structure changed, and the script
      that would have checked it was referenced from nowhere and no longer worked: a match now
      arrives with an empty rules profile and readiness refuses to start without one, which is
      exactly the guard added earlier in this pass. The script brings its own profile -- invented,
      loaded through the editor's own Import JSON -- and now drives two browser contexts through
      create, join, ready, the early-tap take-back above, independent lock and reveal, and two phase
      turnovers seen live on both devices. Zero problems, zero console errors.
- [x] **Opening a ground game is budgeted.** The two ground creates take no credentials and allocate
      state kept for a day, exactly like `POST /api/matches`, which has been rate-limited for that
      reason since it was written. They join that same budget rather than getting one each: the
      machine does not care which engine filled it up, and three allowances would just mean three
      times as much of it.
- [x] **A game with other than two sides no longer 500s on its first turn.** `FirstActivationChooser`
      read `Sides[0]` and `Sides[1]` directly. The rule's own sentence -- "the side with fewer units"
      -- presumes two sides, so any other number is now answered the way a level count is: the rule
      is silent and the table settles it. The read path had been guarding this with its own copy of
      the check; the guard moved into the rule and the copy came out, so the two cannot drift.
- [x] **The rules-profile editor follows the table.** It seeded its draft from the snapshot on mount,
      and the snapshot has not arrived when it mounts, so it was seeded blank and stayed blank for
      the session -- a match that already had a profile showed zeros, and Save wrote those zeros over
      the real numbers. It now resyncs when the table's profile changes, compared by *content*: a
      snapshot lands on every mutation carrying a freshly parsed profile object, so watching for a
      new object would wipe a half-typed form every time the opponent moved a ship.
- [x] **Eleven more handlers cannot be double-submitted.** Create Fleet made two fleets, Duplicate
      two ships, Bring two copies of a library fleet, and Repair rolled the damage-control dice
      twice -- the one of those that nothing can undo. They now go through the `busy`/`run()` wrapper
      the rest of the screen already used, rather than a second mechanism beside it. The audit
      counted twelve; eleven mutating handlers were actually unguarded.

## Current Constraint

ForceSignal remains a tabletop helper first. Match state is session-based, with browser snapshot export for recovery and after-action review. Durable online hosting is intentionally deferred.
