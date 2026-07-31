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

The current persistence layer is intentionally in-memory so the first hidden-order workflow is easy to exercise. PostgreSQL/EF Core is the next hardening step.

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

Readiness currently reports `persistence = in-memory` with deployment warnings. That is intentional for tabletop-first real-world testing; public or long-running deployments should enable durable match storage after those workflows prove out.

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
