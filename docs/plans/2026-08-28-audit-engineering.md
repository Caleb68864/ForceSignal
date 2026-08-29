# ForceSignal — Engineering Quality Audit (domain / tests / tooling)

Repo root: `C:\Users\CalebBennett\Documents\GitHub\ForceSignal`
Branch: `master` (clean at audit time, HEAD `e443435`)
Scope: read-only audit of domain + module code, tests, repo tooling, docs, dependencies.
Excludes: `src/ForceSignal.Web` (covered by the web audit).

---

## 1. Repo tooling

| Item | Status |
|---|---|
| CI (`.github/workflows`) | **MISSING** — no `.github` directory at all |
| `.editorconfig` | Present, thorough (analyzer severities tuned per-category) |
| `.gitignore` | Present and correct — covers `bin/ obj/ node_modules/ dist/ .vs/ .vscode/ *.user *.suo *.log .env coverage/ TestResults/ .playwright-cli/ output/ .playwright-mcp/ bash.exe.stackdump .claude/worktrees/` |
| Stray tracked files | **None.** 311 tracked files; `git ls-files` matched nothing for `dist/`, `output/`, `stackdump`, `bin/`, `obj/`, `node_modules`, `.env` |
| `.env.example` | Present, consistent with README:82-85 and `docker-compose.yml` |
| `.dockerignore` | Present |
| `Directory.Build.props` | Present — `AnalysisLevel=latest`, `AnalysisMode=Recommended`, `EnforceCodeStyleInBuild=true`, `GenerateDocumentationFile=true`, `NoWarn=CS1591` |

`bash.exe.stackdump` and `output/` **do exist on disk** but are correctly gitignored and untracked. Stale `obj/Debug/net8.0/` folders exist untracked (leftovers from the net8 retarget spike). Nothing needs removal from the index.

### T1 (HIGH) — No CI
`scripts/verify.ps1` is already a complete gate: `dotnet restore` → `dotnet build` → `dotnet test` → npm build → npm audit → vulnerable-package scan → optional `docker compose` smoke (`-IncludeDocker`). README:114 tells a human to run it. **Nothing runs it automatically on push or PR.**

*Fix:* one GH Actions job on `windows-latest` invoking `.\scripts\verify.ps1`.

### T2 (MEDIUM) — Analyzer warnings are advisory only
`Directory.Build.props:1-8` enables `AnalysisMode=Recommended` and `EnforceCodeStyleInBuild=true` but sets **no** `TreatWarningsAsErrors` / `WarningsAsErrors` anywhere in the repo (grep across all `.props` and `.csproj` returns zero). Combined with T1, the carefully tuned `.editorconfig` severities (CA1822, CA1852 promoted to `warning`; Reliability/Security/Performance categories to `warning`) are enforced by nothing.

*Fix:* add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to `Directory.Build.props`, or pass `-warnaserror` in the CI build step.

### T3 (LOW) — Two names for one persistence setting
`src/ForceSignal.Api/Program.cs:824`:
```csharp
configuration["Persistence:MatchDatabasePath"] ?? configuration["FORCESIGNAL_MATCH_DB"]
```
`docker-compose.yml` sets `Persistence__MatchDatabasePath`; `.env.example` documents only `FORCESIGNAL_MATCH_DB` (commented out). Both work, but the dual path is undocumented and a reader of `.env.example` alone would not know compose uses the other name.

*Fix:* document both in `.env.example`, or drop the `FORCESIGNAL_MATCH_DB` fallback.

### Note — the only env var the web reads
`VITE_API_BASE_URL` at `src/ForceSignal.Web/src/lib/api.ts:6` is the sole `import.meta.env` consumer. `.env.example` matches. No drift.

---

## 2. Tests

### Counts per project

