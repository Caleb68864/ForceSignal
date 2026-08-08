# ForceSignal

ForceSignal is a self-hosted space fleet tabletop companion for synchronized hidden orders, ship tracking, firing records, and after-action logs.

## Legal / IP Notice

ForceSignal is an unofficial tabletop companion. It is not affiliated with, endorsed by, or sponsored by Ground Zero Games.

Full Thrust and Ground Zero Games are trademarks/property of their respective owners.

Use your own legally obtained rules and fleet data. ForceSignal links outward to the official Ground Zero Games rules page instead of bundling rules PDFs or copied source material: <https://shop.groundzerogames.co.uk/rules.html>.

## Content Policy

- Do not bundle official PDFs, rule text, ship SSDs, fleet lists, faction background, logos, artwork, or pasted Fleet Book tables in this repo or app.
- JSON/CSV fleet import and export are for user-owned data.
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

The ship class icons used by the web app are original inline SVG silhouettes created for ForceSignal. No third-party icon pack is bundled. If a later pass imports icons from a source such as game-icons.net, add the required license attribution next to this note and in any distributed app credits.

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

Run the stack:

```powershell
docker compose up --build
```

Health surfaces:

- API liveness: `http://localhost:8080/health`
- API readiness: `http://localhost:8080/ready`
- Web liveness: `http://localhost:6297/health`

The production containers run without root privileges and drop Linux capabilities. API and web responses include baseline browser security headers.

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

Pass `-IncludeDocker` when Docker is running to build the images, start the compose stack, and run `scripts/docker-smoke.ps1`.

## API Documentation

In Development, the API exposes:

- OpenAPI JSON: `http://localhost:5225/openapi/v1.json`
- Scalar API reference: `http://localhost:5225/scalar/v1`

Public API contracts and rules-profile primitives use XML documentation. Analyzer and documentation generation settings are centralized in `Directory.Build.props`, with rule severities in `.editorconfig`.
