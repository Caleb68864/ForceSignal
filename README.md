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
[Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/), which requires
the artists to be credited wherever the icons are used - including in a build someone else runs.
See [ATTRIBUTION.md](ATTRIBUTION.md) for the per-artist credit. The same credit appears in the icon
sheet's header and in the app's on-screen notice panel, so losing one copy does not put the project
out of licence. Tests in `UnitIcon.test.ts` fail if the credits go missing or if the sheet and the
app disagree about which icons exist.

## Local Development

Run the API:

```powershell
dotnet run --project src/ForceSignal.Api
```

Run the web app:

```powershell
cd src/ForceSignal.Web
npm install
npm run dev
```

Open `http://localhost:6297`. The web app expects the API at `http://localhost:5225` by default.

## Docker Readiness

Copy `.env.example` to `.env` and set production-facing values before deploying:

- `FORCESIGNAL_WEB_ORIGIN`: public web origin allowed to call the API.
- `VITE_API_BASE_URL`: public API origin baked into the static web build.
- `FORCESIGNAL_MATCH_DB`: where matches are written. Defaults to a path on the named volume, which is what makes a restart resume the game; change it only to somewhere else you have mounted.
- `FORCESIGNAL_TRUST_FORWARDED_HEADERS`: see the note on reverse proxies below.
- `FORCESIGNAL_FEATURES_STARGRUNT`, `FORCESIGNAL_FEATURES_DIRTSIDE`: the ground-combat engines, off by default.

Every one of those is a name `docker-compose.yml` interpolates, which is the only way a value in `.env` reaches a container - Compose substitutes `.env` into the compose file and does not otherwise pass it through. `scripts/check-deployment-config.py` fails the build if the two files stop naming the same variables, so a knob cannot end up documented here and wired to nothing.

Run the stack:

```powershell
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

Readiness reports `persistence` as `sqlite` or `in-memory`, and warns when matches would be lost on a restart. The warning is the only thing telling an operator their game is not being written down, so it is reported in every environment.

Smoke test a running stack:

```powershell
.\scripts\docker-smoke.ps1
```

The API fails fast outside Development unless `Cors:AllowedOrigins` is configured. The compose file wires this from `FORCESIGNAL_WEB_ORIGIN`.

## Verification

```powershell
.\scripts\verify.ps1
```

Pass `-IncludeDocker` when Docker is running to build the images, start the compose stack, and run `scripts/docker-smoke.ps1`. CI passes it, because the smoke test holds the only assertions that read a running stack rather than a config file: that the configured database actually opened, so a restart resumes the game, and that nginx assembled the security headers onto every response including `/health`.

`scripts/check-deployment-config.py` runs first and needs no Docker. It holds `.env.example`, `docker-compose.yml`, `vite.config.ts`, `launchSettings.json` and `nginx.conf` to each other, which is where the disagreements that still build and still test green live.

## API Documentation

In Development, the API exposes:

- OpenAPI JSON: `http://localhost:5225/openapi/v1.json`
- Scalar API reference: `http://localhost:5225/scalar/v1`

Public API contracts and rules-profile primitives use XML documentation. Analyzer and documentation generation settings are centralized in `Directory.Build.props`, with rule severities in `.editorconfig`.
