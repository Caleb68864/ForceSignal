# Pass 6 — standard (suite integrity + UI regression): CLEAN

Caught a process fault, not a code gap: the API I had running for browser checks held the test
DLLs, so `dotnet test` had been silently running **3 of 4 projects**. Stopped it and re-ran:
all four green (57 tests). UI restore + claim flow re-verified after the pass-3 edits.

# Pass 7 — standard (concurrency): CLEAN

12 simultaneous restores of one snapshot: all succeeded, 12 distinct match ids, 12 distinct room
codes, zero duplicates, every restored match internally coherent, and a claim on one match left
another's seats untouched. The design's atomic-under-`_gate` claim holds under contention.

# Pass 8 — adversarial

Modality: reachability audit + containerized stack + a forced race. Re-derived the API surface
from the design independently.

## Gaps found: 1

9. `MatchIdentityDto.HasUnclaimedSeats` was returned by the API and asserted in an API test, but
   the **live client never read it** — one reference in `main.tsx`, its own type declaration.
   Behind it sat a real race: if the last seat is claimed between the refused join and the
   room-code lookup, the user got a seat picker containing nothing but Cancel. The client now
   reads the flag and says every seat has been claimed.

Verified by forcing the interleaving (claiming the last seat while the by-code lookup was in
flight): the branch fires with a specific message and leaves the user on the join screen.
Also confirmed the ordinary case still behaves per the design — joining a fully claimed restored
room mints a new participant.

Container rebuild + `docker-smoke.ps1` green; restore and claim exercised against the container.

**Verdict:** 1 gap, fixed. clean_streak = 0.
