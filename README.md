# ForceSignal

ForceSignal is a self-hosted space fleet tabletop companion for synchronized hidden orders, ship tracking, firing records, and after-action logs.

## Legal / IP Notice

ForceSignal is an unofficial tabletop companion. It is not affiliated with, endorsed by, or sponsored by Ground Zero Games.

Full Thrust and Ground Zero Games are trademarks/property of their respective owners.

Use your own legally obtained rules and fleet data. ForceSignal links outward to the official Ground Zero Games rules page instead of bundling rules PDFs or copied source material: <https://shop.groundzerogames.co.uk/rules.html>.

## Content Policy

- Do not bundle official PDFs, rule text, ship SSDs, fleet lists, faction background, logos, artwork, or pasted tables in this repo or app.
- **The engine owns procedures; the player owns every number those procedures read.** What is rolled
  against what, what shifts what, and what order things happen in are code. Die faces, damage
  tables, range bands, thresholds and reach are entered by the player and shipped by nobody. There
  is deliberately no default profile, and `docs/rules-profile-template.json` is blank rather than
  typical. A default that happens to be somebody's published numbers is still those numbers.
- **All three engines read their tables off a per-game profile the players fill in.** Full Thrust
  reads `RulesProfile`. Dirtside's three die tables - gunnery, signature and posture - are
  `DirtsideRulesProfile`, entered per game like the chit pot. StarGrunt's range page - how wide a
  band is for each quality of troops, the die a target rolls at each band out, how far small arms
  reach, and what soft cover, hard cover and a settled position are worth - is
  `StarGruntRulesProfile`, with the rungs cover is worth in a melee beside it. A ground game whose
  profile has no entry for a shot **refuses that shot before it is spent, and names the entry**; it
  does not fall back to anything, because a fallback here would be somebody's published table
  wearing a default's clothes. Only what a shot reads has to be there.
- **Two exceptions, written down rather than left quiet, both in Dirtside.** The firer's die still
  walks one rung per range band and one more for a hurried shot, and an Under Fire marker still costs
  one rung on the fire-effectiveness check. They are the same shape StarGrunt's range walk was - the
  size of a shift written into the engine - and were found when the die guard was widened to see
  that shape. They are listed by name in `EngineDiceContentPolicyTests.NotYetThePlayers`, a list
  kept separate from the not-a-rules-number exemptions precisely so it cannot read as settled.
- **What the guard covers, and what it does not.** That test counts every way either ground engine's
  code can reach the quality ladder, and each one has to be classified. It does not count numbers
  that are not dice. The engines still hold some counts as procedure - two actions to an activation,
  at most three suppression markers, a hit that more than doubles the armour roll kills, power
  armour and terror each double - and whether each of those is the engine's or the player's has not
  been argued line by line the way the dice have.
- JSON/CSV fleet import and export are for user-owned data, and so is the rules profile.
- Do not ship official Ground Zero Games fleets as sample data.
- If compatibility wording is used publicly, get written permission from Ground Zero Games first.

## Current Slice

- Create a guest match with a memorable room code.
- Join from another browser/device using the room code.
- Add a starter fleet and ship.
- Mark players ready to enter order entry.
- Lock private movement orders with a local salt.
- Reveal and verify orders after locking.
- Resolve ending velocity/course on the server.
- Advance to the next turn.
- Pick ship class icons and fleet accent colors for the tactical play map.
- Restore a match from an exported snapshot backup and claim your original seat.
- Record points values per ship, keep a device library of prebuilt fleets, and hold a match to an agreed points ceiling.

Matches are held in memory and, when a database path is configured, written to a SQLite file as
well - so restarting the API resumes the game rather than ending it. `docker compose` sets this up
on a named volume; running the API directly needs `Persistence:MatchDatabasePath` (or
`FORCESIGNAL_MATCH_DB`) set, and without it matches live only in memory and readiness says so.

SQLite rather than a database server because of what ForceSignal is: one process serving one
table's worth of players off a laptop somebody carried to the game. A server would add another
container to keep running before anyone can play, in exchange for concurrency a service holding a
single lock cannot use. `IMatchStore` is the seam if that ever changes.

