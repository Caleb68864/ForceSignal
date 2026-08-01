# Converge Report — match-restore-design

**Outcome:** CONVERGED
**Passes used:** 11 / 12
**Reference:** `docs/plans/2026-07-31-match-restore-design.md`
**Base:** master

Scans ran in-session rather than in subagents, per this session's standing instruction not to
use the Agent tool unless asked. All other loop rules held: per-pass commits, suppression set
honoured, gaps re-scanned rather than assumed fixed, adversarial confirmation at `clean_streak == 2`.

**Suppression set** (from the design's Out of Scope, never re-flagged): server-side durable
autosave and self-recovery, reconstructing hidden orders, fleet transfer between participants.

## Per pass

| Pass | Mode | Gaps | Fixed | Verdict |
|------|------|------|-------|---------|
| 1 | standard (requirement sweep) | 5 | 5 | gaps |
| 2 | standard (error contract, execution) | 1 | 1 | gaps |
| 3 | standard (client flow, execution) | 1 | 1 | gaps |
| 4 | standard (full round trip) | 0 | — | CLEAN |
| 5 | standard (normalizer probe) | 1 | 1 | gaps |
| 6 | standard (suite integrity + UI regression) | 0 | — | CLEAN |
| 7 | standard (concurrency) | 0 | — | CLEAN |
| 8 | **adversarial** (reachability + container + forced race) | 1 | 1 | gaps |
| 9 | standard (two-device design flow) | 0 | — | CLEAN |
| 10 | standard (scope + suppression integrity) | 0 | — | CLEAN |
| 11 | **adversarial** (untested phase row, destroyed ships, playability) | 0 | — | CLEAN |

Passes 9, 10 and 11 clean consecutively, the third adversarial.

## Gaps closed (9)

1. **Design stated preserved fleet/ship ids**; the implementation reissues them because the
   by-ship-id and by-fleet-id endpoint lookups scan every match, so a restored copy sharing ids
   with a live match made both ambiguous. Design amended with a recorded deviation rather than
   reverting a correct fix.
2. **Ordnance markers had no restore coverage** despite being named in scope. Now asserted with
   remapped source/target and fighter carrier links.
3. **`IsReady` preservation and post-claim `IsConnected`** were unverified.
4. **Claiming a nonexistent seat or unknown match** had no coverage.
5. **"No partial restores" atomicity** was unverified; a rejected payload is now proven to leave
   the store untouched and the source match intact.
6. **Malformed JSON answered with a plain-text 400**, not problem+json — model binding rejected
   the body before the handler's catch. The old test used *valid* JSON so it never fired. The
   endpoint now parses the body itself; the test is a Theory over not-JSON, empty body, wrong
   schema and empty ships.
7. **The join screen had no surface for status messages at all**, so every restore failure was
   silent — including the pre-existing "no local snapshot backup" path. Status line added to the
   join screen and seat picker; unparseable files now name the file instead of surfacing a raw
   `JSON.parse` error.
8. **A weapon mount with a blank name was silently dropped**, disarming the ship. That filter is
   right for an empty form row but is silent data loss on a recovery path, contradicting
   "No partial restores". Restore keeps every mount, naming an unnamed one `Unnamed Mount`.
9. **`MatchIdentityDto.HasUnclaimedSeats` was never read by the live client** — returned by the
   API and asserted in a test only. The race behind it is real: if the last seat is claimed
   between the refused join and the room-code lookup, the user saw a picker with nothing to
   claim. Verified by forcing that interleaving.

## Process fault worth recording

Pass 6 found that a locally running API held the test DLLs, so `dotnet test` had been silently
running **three of four projects**. Two earlier "suite green" claims in this run were therefore
incomplete. Stopping the API and re-running showed all four green; later passes always stop it
first.

## Verification at exit

- `dotnet test ForceSignal.slnx` — 59 tests, all projects green (restore suite: 22).
- `scripts/verify.ps1` — restore, build 0 errors, tests, no vulnerable .NET packages, npm audit
  0 vulnerabilities, web build, compose config valid.
- `docker compose up -d --build` + `scripts/docker-smoke.ps1` — green; restore and claim
  exercised against the container.
- Two-device design flow driven in the browser: restore from file, claim, second device joins by
  room code, sees the taken seat disabled, claims the other, and commands only its own ship.

## Residual gaps

None.

## Frozen gaps

None.