| Project | `[Fact]` | `[Theory]` | `[InlineData]` | src LOC | test LOC | ratio |
|---|---|---|---|---|---|---|
| Api.Tests | 38 | 4 | 24 | 1448 | 1396 | 0.96 |
| Application.Tests | 234 | 0 | 0 | 5708 | 6121 | 1.07 |
| Domain.Tests | 20 | 10 | 70 | 1040 | 475 | 0.46 |
| Infrastructure.Tests | 7 | 1 | 4 | 168 | 248 | 1.48 |
| Modules.Dirtside.Tests | 165 | 24 | 82 | 4845 | 3284 | 0.68 |
| Modules.StarGrunt.Tests | 192 | 18 | 79 | 3561 | 3118 | 0.88 |
| Modules.FullThrust.Tests | 69 | 19 | 92 | 963 | 1401 | 1.45 |
| Modules.GroundCombat.Tests | 68 | 10 | 44 | 2399 | 1383 | 0.58 |

**879 declared test methods; ~989 executed cases** (the 989 figure is corroborated by `docs/plans/2026-08-10-net8-retarget-spike.md`, which ran the whole suite on the net8 runtime).

`src` totals: 21,303 LOC across 106 first-party `.cs` files. `tests` totals: 17,426 LOC.

### What IS well covered

- **Integration tests exist and are real.** `WebApplicationFactory` is used in 6 of the 8 `Api.Tests` files: `ApiDocumentationAndReadinessTests.cs`, `ApiHardeningTests.cs`, `FeatureFlagTests.cs`, `MatchRestoreEndpointTests.cs`, `OrderPreviewEndpointTests.cs`, `RulesProfileEndpointTests.cs`, `StarGruntEndpointTests.cs`.
- **Persistence round-trip is covered.** `tests/ForceSignal.Infrastructure.Tests/SqliteMatchStoreTests.cs` — 8 tests including `AWholeMatchSurvivesARealRestartThroughARealFile` (line 73, real file, real restart), `TwoGamesInOneFileDoNotSeeEachOthersSaves` (line 115), `ARowThatIsNotOneOfOursIsSkippedRatherThanFatal` (line 95), and a `[Theory]` refusing non-identifier table names (line 137 — SQL injection guard).
- **Restore is covered heavily.** `tests/ForceSignal.Application.Tests/InMemoryMatchServiceRestoreTests.cs` is 564 lines (largest test file in the repo), plus `MatchRestoreEndpointTests.cs` at the HTTP layer and `MatchPersistenceTests.cs` / `StarGruntPersistenceTests.cs`.
- **Application.Tests is broad:** 28 files covering arcs, carrier ops, combat, derived ship fields, drift, fighter movement, fire control, firing solutions, hardening, initiative, needle beams, ordnance, points, preview, range checks, repair, restore, rules profile, threshold, torpedo, turn flow — plus three scripted test doubles (`ScriptedDice.cs`, `ScriptedFigureAllocator.cs`, `ScriptedQualityDice.cs`).

### TEST-1 (HIGH) — `DirtsideGameService` has ZERO tests
`src/ForceSignal.Application/Ground/DirtsideGameService.cs` is **438 lines** exposing a 14-method `IDirtsideGameService` interface. The string `DirtsideGameService` appears in **no test file anywhere in the repo**.

Its StarGrunt sibling (`StarGruntGameService.cs`, 519 lines) has two: `tests/ForceSignal.Application.Tests/StarGruntGameServiceTests.cs` and `StarGruntPersistenceTests.cs`.

The seams to test it are already built and documented:
- `DirtsideGameService.cs:79-86` — `IQualityDiceRoller? rollDie = null` and `IMatchStore? store = null` constructor params.
- Scripted doubles already exist in the same test project (`ScriptedQualityDice.cs`).

*Fix:* mirror `StarGruntGameServiceTests.cs` and `StarGruntPersistenceTests.cs` for Dirtside. Low effort, high value — this is the single largest coverage hole in the codebase.

### TEST-2 (HIGH) — 12 of 13 Dirtside HTTP routes are untested
The API exposes **70 routes** total. Route inventory by area:

| Area | Routes | Exercised in `Api.Tests` |
|---|---|---|
| Full Thrust match/fleet/ship/ordnance | 34 | ~20 |
| StarGrunt | 21 | ~8 |
| Dirtside | 13 | **1** (`/api/dirtside/status`, and only via `FeatureFlagTests.cs:84` asserting a 404 when the flag is off) |
| Infra (`/health`, `/ready`, `/api/features`, openapi, scalar) | 5 | 5 |

