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
# docker-compose.yml mounts a volume and points Persistence:MatchDatabasePath at it, precisely so a
# restarted container resumes the game instead of ending it. So sqlite is what a healthy stack
# reports, and "in-memory" here means the server could not open that file and fell back: every game
# still plays, and every one of them dies at the next restart. That is the failure this stack exists
# to rule out, so it is the one worth asserting.
if ($ready.persistence -ne "sqlite") {
    throw "API readiness returned persistence '$($ready.persistence)'; the compose stack configures sqlite, so the database file did not open."
}

# And the same fallback says so in the warnings, which is the half an operator reads. A healthy
# stack has nothing to say about storage at all.
$storageWarnings = @($ready.warnings) | Where-Object { $_ -match "stored in memory" }
if ($storageWarnings) {
    throw "API readiness warns that matches are not durable: $($storageWarnings -join ' ')"
}

Write-Host "Checking web health..."
$webHealth = Invoke-WebRequest "$WebBaseUrl/health" -UseBasicParsing
if ($webHealth.StatusCode -ne 200) {
    throw "Web health returned HTTP $($webHealth.StatusCode)."
}

# Every location in nginx.conf includes the same header file, and it has to, because nginx drops a
# parent's add_header lines the moment a location declares one of its own. /health declares
# Content-Type, so it is the location that loses them, and it is the one nothing else would notice:
# it answers 200 either way. Asserting on the running container is the only check that reads what
# nginx actually assembled rather than what the config looks like it says.
$requiredHeaders = @(
    "X-Content-Type-Options",
    "Referrer-Policy",
    "Permissions-Policy",
    "X-Frame-Options",
    "Content-Security-Policy"
)
foreach ($url in @("$WebBaseUrl/health", $WebBaseUrl)) {
    $response = Invoke-WebRequest $url -UseBasicParsing
    $missing = $requiredHeaders | Where-Object { -not $response.Headers.ContainsKey($_) }
    if ($missing) {
        throw "$url answered without the security headers: $($missing -join ', ')."
    }
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
