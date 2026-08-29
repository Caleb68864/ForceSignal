# ForceSignal Roadmap

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

## Current Constraint

ForceSignal remains a tabletop helper first. Match state is session-based, with browser snapshot export for recovery and after-action review. Durable online hosting is intentionally deferred.
