param(
    [switch]$IncludeDocker,
    # Plays a turn on two browser devices against a running API and dev server, as CI's two-player
    # job does. Needs Python Playwright: pip install playwright; playwright install chromium.
    [switch]$IncludeTwoPlayer
)

$ErrorActionPreference = "Stop"

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE."
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Command
}

# True when something answers an HTTP GET at the address with a success status.
function Test-Answers {
    param([string]$Url)
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

# True when something is already listening on the port. A plain connect attempt rather than
# Get-NetTCPConnection, which exists only on Windows.
function Test-PortInUse {
    param([int]$Port)
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        return $client.ConnectAsync("127.0.0.1", $Port).Wait(300) -and $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    # First, because it is the cheapest step and the one whose failures are invisible to every other
    # step: a deployment file that disagrees with another one still builds, still tests green, and
    # is only found by whoever deploys it.
    Invoke-Step "Check deployment configuration" {
        Invoke-Native python3 (Join-Path $PSScriptRoot "check-deployment-config.py")
    }

    # The last link in the chain that runs from the ground-combat engine enums to the client's
    # dropdowns. The two before it are C# and are covered by the .NET tests below; this one crosses
    # a language boundary, so nothing but a script can hold it.
    Invoke-Step "Check ground-combat vocabularies" {
        Invoke-Native python3 (Join-Path $PSScriptRoot "check-ground-vocabulary.py")
    }

    Invoke-Step "Restore .NET dependencies" {
        Invoke-Native dotnet restore ForceSignal.slnx
    }

    Invoke-Step "Build .NET solution" {
        Invoke-Native dotnet build ForceSignal.slnx --no-restore
    }

    Invoke-Step "Run .NET tests" {
        Invoke-Native dotnet test ForceSignal.slnx --no-build
    }

    # `dotnet list --vulnerable` reports and exits 0 whether or not it found anything, so calling it
    # through Invoke-Native gates on nothing at all: a known-vulnerable package printed a warning
    # into a green build. The finding is in the output rather than the exit code, so the output is
    # what has to be read.
    Invoke-Step "Check vulnerable .NET packages" {
        $report = & dotnet list ForceSignal.slnx package --vulnerable --include-transitive
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet list package --vulnerable exited with code $LASTEXITCODE."
        }

        $report | Write-Host
        # A project with nothing wrong with it says so in a sentence; a project with a finding prints
        # a table instead, headed by the line this looks for. Keying on the table rather than on the
        # severity words avoids a package that merely has "Low" in its name failing the build.
        if ($report | Where-Object { $_ -match 'has the following vulnerable packages' }) {
            throw "Vulnerable packages were reported above. Update them, or record why the finding does not apply."
        }
    }

    # `npm ci` rather than `npm install`: install is free to resolve a newer version than the lockfile
    # names and to rewrite the lockfile while doing it, which means CI can pass against a dependency
    # tree that nobody committed and that nobody else will get. The Dockerfile already gets this
    # right, and the point of a gate is that it ran what the repository actually says.
    Invoke-Step "Install web dependencies" {
        Push-Location "src/ForceSignal.Web"
        try {
            Invoke-Native npm ci
        }
        finally {
            Pop-Location
        }
    }

    Invoke-Step "Audit web dependencies" {
        Push-Location "src/ForceSignal.Web"
        try {
            Invoke-Native npm audit --audit-level=moderate
        }
        finally {
            Pop-Location
        }
    }

    # The web app has a test script - type check, lint and the vitest suite - and until now nothing
    # in the gate ran it, so a whole side of the app was covered by tests that only ever ran on
    # somebody's laptop if they remembered. It goes before the build: a type error or a failing
    # assertion should be reported as itself rather than as whatever the bundler makes of it.
    Invoke-Step "Test web app" {
        Push-Location "src/ForceSignal.Web"
        try {
            Invoke-Native npm run test
        }
        finally {
            Pop-Location
        }
    }

    Invoke-Step "Build web app" {
        Push-Location "src/ForceSignal.Web"
        try {
            Invoke-Native npm run build
        }
        finally {
            Pop-Location
        }
    }

    # Size, measured on what was just built: the entry chunk every device loads, everything loaded
    # later, and no animated-map code in the entry chunk (a device that leaves it off pays nothing).
    Invoke-Step "Check web bundle budget" {
        Push-Location "src/ForceSignal.Web"
        try {
            Invoke-Native npm run check:bundle
        }
        finally {
            Pop-Location
        }
    }

    # The one check that drives two devices through a whole turn. It used to run only in CI's
    # two-player job, so a change to the screens could pass everything here and still turn that job
    # red after the push - which is how a Reveal button that stopped being offered went unnoticed.
    # Opt-in because it needs Python Playwright and two free ports; CI runs it in its own job.
    if ($IncludeTwoPlayer) {
        Invoke-Step "Two-device smoke test" {
            $python = if ($IsWindows) { "python" } else { "python3" }
            & $python -c "import playwright" 2>$null
            if ($LASTEXITCODE -ne 0) {
                throw "Python Playwright is not installed. Run: pip install playwright; playwright install chromium"
            }

            # The script and the web app are both built against these two addresses, as in CI.
            foreach ($port in 8080, 6297) {
                $listening = Test-PortInUse -Port $port
                if ($listening) {
                    throw "Port $port is already in use. Stop whatever is listening there and run again."
                }
            }

            $logs = Join-Path ([System.IO.Path]::GetTempPath()) "forcesignal-two-player"
            New-Item -ItemType Directory -Force -Path $logs | Out-Null
            $npm = if ($IsWindows) { "npm.cmd" } else { "npm" }
            $saved = @{
                ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
                ASPNETCORE_URLS = $env:ASPNETCORE_URLS
                VITE_API_BASE_URL = $env:VITE_API_BASE_URL
            }
            $api = $null
            $web = $null
            try {
                # Development so CORS lets the dev server in without an origin being configured;
                # --no-launch-profile keeps launchSettings.json from moving it back to 5225.
                $env:ASPNETCORE_ENVIRONMENT = "Development"
                $env:ASPNETCORE_URLS = "http://localhost:8080"
                $api = Start-Process dotnet -ArgumentList "run", "--project", "src/ForceSignal.Api", "--no-launch-profile" `
                    -RedirectStandardOutput (Join-Path $logs "api.log") -RedirectStandardError (Join-Path $logs "api.err.log") -PassThru -NoNewWindow
                $env:VITE_API_BASE_URL = "http://localhost:8080"
                $web = Start-Process $npm -ArgumentList "run", "dev" -WorkingDirectory "src/ForceSignal.Web" `
                    -RedirectStandardOutput (Join-Path $logs "web.log") -RedirectStandardError (Join-Path $logs "web.err.log") -PassThru -NoNewWindow

                # Polled rather than slept, as in CI: a fixed wait is too short on a slow machine.
                $up = $false
                for ($attempt = 0; $attempt -lt 90 -and -not $up; $attempt++) {
                    Start-Sleep -Seconds 1
                    $up = (Test-Answers "http://localhost:8080/health") -and (Test-Answers "http://localhost:6297")
                }

                if (-not $up) {
                    throw "The API or the web dev server did not answer within 90 seconds."
                }

                Invoke-Native $python (Join-Path $PSScriptRoot "two-player-smoke.py")
            }
            catch {
                foreach ($name in "api.log", "api.err.log", "web.log", "web.err.log") {
                    $path = Join-Path $logs $name
                    if (Test-Path $path) {
                        Write-Host "--- $name ---"
                        Get-Content $path -Tail 60 | Write-Host
                    }
                }

                throw
            }
            finally {
                # Whole trees: `dotnet run` and `npm run dev` each start the real server as a child,
                # and stopping only the parent would leave it holding the port for the next run.
                foreach ($process in $api, $web) {
                    if ($process -and -not $process.HasExited) {
                        $process.Kill($true)
                        $process.WaitForExit(10000) | Out-Null
                    }
                }

                foreach ($name in $saved.Keys) {
                    Set-Item -Path "env:$name" -Value $saved[$name] -ErrorAction SilentlyContinue
                    if ($null -eq $saved[$name]) {
                        Remove-Item -Path "env:$name" -ErrorAction SilentlyContinue
                    }
                }
            }
        }
    }
    else {
        Write-Host ""
        Write-Host "==> Skipping the two-device smoke test. Pass -IncludeTwoPlayer to run it (needs Python Playwright)."
    }

    Invoke-Step "Validate Docker Compose configuration" {
        Invoke-Native docker compose config | Out-Null
    }

    if ($IncludeDocker) {
        Invoke-Step "Build Docker images" {
            Invoke-Native docker compose build
        }

        Invoke-Step "Smoke test Docker stack" {
            # Called directly rather than through Invoke-Native, and this is not a style choice.
            # Invoke-Native declares [Parameter()] attributes, which makes it an advanced function,
            # which gives it the common parameters - and "-d" is an unambiguous prefix of "-Debug".
            # So PowerShell bound the -d to Invoke-Native itself and docker never saw it: the stack
            # came up attached, compose sat streaming container logs, and the step hung until
            # something killed it. Nothing noticed for as long as this branch never ran.
            #
            # --wait makes compose block until the healthchecks pass rather than until the
            # containers merely exist, so a smoke test that follows cannot race the API's startup,
            # and --wait-timeout means a stack that never becomes healthy fails here, with
            # compose's own reason, instead of failing as a confusing refusal further down.
            & docker compose up --detach --wait --wait-timeout 180
            if ($LASTEXITCODE -ne 0) {
                & docker compose logs --no-color --tail 200
                throw "docker compose up failed with code $LASTEXITCODE."
            }

            try {
                & (Join-Path $PSScriptRoot "docker-smoke.ps1")
                if ($LASTEXITCODE -ne 0) {
                    throw "docker-smoke.ps1 exited with code $LASTEXITCODE."
                }
            }
            catch {
                # The container logs are the only place the reason lives once the stack is down.
                & docker compose logs --no-color --tail 200
                throw
            }
            finally {
                & docker compose down --volumes
            }
        }
    }
    else {
        Write-Host ""
        Write-Host "==> Skipping Docker image build and smoke test. Pass -IncludeDocker when Docker is running."
    }
}
finally {
    Pop-Location
}
