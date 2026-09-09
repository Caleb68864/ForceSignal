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

Still open from that audit, in its own ranking: ship position editable during
Reveal/Movement, locked orders silently overwritten, `docker-smoke.ps1` asserting
the wrong persistence mode, unbounded restored dice-roll arrays, unrated ground
game creation, `SequenceGuards` unguarded on the write path, the rules-profile
editor seeded blank, "finished plotting" being final, the hardcoded 12/24/36
range-band label, and the twelve web handlers with no double-submit guard.

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
- [ ] Opportunity fire and area-defence interception. The windows and their costs are built; the
      board never opens one, and "Sensors On" currently buys nothing (gaps 7-8).
- [ ] Infantry as stands with the firefight rules rather than as vehicles (gap 10).
- [ ] Defensive posture declared on the shot (gap 11).
- [ ] A configurable chit pot and a full validity-card editor on the screen (gaps 15-16).
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
- [ ] **The Dirtside chit pot is still baked into a static initialiser.** Verified and *not* changed
      this pass -- closing it needs a product decision rather than having one right answer. See
      "Chit pot: the decision to make" below.

## Chit pot: the decision to make

`ChitPotComposition`'s own doc says the composition "belongs where a player can look at it and
replace it rather than buried in a static initialiser they have to read the source to find", and
then puts 50/25/25 numericals and 6/6/3/3 specials in exactly a static initialiser
(`ChitPotComposition.cs:55`, `:128-141`). The doc also concedes the special counts are *a documented
guess* -- the sheet says only that there are fewer of the firer's Systems Down than the target's.

The module is not actually the problem: `Of`, `FromCounts`, `WithSpecials` and `ChitPot(composition)`
are all already there. The problem is that **nothing above the module exposes them**.
`DirtsideGameService.cs:120` hard-wires `new ChitPot()` into a process-wide singleton,
`CreateDirtsideGameRequest` is `(string Name)`, and the composition appears nowhere in
`DirtsideGameSerialization`'s saved record. Dirtside has no analogue of `RulesProfile` at all.

Three questions that are calls to make, not facts to look up:

1. **Does the default survive?** Full Thrust's stated policy is that there is no default and "a match
   carries a profile its players filled in, or it cannot be played". Applying that here is the
   consistent answer and is the one the guessed special counts argue for -- but it makes every
   existing Dirtside game unplayable and every new one require numbers up front.
2. **Per game or per server?** The pot is one process-wide singleton today. A per-game composition
   means building the pot per command, which collides with the `IChitPot` seam the tests inject
   through (`ScriptedChitPot`); that seam would have to become something like
   `Func<ChitPotComposition, IChitPot>`.
3. **What happens to saved games?** `GroundGameRecord.FormatVersion` is 1 and a mismatched row is
   **skipped, not migrated**. Adding a required field silently retires every stored Dirtside game
   unless `Restore` defaults it -- which is question 1 again, wearing a different hat.

Recommendation if a tiebreak is wanted: make the composition optional on the create request and
carry it per game, keep `Default` for one release with readiness warning that it is a guess, then
remove it. That gets the numbers in front of the player without retiring anyone's saved game.

## Closed 2026-09-08 (audit follow-up)

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