Every Dirtside **mutation** route is untested at the HTTP layer:
```
POST /api/dirtside/games
POST /api/dirtside/games/{gameId}/units
POST /api/dirtside/games/{gameId}/turns
POST /api/dirtside/games/{gameId}/turns/current/end
POST /api/dirtside/games/{gameId}/turns/current/first-activator
POST /api/dirtside/games/{gameId}/turns/current/pass
POST /api/dirtside/games/{gameId}/activations
POST /api/dirtside/games/{gameId}/activations/current/end
POST /api/dirtside/games/{gameId}/activations/current/fire
POST /api/dirtside/games/{gameId}/activations/current/moves
POST /api/dirtside/games/{gameId}/activations/current/sensors
POST /api/dirtside/games/{gameId}/activations/current/stand-down
GET  /api/dirtside/games/{gameId}
```

*Fix:* a `DirtsideEndpointTests.cs` mirroring `StarGruntEndpointTests.cs`.

### TEST-3 (MEDIUM) — Silent save-drop on restore is untested and unobservable
`src/ForceSignal.Application/Ground/DirtsideGameService.cs:99-118`:
```csharp
foreach (var saved in store?.LoadAll() ?? [])
{
    try { games[saved.MatchId] = new Held(DirtsideGameSerialization.Restore(saved.State), 1); }
    catch (ArgumentException)
    {
        // Not a Dirtside game, or not one this version understands.
    }
}
```
Same shape at `src/ForceSignal.Application/Ground/StarGruntGameService.cs:139`.

Three game kinds share one SQLite table, so most skips are legitimately "somebody else's game". But a **genuinely corrupt or version-drifted Dirtside save takes the identical path** — dropped with no log, no counter, no readiness signal. A player's game silently disappears on restart and nothing anywhere says why. The comment acknowledges the ambiguity but the code cannot distinguish the two cases.

*Fix:* tag persisted rows with a game-kind discriminator so a failed parse of a row that claims to be Dirtside is a real error; or at minimum log at Information with the match id and skipped count, and surface the count on `/ready`.

### Least-tested module, adjusted for size
- **By raw ratio:** `Modules.GroundCombat` is thinnest (2399 src / 1383 test = 0.58), then `Modules.Dirtside` (4845 / 3284 = 0.68). `Modules.FullThrust` looks best (1.45) only because most Full Thrust logic lives in `InMemoryMatchService` in the Application layer, not the module — its true coverage is Application.Tests' 234 tests.
- **The real answer is Dirtside**, once you count all three layers. StarGrunt has module tests + service tests + persistence tests + endpoint tests. Dirtside has module tests only — no service tests, no persistence tests, no endpoint tests. It is simultaneously the **largest module in the repo** (4,845 LOC) and the one with the least coverage above the engine.

---

## 3. Domain / module code smells

The codebase is in better shape than a typical audit finds. Confirmed clean:

- **Zero public settable properties across all of `src/`.** The grep for `public T X { get; set; }` returns **0 matches**. Domain and contracts are records and readonly record structs throughout. Nothing to make private.
- **Zero `TODO` / `FIXME` / `HACK` / `XXX`** in first-party `.cs` or `.ts`. Every hit is inside `src/ForceSignal.Web/node_modules`.
- **Zero empty `catch { }`.** Every catch has a body and a comment explaining it.
- **Randomness is fully seamed.** Every engine accepts an injectable die and falls back to `Random.Shared` only at the constructor tail:
  - `src/ForceSignal.Application/Matches/InMemoryMatchService.cs:123,136` — `Func<int>? rollDie = null`, `?? (() => Random.Shared.Next(1, 7))`, threaded into all 8 rules objects (lines 127-134).
  - `src/ForceSignal.Modules.FullThrust/Combat/FullThrustLightFiringRules.cs:16-18` — same pattern; repeated in `FullThrustLightPulseTorpedoRules`, `FullThrustNeedleBeamRules`, `FullThrustDamageControlRules`, `FullThrustLightThresholdRules`, `FullThrustCarrierOperationRules`, `FullThrustPointDefenseRules`, `FullThrustSalvoMissileRules`.
  - `src/ForceSignal.Modules.Dirtside/Chits/ChitPot.cs:42-45` — `Func<int, int>? nextIndex = null`, `?? Random.Shared.Next`.
  - `src/ForceSignal.Modules.GroundCombat/Dice/QualityDiceRoller.cs` — `IQualityDiceRoller` is an interface; Dirtside combat takes it as a parameter at 9 call sites (`CloseAssault.cs:273,313,380,432`, `DirectFire.cs:154,232,299`, `HitResolution.cs:180`, `InfantryCombat.cs:210,387`, `SystemsDownRecovery.cs:80`).
  - `src/ForceSignal.Modules.StarGrunt/Game/FigureAllocation.cs` — allocation is injectable too, so a test can put a round on a specific figure.
  - **No non-deterministic `Random` is reachable without an injection point.** Tests use fixed seeds (`ChitPotTests.cs:24,37,52,69`).
