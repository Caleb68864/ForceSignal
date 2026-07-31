# Converge Report — current-functionality-should

**Reference:** `docs/current-functionality-should.md` (49 requirements extracted, R1–R49)
**Base:** no prior git history; repo initialized during this run (`chore: baseline commit`)
**Pass ceiling:** 25 (user-set). Convergence rule: 3 consecutive clean passes, the third adversarial.

Scans were run in-session rather than dispatched to subagents, per the standing
instruction in this session not to use the Agent tool unless asked. Every other rule of
the loop applies: per-pass commits, regression guard, adversarial confirmation pass,
gaps re-scanned rather than assumed fixed.

## Verification commands used

| Modality | Command / method |
|---|---|
| Build + unit/integration tests | `dotnet build ForceSignal.slnx`, `dotnet test ForceSignal.slnx` (36 tests) |
| Web typecheck + bundle | `npm run build` (tsc + vite) |
| Repo battery | `scripts/verify.ps1` (adds vulnerable-package + npm audit + compose config) |
| Deployment | `docker compose up -d --build`, `scripts/docker-smoke.ps1` |
| Behavioural | Playwright against the running stack (dev server on 6300 and the nginx container on 6297) |

## Met% per pass

| Pass | Mode | Gaps found | Fixed | Verdict |
|------|------|-----------|-------|---------|
| 1 | standard | 5 | 5 | gaps |
| 2 | standard | 3 | 3 | gaps |
| 3 | standard | 3 | 3 | gaps |
| 4 | standard (runtime) | 2 | 2 | gaps |
| 5 | standard (runtime) | 1 | 1 | gaps |
| 6 | standard (responsive) | 2 | 2 | gaps |
| 7 | standard (deployment) | 1 | 1 | gaps |
| 8 | standard (touch) | 1 | 1 | gaps |
| 9 | standard (public display) | 1 | 1 | gaps |
| 10 | standard (secrecy probe) | 0 | — | CLEAN |
| 11 | standard (profile writes) | 0 | — | CLEAN |
| 12 | **adversarial** | 3 | 3 | gaps |
| 13 | standard (regression) | 0 | — | CLEAN |
| 14 | standard (recovery exports) | 0 | — | CLEAN |
| 15 | **adversarial** | 1 | 1 | gaps |
| 16 | standard (container bundle) | 1 | 1 | gaps |
| 17 | standard (full battery) | 0 | — | CLEAN |
| 18 | standard (map controls, measurement) | 0 | — | CLEAN |
| 19 | **adversarial** (API fuzzing) | 1 | 1 | gaps |
| 20 | standard (deployed re-verify) | 1 | 1 | gaps |
| 21 | standard (create/update symmetry audit) | 0 | — | CLEAN |
| 22 | standard (remaining checklist warnings) | 0 | — | CLEAN |
| 23 | **adversarial** (own change set) | 0 | — | CLEAN |

**Outcome: CONVERGED** — passes 21, 22, 23 clean in a row, the third adversarial.

Pass 23 reviewed the accumulated 506-insertion diff hostilely, re-ran the mechanical
battery, and finished with a two-fleet turn against the deployed container (movement,
armor-then-hull damage, complete firing record, per-turn firing reset). It found no
requirement gap. It did find two `bash.exe.stackdump` files that my own shell tooling had
left in the tree and that the baseline commit swept in; those are removed and ignored.
That is housekeeping of my leftovers rather than a code-versus-spec gap, which is why the
pass is scored clean — flagging it here so you can judge that call yourself.

## Gaps closed (35 across 23 passes)

### Turn flow and rules
1. Drive damage did not reduce usable thrust; a ship with wrecked drives could plot full
   thrust. Rules notes confirm thrust is the sum of *functioning* drives.
2. Orders could be locked in any phase; committing during Firing flipped the phase back
   to OrdersLocked mid-combat.
3. Orders could be revealed before every live ship had locked, exposing hidden orders early.
4. Destroyed ships blocked the lock/reveal gates — wrecks had to be given orders for the
   turn to advance, which the checklist simultaneously warned about.
5. A reveal that failed hash verification could not be re-locked, deadlocking the turn.
6. Orders for a ship destroyed between reveal and movement were still applied.
7. Destroyed ships could fire, and could be fired at.
8. Readiness required two participants, so a single-device local match could never start.
9. Order lock / reveal / verification-failure were never written to the match log.