## Asset Notes

The Full Thrust ship class icons are original inline SVG silhouettes drawn for ForceSignal and
carry no third-party obligation.

The ground unit icons are from [game-icons.net](https://game-icons.net) under
[Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/), which asks for the
artists to be credited wherever the work is distributed - which a repository does, whether or not
an app draws from it. **No screen renders one yet**: `UnitIcon.tsx` is imported only by its own test,
so the set never reaches the bundle. The credit therefore says the artwork *ships* rather than that
it is *used*, which is the smaller and truer claim, and it stays for exactly as long as the files
do. See [ATTRIBUTION.md](ATTRIBUTION.md) for the per-artist credit; the same credit appears in the
icon sheet's header and in the app's on-screen notice panel, so losing one copy does not put the
project out of licence. Tests in `UnitIcon.test.ts` fail if the credits go missing, if the sheet and
the app disagree about which icons exist, or if the icons get wired into a screen while the credit
still says they are not.

## Local Development

**Prerequisites:**

- **.NET 10 SDK.**
- **Node 22.4 or later** - a hard floor, not a preference. `vitest.config.ts` passes
  `--no-experimental-webstorage`, which only exists from 22.4; an older Node dies on the unrecognised
  option before a single test runs, and `engines` in `package.json` only *warns* at install. CI pins 22.
- **PowerShell 7 (`pwsh`)** for the two scripts written in it, `scripts/verify.ps1` and
  `scripts/docker-smoke.ps1`. It is not Windows-only: on Linux or macOS install `pwsh` and invoke the
  scripts as `pwsh ./scripts/verify.ps1`, which is exactly what CI does on its Ubuntu runner. Nothing
  else in the repository needs it - every other command below runs in bash or zsh as written.
- **Python 3.12 with Playwright**, only for `scripts/two-player-smoke.py`.

Run the API:

```sh
dotnet run --project src/ForceSignal.Api
```

Run the web app:

```sh
cd src/ForceSignal.Web
npm install
npm run dev
```

Open `http://localhost:6297`. The web app expects the API at `http://localhost:5225` by default.

Check one side without running the whole gate:

```sh
dotnet test                                   # every .NET project, from the repository root
npm test --prefix src/ForceSignal.Web         # tsc --noEmit, eslint, then vitest
npm run lint --prefix src/ForceSignal.Web     # eslint alone
```

## Docker Readiness

Copy `.env.example` to `.env` and set production-facing values before deploying:

- `FORCESIGNAL_WEB_ORIGIN`: public web origin allowed to call the API.
- `VITE_API_BASE_URL`: public API origin baked into the static web build.
- `FORCESIGNAL_MATCH_DB`: where matches are written. Defaults to a path on the named volume, which is what makes a restart resume the game; change it only to somewhere else you have mounted.
- `FORCESIGNAL_TRUST_FORWARDED_HEADERS`: see the note on reverse proxies below.
- `FORCESIGNAL_FEATURES_STARGRUNT`, `FORCESIGNAL_FEATURES_DIRTSIDE`: the ground-combat engines, off by default.

Every one of those is a name `docker-compose.yml` interpolates, which is the only way a value in `.env` reaches a container - Compose substitutes `.env` into the compose file and does not otherwise pass it through. `scripts/check-deployment-config.py` fails the build if the two files stop naming the same variables, so a knob cannot end up documented here and wired to nothing.

Run the stack:

```sh
docker compose up --build
```

Health surfaces:

- API liveness: `http://localhost:8080/health`
- API readiness: `http://localhost:8080/ready`
- Web liveness: `http://localhost:6297/health`

The production containers run without root privileges and drop Linux capabilities. API and web responses include baseline browser security headers.

Behind a reverse proxy or ingress, set `FORCESIGNAL_TRUST_FORWARDED_HEADERS=true` in `.env` - or `Proxy__TrustForwardedHeaders=true` directly on an API you run yourself - so the per-client rate limits read the caller's address from `X-Forwarded-For` instead of seeing every request as the proxy's. Leave it unset when the API is reached directly, because the header is a claim any caller can make and is only safe to believe when something you control is writing it.

The ground-combat engines (`FORCESIGNAL_FEATURES_STARGRUNT` and `FORCESIGNAL_FEATURES_DIRTSIDE` in `.env`, or `Features__StarGrunt` and `Features__Dirtside` on an API you run yourself) hand back a `token` when a game is created; every other route for that game requires it in the `X-Game-Token` header.

Dirtside plays direct fire, close assault and systems-down recovery over the wire. An assault is five routes under `/api/dirtside/games/{gameId}/assaults/` - `launch`, `stand`, `round`, `aftermath`, `follow-through` - taken in the order the rules give, and `/activations/current/recover-systems` is the crew's attempt to get a Systems Down marker off. Every threat level, chit validity, chit count and kill threshold those routes read is the player's, off their own record card; the API refuses a platoon whose card does not say rather than filling a number in.

The damage chit pot is the players' too. `POST /api/dirtside/games` takes an optional `chitPot` - how many chits of each colour and number, and how many of each special, counted off your own counter sheet - and that pot is carried with the game for its whole life, through restarts. It is optional for one release only: a game created without it falls back to a built-in composition whose special-chit counts are a *guess* rather than a published distribution, readiness says so, and every snapshot carries `chitPot.isBuiltInDefaultGuess` so the screen can say so too. The fallback is removed next release.

Readiness reports `persistence` as `sqlite`, `in-memory`, or `mixed` when some engines opened their table and others fell back, and lists every store it opened under `stores` - one per engine, because this server keeps a table per engine and any of them can fail on its own. It warns when matches would be lost on a restart, naming the engines affected. The warning is the only thing telling an operator their game is not being written down, so it is reported in every environment.

Smoke test a running stack:

```sh
pwsh ./scripts/docker-smoke.ps1
```

The API fails fast outside Development unless `Cors:AllowedOrigins` is configured. The compose file wires this from `FORCESIGNAL_WEB_ORIGIN`, and the API reads that name directly too, so running it without compose honours the same variable.

## Verification

```sh
pwsh ./scripts/verify.ps1
```

This is the whole gate CI runs, and it needs `pwsh` (see Prerequisites). To check a single side, the
`dotnet test` and `npm test` commands under Local Development are the same steps it takes.

Pass `-IncludeDocker` when Docker is running to build the images, start the compose stack, and run `scripts/docker-smoke.ps1`. CI passes it, because the smoke test holds the only assertions that read a running stack rather than a config file: that the configured database actually opened, so a restart resumes the game, and that nginx assembled the security headers onto every response including `/health`.

`scripts/check-deployment-config.py` runs first and needs no Docker. It holds `.env.example`, `docker-compose.yml`, `vite.config.ts`, `launchSettings.json` and `nginx.conf` to each other, which is where the disagreements that still build and still test green live. `scripts/check-ground-vocabulary.py` runs next and holds the web client's copy of the ground-combat vocabularies to the contracts assembly's.

`scripts/two-player-smoke.py` plays a whole turn through two browsers on two seats - the only check that hidden orders lock and reveal independently on separate devices, and that a phase turning over on one reaches the other without a reload. CI runs it as its own job. To run it yourself, start the API on 8080 and the web dev server against it, then run the script with Playwright installed:

```sh
ASPNETCORE_URLS=http://localhost:8080 dotnet run --project src/ForceSignal.Api --no-launch-profile
VITE_API_BASE_URL=http://localhost:8080 npm run dev --prefix src/ForceSignal.Web
python scripts/two-player-smoke.py
```

Each of the first two stays in the foreground, so give each its own terminal, and stop both when the
script finishes. In PowerShell the environment variables are set as
`$env:ASPNETCORE_URLS = "http://localhost:8080"` before the command instead.

## API Documentation

In Development, the API exposes:

- OpenAPI JSON: `http://localhost:5225/openapi/v1.json`
- Scalar API reference: `http://localhost:5225/scalar/v1`

Public API contracts and rules-profile primitives use XML documentation. Analyzer and documentation generation settings are centralized in `Directory.Build.props`, with rule severities in `.editorconfig`.