- **`decimal` for geometry, `double` only for trig.** 53 `decimal` vs 23 `double`, **zero `float`**. Positions are `decimal` and cast to `double` only inside `Math.Pow` / `Math.Atan2`:
  - `InMemoryMatchService.Combat.cs:417-421, 520-521, 549-550`
  - `InMemoryMatchService.cs:700-701, 998-999, 1093-1094, 1215`
  - Rationale documented at `src/ForceSignal.Domain/Rules/CombatContracts.cs:74` ("Positions are measured in decimal and converted to double to take an arc tangent"), with a `BoundaryTolerance = 1e-9` epsilon at line 81 and NaN/Infinity guards at line 93. This is the correct choice, correctly documented.
- **Engines use result types, not exceptions.** `SequenceCheck` (`src/ForceSignal.Modules.GroundCombat/Sequence/SequenceCheck.cs:16`) and `GameOutcome<T>` (`GameOutcome.cs:27`) carry refusals. The `Can*`/verb pairing is deliberate and documented at `SequenceCheck.cs:8-14`.
- **No duplicated magic-number clusters.** Constants are named and commented — e.g. `src/ForceSignal.Modules.Dirtside/Combat/CloseAssault.cs:185` `HeavyCasualtyShare = 0.5`, `FullThrustLightThresholdRules.RowCount` (which carries its own rules rationale).

### D1 (HIGH) — HTTP status decided by string-matching an exception message
`src/ForceSignal.Api/Program.cs:176-180`:
```csharp
var (status, title) = ex switch
{
    UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Action not allowed"),
    InvalidOperationException when ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
        => (StatusCodes.Status404NotFound, "Request could not be completed"),
    _ => (StatusCodes.Status400BadRequest, "Request could not be completed"),
};
await WriteProblem(context, status, title, ex.Message);
```

Two problems in five lines:

1. **Fragile routing.** There are 112 `throw new InvalidOperationException` sites in `src/`. Rewording any of them silently flips a 404 to a 400, and no test pins the phrasing. A message that happens to contain "not found" for an unrelated reason gets a wrong 404.
2. **Information leak.** `ex.Message` goes straight to the client for *every* caught type. A genuine invariant violation deep in `InMemoryMatchService` — a real bug, not a user error — is returned as a 400 carrying internal detail.

*Fix:* introduce a dedicated `NotFoundException` (or return a `MatchError` result type from the service boundary) instead of message sniffing, and only surface `ex.Message` for exception types the domain raises deliberately for the caller.

### D2 (MEDIUM) — Exceptions are the control-flow mechanism at the Application boundary
Throw counts in `src/`: 112 `InvalidOperationException`, 20 `ArgumentException`, 12 `UnauthorizedAccessException`, 5 `NotSupportedException`, 4 `ArgumentOutOfRangeException`, 3 `JsonException`, 1 `HubException` — all funnelled through the single middleware at `Program.cs:152-183`.

The engines got result types (`GameOutcome<T>`, `SequenceCheck`); the Application services that wrap them did not. This is the structural reason D1 exists. Not urgent on its own, but it means every refusal a player can legitimately trigger is indistinguishable, at the boundary, from a programming error.

