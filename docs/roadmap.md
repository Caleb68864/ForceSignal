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
- [ ] Ordnance attack resolution (gap 11), fighter group strength (gap 12), and point defence
      (gap 13). Salvo markers drift and expire but never attack, fighter groups roll a fixed dice
      count rather than one die per surviving fighter, and nothing shoots at fighters or missiles.
      These are interdependent, and all are Fleet Book systems rather than FTL, so beam-and-torpedo
      matches play without them.

## Movement Rule Fidelity

- [x] Free rotation at rest: a ship at velocity 0 that makes no other move rotates to any heading
      for free, ignoring the thrust cost and the half-thrust cap, so a thrust-0 station can come
      about. Getting under way still costs thrust as usual.
- [x] Split course changes across the move: a plotted turn pivots half at the start, rounded down,
      and the remainder at the mid-point, running half the velocity between the two pivots - which
      is what puts the ship where the rulebook's worked examples say it ends up. A plotted sequence
      of turns takes an equal share of the move each and splits the same way.

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
