# Forge pass — design (round two)

Date: 2026-08-29. Baseline: `274df27`, 1,246 .NET tests and 101 web tests green, `verify.ps1`
green. Round one (`2026-08-28-hardening-pass-design.md`) closed the audit findings that were
defects. This round takes the items the audits flagged as *unrealised* - work already paid for that
a player cannot reach - plus what the browser click-through turned up.

## What this round is for

1. **Dirtside close assault and systems-down recovery are built, tested and unroutable.**
   `CloseAssault.cs` (488 lines) and `SystemsDownRecovery.cs` are pure resolvers with module tests,
   and `DirtsideGame` never calls either. The readiness warning admits it. StarGrunt went through
   exactly this step (`StarGruntGame.Assault.cs`, "close assault reaches the table"), so the shape
   is known: the game gains the commands, the service gains the methods, the API gains the routes,
   the screen gains the controls.
2. **The Dirtside screen sits on top of the Full Thrust workspace.** `main.tsx:1257` hides the
   workspace for StarGrunt only. Seen in the browser during round one.
3. **A refresh forgets where you were.** Game mode and battle view are component state; reload
   lands every device on Full Thrust / Ships, and a ground game is only reachable by clicking back
   into its engine.
4. **`Program.cs` is 990 lines** with the 34 Full Thrust routes inline. The ground engines already
   live in `Endpoints/`; the Full Thrust routes should too. Pure move, 83 API tests keep it honest.
5. **Dirtside has no fidelity audit.** Full Thrust and StarGrunt each got a numbered
   `*-fidelity-gaps.md`; the largest module in the repo has none.
6. **No component tests.** `vitest` runs in node; the round-one components (ErrorBoundary,
   RoomCode, CommittedNumber, the ground reopen flow) are verified by hand only.

## Decisions

- **Close assault in Dirtside follows the resolvers as written.** `CloseAssault.cs` already encodes
  launch, stand-or-withdraw, rounds, aftermath and follow-through with its own tests; the game
  layer sequences them, it does not reinterpret them. Where the resolver needs a number the player
  owns (threat, confidence, casualty shares) the request carries it, as everywhere else.
- **Systems-down recovery is an activation step**, attempted by the activating element, refused
  on the activation the marker was placed, retryable forever - which is what the resolver's
  remarks say and what its purity makes cheap.
- **Mode and view persist per device** in localStorage alongside the other keys; nothing goes in
  the URL, because the app has no router and adding one is not this round's job.
- **The fidelity doc is written from the code and the resolvers' own remarks**, in the same
  numbered "Gap N - ... - FIXED/OPEN" form as the other two, so a later scan does not re-find
  what is already recorded. It bundles no rules text.

## Work packages

### A. Dirtside reaches the table (owns API, Application, Contracts, Modules, tests)

- `DirtsideGame.Assault.cs`: launch (reaction test), defender stands or withdraws (confidence),
  rounds, aftermath, follow-through; `DirtsideGame.Turn.cs`: a recover-systems step.
- Service methods, request/response contracts, routes under
  `/api/dirtside/games/{gameId}/assaults/*` and `.../activations/current/recover-systems`, all
  behind the game token. Endpoint, service and game tests. Readiness warning updated to what is
  still missing (opportunity fire, area-defence interception, indirect fire).
- Extract Full Thrust routes to `Endpoints/MatchEndpoints.cs` (and helpers), leaving `Program.cs`
  to wiring and middleware. No behaviour change.

### B. Web (owns `src/ForceSignal.Web`)

- Hide the Full Thrust workspace under Dirtside as it is under StarGrunt.
- Persist `gameMode` and `activeView`.
- `jsdom` and component tests: ErrorBoundary (throws → fallback with export), RoomCode (copy and
  fallback), CommittedNumber (commit on blur/Enter, clamp, no-op on unchanged), DirtsideView
  reopen (stored handle → `readGame`; 404 → forgotten).
- After A lands: assault and recover-systems controls in `DirtsideView`, wired to the new routes.

### C. Docs (owns `docs/`)

- `docs/dirtside-fidelity-gaps.md`, numbered, in the form of its two siblings.
- `docs/ground-combat-plan.md` and `docs/roadmap.md` updated for what A delivers.

## Verification

`scripts/verify.ps1` green; the Dirtside screen plays an assault in a browser; a refresh returns
to the same engine and view.

## Outcome (2026-08-29)

Delivered in six commits after this document: web mode fix, persistence and component tests
(`ccc908c`); Dirtside assault, recovery, gaps 12-13 and the route extraction (`6842471`); the
Dirtside fidelity audit (`1bf1b59`); the assault screen (`eaa1b07`); and a validation gap the
end-to-end run found.

- .NET: 1,289 tests (from 1,246); web: 132 tests (from 101); `verify.ps1` green.
- Played end to end in a browser against a live API: a two-seat Full Thrust turn (setup, orders,
  reveal, movement, firing, advance) with the second seat driven over HTTP, an API restart on
  SQLite mid-match that the browser reconnected through with damage intact, a StarGrunt game
  reopened after a reload, and a complete Dirtside close assault from launch to follow-through.
- Found by the run: a rules-profile table row naming a face the die cannot show was accepted
  silently, so every beam rolled for nothing. `Validate` and the client's `gapsIn` now say so.
- Dirtside fidelity: 18 gaps recorded, 5 closed here (1, 2, 3, 12, 13), 10 open, 3 recorded.