*Note:* the middleware correctly handles the `Response.HasStarted` case (`Program.cs:167-171`) by rethrowing rather than double-writing — that part is well done and commented.

### D3 (LOW) — `InMemoryMatchService` is 4,075 lines across 6 partials
```
InMemoryMatchService.cs             1833
InMemoryMatchService.Combat.cs       757
InMemoryMatchService.Restore.cs      466
InMemoryMatchService.Firing.cs       370
InMemoryMatchService.Persistence.cs  335
InMemoryMatchService.State.cs        314
```
It holds Full Thrust's entire rules orchestration, while StarGrunt and Dirtside were built as independent modules over immutable game values. The partials keep it navigable so this is a note rather than a defect — but the asymmetry has a consequence: Full Thrust is the **one ruleset that cannot travel to a non-web client** the way `docs/plans/2026-08-10-net8-retarget-spike.md` argues the others can. That spike's whole premise ("the game is an immutable value rather than a mutable service") holds for the modules and not for Full Thrust.

### D4 (LOW) — `Program.cs` is 987 lines with only one endpoint group extracted
`src/ForceSignal.Api/Endpoints/GroundCombatEndpoints.cs` (403 lines) holds the StarGrunt and Dirtside routes. The 34 Full Thrust match/fleet/ship/ordnance routes are all inline in `Program.cs` alongside CORS config, feature flags, persistence wiring, the exception middleware, and readiness-warning composition.

---

## 4. Docs — remaining game-rule work

**Neither fidelity doc uses `- [ ]` checkboxes.** Status lives in the heading: `## Gap N — … — FIXED` / `— RECORDED` / `— MOSTLY FIXED`. A checkbox grep returns zero and is misleading.

### `docs/rules-fidelity-gaps.md` (Full Thrust) — 23 gaps, effectively all closed

Gaps 1-21 and 23: **FIXED** (dated 2026-08-01, 08-02, 08-08).
Gap 22: **RECORDED** — a deliberate judgement, not debt.

Both scans are declared closed. What remains:

1. **"Still open — threshold rows by class"** (line 739). Not actionable as written. Four rows for every hull is the *Fleet Book* rule and is what both shipped layers use; rows-by-class band (escort 2, cruiser 3, capital 4) is the *FT2* rule, and ForceSignal has no FT2 profile to hang it on. Putting it behind the Fleet Book switch would make the app wrong under both layers; putting it on `LightCinematic` would silently change the layer people actually play. The seam is already built — `RulesProfile.ThresholdRows`, `FullThrustLightThresholdRules.RowCountFor`, `ShipClassBands.FromIconKey`. **Blocked on a product decision** to build a third rules layer, which also changes screens, needle reach, fighter moves and ship points.
   - The doc is honest about a real limitation in the seam: ship class is free text, so the only normalized size signal is the map icon. An unrecognised class falls back to cruiser; "Escort Cruiser" matches "escort" first and is wrong; "Battlecruiser" matches nothing and lands on cruiser; every carrier shares one icon; a station is not on the ladder. Any FT2 profile should let the player set the band outright.
2. **"Play a full game"** — the doc's own closing line: everything is verified by tests and by driving the API and browser, but **no two-device match has been played end to end since the turn structure changed.**
3. **"Deliberately not built"** list (line 658) — recorded so a later scan does not re-find them as oversights: fighter-to-fighter combat and dogfights, pilot quality, fighter screens, group morale; area defence fire control, reflex/cloaking fields, ECM and active/passive sensors; nova cannon, wave guns, submunitions, heavy More Thrust missiles, mines; the optional vector movement system and its rolling/thruster rules; FTL drives, boarding, ortillery, terrain, atmospheric ops, ground-combat interface, campaign/tournament rules; Fleet Book reroll damage. **None of these blocks a match.**

### `docs/stargrunt-fidelity-gaps.md` — 11 gaps, one partial

Gaps 1-7 and 9-11: **FIXED** (all 2026-08-10).

