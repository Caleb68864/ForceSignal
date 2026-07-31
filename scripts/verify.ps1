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
    Invoke-Step "Restore .NET dependencies" {
        Invoke-Native dotnet restore ForceSignal.slnx
    }

    Invoke-Step "Build .NET solution" {
        Invoke-Native dotnet build ForceSignal.slnx --no-restore
    }

    Invoke-Step "Run .NET tests" {
        Invoke-Native dotnet test ForceSignal.slnx --no-build
    }

    Invoke-Step "Check vulnerable .NET packages" {
        Invoke-Native dotnet list ForceSignal.slnx package --vulnerable --include-transitive
    }

    Invoke-Step "Install web dependencies" {
        Push-Location "src/ForceSignal.Web"
        try {
            Invoke-Native npm install
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
