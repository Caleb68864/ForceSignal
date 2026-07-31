# ForceSignal Roadmap

ForceSignal is currently focused on being a session-based tabletop helper for in-person games. Online play and durable persistence remain future options after real table testing.

## Near-Term Tabletop Priorities

- [x] Fighter operations: launch/recover, group strength, endurance, max operating range, carrier assignment, and map range visualization.
- [x] Ordnance and ammo: missile/salvo markers, limited-use weapons, reload counters, and launched ordnance map objects.
- [x] Carrier operations: assigned/airborne/recovering group summaries and damage-adjusted launch/recovery reminders.
- [x] Table safety tools: end-of-turn checklist, unresolved fire reminders, fighter endurance reminders, quick undo, and log/snapshot export prompts.
- [x] Measurement helpers: range bands, weapon arcs, fighter range rings, ordnance markers, and movement path preview with final position/course.
- [x] Physical table workflow: print/export ship cards, compact tablet-friendly controls, public table display mode, and pass-and-play privacy.
- [x] Damage detail: threshold checks, damage-control reminders, critical system hit markers, crippled/dead-in-space states.

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

- [x] Browser/local snapshot recovery for tabletop use through local autosave and explicit snapshot export.
- [ ] Durable PostgreSQL persistence for long-running online matches. Deferred until real-world tabletop testing proves what needs durable storage.
- [x] Spectator/public table display with hidden information removed.
- [ ] Reconnect-safe online play once the tabletop workflows are proven. The client half is in
      place: automatic reconnect re-establishes the match notification group, resyncs the
      snapshot, and reports link state, and participant presence is tracked and logged on
      connect/disconnect. Still blocked by durable persistence — an API restart drops
      in-memory match state, so the session ends no matter how cleanly the client reconnects.

## Current Constraint

ForceSignal remains a tabletop helper first. Match state is session-based, with browser snapshot export for recovery and after-action review. Durable online hosting is intentionally deferred.
