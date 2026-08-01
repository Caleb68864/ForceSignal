# ForceSignal Roadmap

ForceSignal is currently focused on being a session-based tabletop helper for in-person games. Online play and durable persistence remain future options after real table testing.

## Near-Term Tabletop Priorities

- [x] Fighter operations: launch/recover, group strength, endurance, max operating range, carrier assignment, and map range visualization.
- [x] Ordnance and ammo: missile/salvo markers, limited-use weapons, reload counters, and launched ordnance map objects.
- [x] Carrier operations: assigned/airborne/recovering group summaries and damage-adjusted launch/recovery reminders.
- [x] Table safety tools: end-of-turn checklist, unresolved fire reminders, fighter endurance reminders, quick undo, and log/snapshot export prompts.
- [x] Measurement helpers: range bands, weapon arcs, fighter range rings, ordnance markers, and movement path preview with final position/course.
- [x] Physical table workflow: print/export ship cards, compact tablet-friendly controls, public table display mode, and pass-and-play privacy.
- [x] Damage detail: damage-control counters, critical system hit markers, and crippled/dead-in-space
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
- [ ] Drift for unordered ships (gap 7). The rules let a ship with no written order continue on the
      same course and velocity; the app cannot advance the turn until every live ship is ordered.
- [ ] Firing initiative and alternation (gap 6). Roll off, winner fires one ship completely, then
      alternate - with damage applied immediately. The app lets anyone fire anything at any time.
      The per-ship volley now exists, so what is left is the initiative roll and enforcing turns.
- [ ] Pulse torpedoes (gap 10), ordnance attack resolution (gap 11), and point defence (gap 13).
      FTL's second weapon is missing, ordnance markers drift but never attack, and nothing shoots
      at fighters or missiles.

## Movement Rule Fidelity

The current profile is a deliberately simplified cinematic model. These two known
divergences were identified while converging the app against `current-functionality-should.md`
and are deferred, not forgotten.

- [ ] Free rotation at rest: a stationary ship should be able to turn to any facing without
      spending thrust. Today every turn costs thrust and is capped at half thrust, so a
      thrust-0 station or a stopped hull can never change facing at all.
- [ ] Split course changes across the move: a plotted turn should pivot half at the start and
      the remainder at the mid-point, moving half the velocity between the two pivots. The
      resolver currently divides the move into equal segments and pivots between them, which
      approximates the same shape but puts ships on a slightly different path and changes
      where the movement trail bends.

## Later Production Options

- [x] Browser/local snapshot recovery for tabletop use through local autosave, explicit snapshot export, and full-state restore with seat claiming.
- [ ] Durable PostgreSQL persistence for long-running online matches. Deferred until real-world
      tabletop testing proves what needs durable storage. File-based restore now covers recovery
      from an API restart, which lowers the urgency without removing the need: recovery is still
      manual and depends on someone having saved a snapshot.
- [x] Spectator/public table display with hidden information removed.
- [ ] Reconnect-safe online play once the tabletop workflows are proven. The client half is in
      place: automatic reconnect re-establishes the match notification group, resyncs the
      snapshot, and reports link state, and participant presence is tracked and logged on
      connect/disconnect. Still blocked by durable persistence — an API restart drops
      in-memory match state, so the session ends no matter how cleanly the client reconnects.

## Current Constraint

ForceSignal remains a tabletop helper first. Match state is session-based, with browser snapshot export for recovery and after-action review. Durable online hosting is intentionally deferred.
