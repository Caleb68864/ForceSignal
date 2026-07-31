param(
    [string]$ApiBaseUrl = "http://localhost:8080",
    [string]$WebBaseUrl = "http://localhost:6297"
)

$ErrorActionPreference = "Stop"

function Invoke-Json($Uri, $Method = "GET", $Body = $null) {
    $parameters = @{
        Uri = $Uri
        Method = $Method
    }

    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = ($Body | ConvertTo-Json -Depth 8)
    }

    Invoke-RestMethod @parameters
}

Write-Host "Checking API health..."
$health = Invoke-Json "$ApiBaseUrl/health"
if ($health.status -ne "ok") {
    throw "API health returned '$($health.status)'."
}

Write-Host "Checking API readiness..."
$ready = Invoke-Json "$ApiBaseUrl/ready"
if ($ready.status -ne "ready") {
    throw "API readiness returned '$($ready.status)'."
}
if ($ready.persistence -ne "in-memory") {
    throw "API readiness returned unexpected persistence '$($ready.persistence)'."
}
if (-not $ready.warnings -or $ready.warnings.Count -lt 1) {
    throw "API readiness should report production warnings until persistent storage is enabled."
}

Write-Host "Checking web health..."
$webHealth = Invoke-WebRequest "$WebBaseUrl/health" -UseBasicParsing
if ($webHealth.StatusCode -ne 200) {
    throw "Web health returned HTTP $($webHealth.StatusCode)."
}

Write-Host "Checking web shell..."
$web = Invoke-WebRequest $WebBaseUrl -UseBasicParsing
if ($web.StatusCode -ne 200 -or $web.Content -notmatch "ForceSignal") {
    throw "Web shell did not load ForceSignal."
}

Write-Host "Checking match creation..."
$match = Invoke-Json "$ApiBaseUrl/api/matches" "POST" @{ displayName = "Smoke Admiral"; matchName = "Docker Smoke" }
if (-not $match.matchId -or -not $match.participantToken) {
    throw "Match creation did not return a match id and participant token."
}

Write-Host "Checking SignalR negotiate surface..."
$negotiate = Invoke-WebRequest "$ApiBaseUrl/hubs/match/negotiate?negotiateVersion=1" -Method POST -UseBasicParsing
if ($negotiate.StatusCode -ne 200) {
    throw "SignalR negotiate returned HTTP $($negotiate.StatusCode)."
}

Write-Host "Docker smoke passed."
