# Pass 1 — standard

**Reference:** docs/plans/2026-07-31-match-restore-design.md
**Suppression set:** durable autosave, reconstructing hidden orders, fleet transfer.

## Scored

| Req | Score | Evidence |
|---|---|---|
| Restore returns full state | Met | InMemoryMatchService.RestoreMatch; round-trip tests |
| No durable persistence added | Met | no storage code touched |
| POST restore accepts wrapper or bare snapshot | Met | Program.cs restore endpoint; RestoreAcceptsABareSnapshot... |
| No participant token required | Met | endpoint takes JsonElement only |
| Returns snapshot + room code + seats | Met | MatchRestoredResponse |
| Rejects no ships / no participants / unknown profile | Met | RestoreMatch_RejectsUnusableSnapshots |
| Seat claim returns fresh session; taken seat refused | Met | ClaimedSeat_...AndCannotBeClaimedTwice |
| Seat list + join-by-code surface seats | Met | GetSeats, FindMatchByCode, JoinMatch guard |
| Atomic rebuild under _gate | **Partial (unverified)** | atomicity by construction, no test |
| Reuses existing normalizers | Met | RestoreMatch body |
| New match id and tokens | Met | round-trip test |
| Preserved fleet/ship/participant ids | **Misinterpreted** | code reissues fleet/ship ids by design correction |
| Room code reused when free else minted | Met | ReusedJoinCode assertions |
| IsConnected false, no token until claimed; IsReady preserved | **Partial (unverified)** | no test for IsReady |
| Phase mapping table | Met | three phase tests |
| Log carries over + restore entry | Met | round-trip test |
| Ordnance markers restored | **Partial (unverified)** | scope requires it; zero test coverage |
| Errors: taken or nonexistent seat | **Partial** | taken covered; nonexistent not |
| No partial restores | **Partial (unverified)** | see atomicity |

## Gaps found: 5

1. Design stated preserved fleet/ship ids; implementation deliberately reissues them. Spec amended
   with a recorded deviation rather than reverting a correct fix.
2. Ordnance markers had no restore coverage despite being in scope.
3. `IsReady` preservation and post-claim `IsConnected` were unverified.
4. Claiming a nonexistent seat (and an unknown match) had no coverage.
5. "No partial restores" atomicity was unverified.

## Fixes

Design doc amended; four tests added (ordnance + fighter links + readiness, unknown seat,
unknown match, rejected-payload atomicity). Application tests 26 -> 30.

**Verdict:** 5 gaps, fixed. clean_streak = 0.