### Firing
10. Arc select could show a legal arc while sending an illegal one (server rejection).
11. A destroyed stored target stayed in the draft while the UI showed a different one.
12. Editing range silently overwrote a hand-picked arc.
13. Firing defaulted to the first ship in snapshot order — usually a friendly hull, so one
    click on Fire hit your own carrier. Targets are now hostile-first by range.

### Play map and interaction
14. Ordnance markers only rendered inside the Fire tool, so they were invisible on the
    public display and in the quiet Status view.
15. No pinch-to-zoom, and a second touch pointer corrupted panning.
16. Long press on a marker did nothing — touch tables had no equivalent of the right-click
    context action; and targeting jumped the selection to the opposing hull, hiding the
    solution it had just set.
17. `inspectorMode` persisted across selection changes, showing panels the new selection
    was not entitled to (carrier ops on an opponent hull).
18. Editing a non-selected ship exposed its full control set.

### Public display, print, responsive
19. Public display and print never hid the pre-turn checklist — the CSS targeted
    `.pre-turn-checklist` but the element is `preturn-checklist`.
20. Public display left the Ships tab reachable with full command controls.
21. Public display hid the panel that owned its own toggle, so the mode could only be left
    by reloading.
22. Touch sizing never applied: class-scoped desktop sizes outrank the bare element rule
    inside the breakpoint, and tablets wider than 760px were not covered at all.

### Export / import / recovery
23. Fleet import aborted with "Home carrier was not found" for any exported fighter with a
    carrier assignment, because ship ids are per-match. Export now carries the carrier
    name, import creates carriers first and remaps, and the server treats an unknown
    carrier id as unassigned.
24. Only the first owned fleet was reachable for add-ship and export, so an imported second
    fleet was orphaned. After-action CSV contained only log rows. Blob URLs were revoked in
    the same tick as the click.

### Realtime and hardening
25. No group re-join after automatic reconnect — clients silently stopped receiving updates.
26. A rejoin failure aborted the resync, leaving a stale table for a match that no longer existed.
27. `MatchHub.JoinMatchGroup` accepted a participant token and ignored it.
28. `ParticipantDto.IsConnected` was hardcoded true forever; presence is now tracked.
29. No indication of a dropped realtime link.
30. Teardown called `connection.stop()` while `start()` was in flight, which throws and can
    leave a live connection running.
31. `Ready` and `Advance Turn` had no rejection handler (silent unhandled rejections).
32. `/ready` reported no warnings in Development. `.env.example` advertised PostgreSQL
    variables outside the current scope. `CreateShip` did not clamp thrust.
33. Checklist and count strings had singular/plural and subject-verb agreement defects.
34. Create paths accepted blank names, producing nameless ships, fleets and participants,
    while the update paths already guarded against exactly that; blank class names likewise
    survived create but were collapsed to null on update.
35. Housekeeping: `bash.exe.stackdump` files left by shell tooling were committed by the
    baseline commit; removed and ignored, along with `.playwright-cli/`, `.playwright-mcp/`
    and `output/` scratch directories.

## Accepted deviations (not gaps)

- `WeaponMountState.ReloadTurns`, `OrdnanceMarker.AttackDice/MaxRange` are tracked and
  exported for tabletop bookkeeping; the functionality list asks for no reload or ordnance
  resolution mechanic.
- POST responses bypass `normalizeMatchSnapshot`. The API always sends complete, normalized
  arrays, and any drift self-heals on the next snapshot load.
- `setSnapshot` is last-write-wins with no version comparison. A stale response is corrected
  by the notification that the same write triggers.
- No ship deletion. The list covers create/add/edit and destroyed state only.

## Rules-fidelity observations (outside this spec's scope — your call)

Cross-checked against your Full Thrust notes, which validated the thrust model
(functioning drives only; turn cap = half thrust rounded up in FTL; 1 TP = 1 mu or one
course point; no reverse). Two documented rules details are deliberately not implemented,
and the functionality list does not ask for them:

- A ship at velocity 0 may rotate freely on the spot, spending no thrust. Here a turn always
  costs thrust, so a thrust-0 station can never change facing.
- A plotted course change is pivot-half / move-half / pivot-rest / move-rest. The resolver
  splits movement into equal segments and pivots between them, which is a coarser
  approximation of the same shape.

No rules text, tables, or fleet data were copied into the repo, per the content policy.