**Gap 8 — Fatigue: "MOSTLY FIXED"** (line 153, severity medium). Fatigue is now a scenario property of the unit, set when it is added, carried in force files, and it both sets opening confidence and caps rally recovery. **What is still missing is anything that *changes* fatigue during a game — it is set once and never moves.** This is the only open rule item in StarGrunt.

Also flagged in the doc's own text: an open "assault-as-state question" (suggested-order item 6, line 261ff) — close assault is built and tested, but whether an assault should be modelled as persistent state was left undecided.

### **No `docs/dirtside-fidelity-gaps.md` exists** — a real documentation gap

Full Thrust and StarGrunt each received a numbered rules-fidelity audit. **Dirtside — the largest module in the repo at 4,845 LOC — has never had one.**

`src/ForceSignal.Api/Program.cs:851-856` already admits the shortfall in a readiness warning:

> "Dirtside ground combat is enabled. Direct fire and the activation sequence are playable; **opportunity fire, area-defence interception, close assault and indirect fire are not yet reachable from this API.**"

Worth noting: `src/ForceSignal.Modules.Dirtside/Combat/CloseAssault.cs` is **488 lines, built and tested in the module**, and `SystemsDownRecovery.cs` likewise — they are simply **unroutable over HTTP**. That is engine work already paid for and not yet reachable by a player.

### `docs/roadmap.md` — 38 checked, 2 unchecked

- **L123** — Threshold rows by class. Same item as above; the roadmap explicitly says it "is *not* a Fleet Book difference and is deliberately not wired."
- **L154** — Reconnect-safe online play. Both halves are built (client reconnects, rejoins the notification group and resyncs; server keeps the match across a restart). What remains is real-world testing, not missing code.

---

## 5. `docs/converge/*` — what it is

Audit trails from the `/forge-converge` loop: iterative scan-and-fix passes that drive code back to a written spec until it converges (convergence rule: 3 consecutive clean passes, the third adversarial). Two runs, both dated 2026-07-31, 423 lines total across 8 files.

**`docs/converge/2026-07-31-current-functionality-should/converge-report.md`** (155 lines)
- Reference: `docs/current-functionality-should.md`, 49 extracted requirements R1–R49.
- Base: no prior git history — the repo was initialized during this run (`chore: baseline commit`).
- Pass ceiling 25 (user-set); 15+ passes logged in a per-pass table (pass / mode / gaps found / fixed / verdict), mixing static requirement sweeps with **runtime, responsive, deployment, touch, public-display and secrecy-probe** modes plus adversarial confirmation passes at 12 and 15.
- Records the verification command per modality: `dotnet build ForceSignal.slnx`, `dotnet test ForceSignal.slnx`, `npm run build`, `scripts/verify.ps1`, `docker compose up -d --build` + `scripts/docker-smoke.ps1`, and Playwright against the running stack (dev server on 6300, nginx container on 6297).

**`docs/converge/2026-07-31-match-restore-design/`** (report + 6 per-pass files)
- **Outcome: CONVERGED**, 11 of 12 passes, 9 gaps closed. Passes 9, 10, 11 clean consecutively with the third adversarial.
- Carried an explicit **suppression set** taken from the design's Out of Scope — server-side durable autosave and self-recovery, reconstructing hidden orders, fleet transfer between participants — never re-flagged across passes.

**Why they still matter:** they record *why* deviations exist, not just that they do. The clearest example (report gap 1): the design stated that restore preserves fleet and ship ids; the implementation reissues them, because the by-ship-id and by-fleet-id endpoint lookups scan every match, so a restored copy sharing ids with a live match made both ambiguous. **The design doc was amended with a recorded deviation rather than the correct fix being reverted.** Anyone later "fixing" restore to preserve ids would reintroduce that bug. Gap 2 similarly records that ordnance markers had no restore coverage despite being in scope, now asserted with remapped source/target and fighter carrier links.

Both reports note the scans ran in-session rather than dispatched to subagents, per a standing instruction in those sessions.

---

## 6. Dependencies

**Target framework: `net10.0` uniformly across all 17 projects.** No stragglers, no multi-targeting. (Untracked `obj/Debug/net8.0/` folders survive from the retarget spike.)

