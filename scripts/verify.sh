#!/usr/bin/env bash
# The same gate as scripts/verify.ps1, for a machine without PowerShell.
#
#   scripts/verify.sh [--include-docker] [--include-two-player]
#
# Keep the two in step: the steps, their order and what each one fails on are the same, and a step
# added to one belongs in the other. CI runs verify.ps1, so that one is the reference.
set -euo pipefail

include_docker=false
include_two_player=false
for argument in "$@"; do
  case "$argument" in
    --include-docker) include_docker=true ;;
    # Plays a turn on two browser devices against a running API and dev server, as CI's two-player
    # job does. Needs Python Playwright: pip install playwright; playwright install chromium (or
    # point CHROMIUM_PATH at an installed Chromium).
    --include-two-player) include_two_player=true ;;
    -h|--help)
      sed -n '2,7p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown option: $argument" >&2
      exit 2
      ;;
  esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
scripts="$repo_root/scripts"
cd "$repo_root"

step() {
  echo
  echo "==> $1"
}

step "Check deployment configuration"
python3 "$scripts/check-deployment-config.py"

step "Check ground-combat vocabularies"
python3 "$scripts/check-ground-vocabulary.py"

step "Restore .NET dependencies"
dotnet restore ForceSignal.slnx

step "Build .NET solution"
dotnet build ForceSignal.slnx --no-restore

step "Run .NET tests"
dotnet test ForceSignal.slnx --no-build

# `dotnet list --vulnerable` exits 0 whether or not it found anything; the finding is in the output.
step "Check vulnerable .NET packages"
report="$(dotnet list ForceSignal.slnx package --vulnerable --include-transitive)"
echo "$report"
if grep -q 'has the following vulnerable packages' <<<"$report"; then
  echo "Vulnerable packages were reported above. Update them, or record why the finding does not apply." >&2
  exit 1
fi

step "Install web dependencies"
(cd src/ForceSignal.Web && npm ci)

step "Audit web dependencies"
(cd src/ForceSignal.Web && npm audit --audit-level=moderate)

step "Test web app"
(cd src/ForceSignal.Web && npm run test)

step "Build web app"
(cd src/ForceSignal.Web && npm run build)

step "Check web bundle budget"
(cd src/ForceSignal.Web && npm run check:bundle)

answers() {
  [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "$1" || true)" = "200" ]
}

port_in_use() {
  (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
}

# Stops a process and everything it started: `dotnet run` and `npm run dev` each start the real
# server as a child, and stopping only the parent would leave it holding the port.
stop_tree() {
  local pid="$1"
  [ -n "$pid" ] || return 0
  local child
  for child in $(pgrep -P "$pid" 2>/dev/null || true); do
    stop_tree "$child"
  done
  kill "$pid" 2>/dev/null || true
}

if [ "$include_two_player" = true ]; then
  step "Two-device smoke test"
  if ! python3 -c "import playwright" 2>/dev/null; then
    echo "Python Playwright is not installed. Run: pip install playwright; playwright install chromium" >&2
    exit 1
  fi

  # The script and the web app are both built against these two addresses, as in CI.
  for port in 8080 6297; do
    if port_in_use "$port"; then
      echo "Port $port is already in use. Stop whatever is listening there and run again." >&2
      exit 1
    fi
  done

  logs="$(mktemp -d -t forcesignal-two-player.XXXXXX)"
  api_pid=""
  web_pid=""
  cleanup_two_player() {
    stop_tree "$web_pid"
    stop_tree "$api_pid"
  }
  show_logs() {
    for name in api.log web.log; do
      echo "--- $name ---"
      tail -n 60 "$logs/$name" || true
    done
  }
  trap cleanup_two_player EXIT

  # Development so CORS lets the dev server in without an origin being configured;
  # --no-launch-profile keeps launchSettings.json from moving it back to 5225.
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:8080 \
    dotnet run --project src/ForceSignal.Api --no-launch-profile >"$logs/api.log" 2>&1 &
  api_pid=$!
  (cd src/ForceSignal.Web && exec env VITE_API_BASE_URL=http://localhost:8080 npm run dev) >"$logs/web.log" 2>&1 &
  web_pid=$!

  # Polled rather than slept, as in CI: a fixed wait is too short on a slow machine.
  up=false
  for _ in $(seq 1 90); do
    sleep 1
    if answers http://localhost:8080/health && answers http://localhost:6297; then
      up=true
      break
    fi
  done

  if [ "$up" != true ]; then
    echo "The API or the web dev server did not answer within 90 seconds." >&2
    show_logs
    exit 1
  fi

  if ! python3 "$scripts/two-player-smoke.py"; then
    show_logs
    exit 1
  fi

  cleanup_two_player
  trap - EXIT
else
  echo
  echo "==> Skipping the two-device smoke test. Pass --include-two-player to run it (needs Python Playwright)."
fi

step "Validate Docker Compose configuration"
docker compose config >/dev/null

if [ "$include_docker" = true ]; then
  step "Build Docker images"
  docker compose build

  step "Smoke test Docker stack"
  # --wait blocks until the healthchecks pass, so the smoke test cannot race the API's startup.
  if ! docker compose up --detach --wait --wait-timeout 180; then
    docker compose logs --no-color --tail 200
    exit 1
  fi

  # The smoke checks themselves are PowerShell (scripts/docker-smoke.ps1); there is one copy.
  status=0
  if command -v pwsh >/dev/null; then
    pwsh -NoProfile -File "$scripts/docker-smoke.ps1" || status=$?
  else
    echo "scripts/docker-smoke.ps1 needs PowerShell (pwsh), which is not installed." >&2
    status=1
  fi

  if [ "$status" -ne 0 ]; then
    docker compose logs --no-color --tail 200
  fi
  docker compose down --volumes
  exit "$status"
else
  echo
  echo "==> Skipping Docker image build and smoke test. Pass --include-docker when Docker is running."
fi
