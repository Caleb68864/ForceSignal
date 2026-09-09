param(
    [switch]$IncludeDocker
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

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    # First, because it is the cheapest step and the one whose failures are invisible to every other
    # step: a deployment file that disagrees with another one still builds, still tests green, and
    # is only found by whoever deploys it.
    Invoke-Step "Check deployment configuration" {
        Invoke-Native python3 (Join-Path $PSScriptRoot "check-deployment-config.py")
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

    Invoke-Step "Validate Docker Compose configuration" {
        Invoke-Native docker compose config | Out-Null
    }

    if ($IncludeDocker) {
        Invoke-Step "Build Docker images" {
            Invoke-Native docker compose build
        }

        Invoke-Step "Smoke test Docker stack" {
            Invoke-Native docker compose up -d
            try {
                & (Join-Path $PSScriptRoot "docker-smoke.ps1")
                if ($LASTEXITCODE -ne 0) {
                    throw "docker-smoke.ps1 exited with code $LASTEXITCODE."
                }
            }
            finally {
                Invoke-Native docker compose down
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
