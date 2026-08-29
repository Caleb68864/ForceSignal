# Hardening and polish pass — design

Date: 2026-08-28. Baseline: `e443435`, 1,188 .NET tests and 46 web tests green, `tsc` clean,
one analyzer warning (CA1720).

The brief was "harden it, polish it". This pass was designed autonomously from three read-only
audits (API, web, engineering quality), so where a finding needed a product decision the decision
is recorded here rather than made silently.

## What the audits found

The codebase is in good shape. Full Thrust authorization is thorough, tokens are 256-bit and
compared in constant time, nothing secret reaches the wire, restore is double-bounded, every
engine has a dice seam, there are no public setters, no TODOs, no empty catches, and the web
snapshot flow cannot regress the board. The gaps are at the edges:

- **Server availability.** `POST /api/matches` is unauthenticated and unrate-limited, and at the
  500-match ceiling it evicts the *oldest live game* from memory and SQLite. One StarGrunt request
  can roll two billion dice inside the global lock. Ground games are never capped or evicted.
- **Ground engines have no authorization.** Anyone with a game id can act for either side.
- **500s that should be 400s**: a null `order` body, a corrupt persisted row at startup (which
  stops the whole API), a SQLite busy failure after memory has already been mutated.
- **Web resilience.** No error boundary; localStorage is trusted; one unguarded `setItem`; damage
  and map buttons can double-fire; no request timeout; `strict` is off; no linter.
- **Tooling.** No CI, analyzer warnings are advisory, Dirtside has zero service or endpoint tests.

## Decisions

1. **Refuse, do not evict, at the match ceiling.** A live game is never destroyed to make room for
   a new one. Idle games (past retention) are still evicted.
2. **One shared game token for ground engines**, not per-side tokens. StarGrunt and Dirtside are
   hot-seat screens today (one device, both sides). The token is minted on create, returned once,
   and required in `X-Game-Token` on every route that reads or mutates a game. Per-side tokens
   are a product decision for when a second device exists.
3. **Room codes stay three words.** Readability aloud at a table was a deliberate choice. Instead:
   the seat-claim route gets the room-code rate limit it was missing, comparison becomes
   constant-time, and a leading/trailing space no longer fails a join.
4. **Explicit not-found exceptions replace message sniffing** for the 404 mapping. Exception
   messages still reach the client for the deliberate refusal types only; anything else is a
   generic 500 that is logged.
5. **Forwarded headers are opt-in** (`Proxy:TrustForwardedHeaders=true`) so rate limiting
   partitions per client behind a reverse proxy without trusting spoofed headers by default.
6. **`/ready` is left as is.** Changing it to 503 on in-memory persistence would break the
   dev-compose healthcheck; that is a deployment-policy call for the operator.
7. **`strict` TypeScript and ESLint are turned on** and the fallout is fixed, because the
   defining feature of the client is optional-chaining over snapshot fields the compiler currently
   does not check.
8. **No rule-fidelity work.** The remaining rule items (FT2 layer, StarGrunt fatigue drift,
   Dirtside close assault over HTTP) are game-design decisions, listed in the audit for later.

## Work packages

Three packages with disjoint file ownership so they can run in parallel.

### A. API and application hardening

Owns `src/ForceSignal.Api`, `src/ForceSignal.Application`, `src/ForceSignal.Contracts`,
`src/ForceSignal.Infrastructure`, `src/ForceSignal.Modules.*`, `tests/**`.

- Rate limit `POST /api/matches` and `.../seats/{id}/claim`; refuse instead of evict at ceiling.
- Clamp `StarGruntSettleDownedRequest.Downed` to living figures.
- Ground services: `MaxConcurrentGames`, idle retention, eviction, log trimming, collection and
  string caps mirroring the Full Thrust ceilings.
- Ground game token: `X-Game-Token` header, minted in `CreateGame`, returned in the created
  response as `Token`, checked with `FixedTimeEquals` on every game route.
- `NotFoundException` (Application) for the 404 path; catch-all 500 with generic message.
- Null-check `Order` on commit/preview/reveal; widen restore catches; guard `seats[0]`; log
  skipped rows.
- Truncate ship/class/weapon names on update paths and restored log strings.
- SQLite `busy_timeout`; map `SqliteException` to 503.
- Forwarded headers opt-in, before `UseRateLimiter`.
- Trim join code on join; constant-time room-code compare.
- `.dockerignore` excludes `*.db*`.
- Tests: `DirtsideGameServiceTests`, `DirtsidePersistenceTests`, `DirtsideEndpointTests`, plus
  a test for each hardening change above.
- Fix CA1720 so `TreatWarningsAsErrors` can be turned on.

### B. Web hardening and polish

Owns `src/ForceSignal.Web`.

- `ErrorBoundary` with "Export Last Device Backup".
- Validate session and fleet library out of localStorage; `writeStorage` for the session.
- `busy` guard on damage/quick-damage buttons and on every `PlayMap` mutation.
- Fighter-ops numbers commit on blur; clamp all numeric inputs on change.
- `api.ts`: guarded JSON parsing, 15s `AbortSignal.timeout`, human network-error message.
- `strict: true`; ESLint with `react-hooks`; `npm test` runs typecheck, lint, vitest.
- Copy-room-code button, one-line join hint, persisted display name.
- Ground views: `busyRef`, shared `newId`, game id persisted and re-read on mount, `aria-live`,
  send `X-Game-Token`.
- Accessibility: labelled damage steppers, hover readout off the live region, Enter targets a
  focused enemy marker.
- Cleanup: unused assets, misplaced comment, `plugin-react` to devDependencies, `<noscript>`,
  destroyed-ship filter, print race.
- Tests for `rules.ts` and `fleetIo.ts`; `vitest.config.ts`.

### C. Tooling

Owns repo root and `.github`.

- GitHub Actions running `scripts/verify.ps1` on push and PR.
- `TreatWarningsAsErrors` in `Directory.Build.props`.
- `Directory.Packages.props` central package versions.
- `.env.example` documents both persistence setting names.

## Verification

`scripts/verify.ps1` green; every new behaviour has a test; `npm test` green with lint and strict
typecheck; the three screens still play through in a browser.