| Package | Version | Used by | Note |
|---|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | 10.0.10 | Api | current with TFM |
| `Microsoft.OpenApi` | 2.11.0 | Api | current |
| `Scalar.AspNetCore` | 2.16.17 | Api | current |
| `Microsoft.Data.Sqlite` | 10.0.10 | Infrastructure | current with TFM |
| `SQLitePCLRaw.bundle_e_sqlite3` | 3.0.5 | Infrastructure | current |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.10 | Api.Tests | current |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | all 8 test projects | current |
| `xunit` | 2.9.3 | all 8 test projects | xUnit **v2**; v3 is the current line |
| `xunit.runner.visualstudio` | 3.1.4 | all 8 test projects | matches the v2 line |
| `coverlet.collector` | 6.0.4 | all 8 test projects | current |

**Nothing is pinned dangerously old.** The only version worth a conversation is xUnit v2 vs v3, and staying on v2 reads as a deliberate choice rather than rot — v3 is a breaking migration with no benefit visible in this suite.

### DEP-1 (MEDIUM) — No central package management
Ten packages are re-declared across eight test `.csproj` files with versions typed by hand. The versions **happen to agree today**, and there is no mechanism keeping them that way. `Directory.Build.props` already exists at the root, so the infrastructure for a `Directory.Packages.props` with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` is one file away.

### DEP-2 (LOW) — `PrivateAssets` inconsistency
Only `tests/ForceSignal.Api.Tests/ForceSignal.Api.Tests.csproj` sets `<PrivateAssets>all</PrivateAssets>` on `coverlet.collector` and `xunit.runner.visualstudio`. The other seven test projects declare both bare. Cosmetic given `IsPackable=false` everywhere, but it is unexplained inconsistency.

### DEP-3 (LOW) — `GenerateDocumentationFile=true` applies to test projects
`Directory.Build.props:6` sets it repo-wide, so all 8 test projects emit XML documentation nobody consumes. `CS1591` is in `NoWarn` so it is silent — it just adds build output and I/O.

---

## Priority order

1. **T1** — Add CI running `scripts/verify.ps1`. The gate is already written; nothing invokes it. Highest value per unit of effort in this list.
2. **TEST-1 / TEST-2** — Dirtside has 4,845 LOC of engine and 189 module tests, but **zero** tests on its 438-line application service and **12 of 13** HTTP routes untested. Mirror the StarGrunt test files.
3. **D1** — Replace `ex.Message.Contains("not found")` status routing at `Program.cs:177` and stop returning raw exception messages to clients.
4. **T2 / DEP-1** — `TreatWarningsAsErrors` in `Directory.Build.props`; `Directory.Packages.props` for central versions.
5. **Docs** — Write `docs/dirtside-fidelity-gaps.md` to match what Full Thrust and StarGrunt each received. The readiness warning at `Program.cs:851` is already a partial gap list, and `CloseAssault.cs` (488 lines) plus `SystemsDownRecovery.cs` are **built, tested, and unroutable** — engine work already paid for that a player cannot reach.
6. **TEST-3** — Make the silent save-drop in `DirtsideGameService.cs:108` / `StarGruntGameService.cs:139` observable.

### Rule-work backlog (game fidelity, separate from engineering)

| Item | Source | State |
|---|---|---|
| StarGrunt fatigue that changes mid-game | `stargrunt-fidelity-gaps.md:153` | only genuinely open rule gap |
| StarGrunt "assault-as-state" question | `stargrunt-fidelity-gaps.md:261` | undecided |
| FT2 rules profile (threshold rows by class, screens, needle reach, fighter moves, ship points) | `rules-fidelity-gaps.md:739`, `roadmap.md:123` | seam built, blocked on product decision |
| Dirtside: opportunity fire, area-defence interception, close assault, indirect fire over HTTP | `Program.cs:851` | engine built for close assault; routes missing |
| Play a full two-device match end to end | `rules-fidelity-gaps.md` closing line | never done since the turn structure changed |
| Reconnect-safe online play | `roadmap.md:154` | both halves built; needs real-world testing |
