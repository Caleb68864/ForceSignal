using ForceSignal.Api.Endpoints;
using ForceSignal.Api.Hubs;
using ForceSignal.Application.Features;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Features;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

const string RoomCodeLookupPolicy = "room-code-lookup";
const string RestorePolicy = "match-restore";

// A snapshot of a real match is tens of kilobytes. The ceiling is far above that and far below
// anything that would strain the machine, so a mistaken upload fails fast instead of being read
// into memory in full.
const long MaxRestoreBodyBytes = 8 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = ReadAllowedOrigins(builder.Configuration, builder.Environment);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();

        if (allowedOrigins.AllowAnyOrigin)
        {
            policy.SetIsOriginAllowed(_ => true);
        }
        else
        {
            policy.WithOrigins(allowedOrigins.Origins);
        }
    });
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<IMatchService, InMemoryMatchService>();

// Which optional game engines this server offers. Both ground-combat engines default to off: they
// are built alongside the working Full Thrust game and must not be able to reach a table that
// turned up to play it. See FeatureFlags for why the flag is checked in more than one place.
// Resolved from the built configuration rather than the builder's, so a host that layers settings
// in during startup - a container, or an integration test - is read rather than missed.
builder.Services.AddSingleton(sp => FeatureFlags.Read(
    new ConfigurationFeatureSource(sp.GetRequiredService<IConfiguration>())));

// A room code is the only thing standing between a stranger and a seat, and it is short enough to
// be read aloud. Guessing one is a matter of trying codes quickly, so the two endpoints that turn
// a code into a match are the ones worth slowing down. The window is generous enough that a player
// mistyping their code a few times never notices it.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RoomCodeLookupPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Restoring is expensive - it parses a file and allocates a whole match - so it gets its own,
    // tighter budget.
    options.AddPolicy(RestorePolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();
var features = app.Services.GetRequiredService<FeatureFlags>();
var RestoreJson = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
    // A snapshot is four or five objects deep. A file nested far past that is not a snapshot
    // someone saved, and parsing it would recurse as far as it was told to.
    MaxDepth = 32,
};

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException
        or InvalidOperationException
        or ArgumentException
        or KeyNotFoundException
        or JsonException
        or FormatException
        or OverflowException)
    {
        // Once any of the response has gone out there is no status line left to rewrite, and
        // trying throws a second exception on top of the first. Let the host tear the connection
        // down instead of masking the original fault.
        if (context.Response.HasStarted)
        {
            throw;
        }

        var (status, title) = ex switch
        {
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Action not allowed"),
            InvalidOperationException when ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                => (StatusCodes.Status404NotFound, "Request could not be completed"),
            _ => (StatusCodes.Status400BadRequest, "Request could not be completed"),
        };
        await WriteProblem(context, status, title, ex.Message);
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    // The API serves JSON, never a page, so nothing it returns has any business being framed.
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    await next();
});

app.UseCors();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ForceSignal.Api" }))
    .WithName("GetHealth")
    .WithTags("Operations")
    .WithSummary("Reports API liveness.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/ready", () => Results.Ok(new
{
    status = "ready",
    service = "ForceSignal.Api",
    environment = app.Environment.EnvironmentName,
    cors = allowedOrigins.AllowAnyOrigin ? "development-any-origin" : "configured",
    persistence = "in-memory",
    features = features.ToDto(),
    warnings = ReadDeploymentWarnings(builder.Configuration, app.Environment, allowedOrigins, features)
}))
    .WithName("GetReadiness")
    .WithTags("Operations")
    .WithSummary("Reports API readiness and deployment-critical configuration state.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/api/features", () => Results.Ok(features.ToDto()))
    .WithName("GetFeatures")
    .WithTags("Operations")
    .WithSummary("Reports which optional game engines this server offers.")
    .Produces<FeatureFlagsDto>();

app.MapHub<MatchHub>("/hubs/match");

// Ground-combat engines mount their endpoints here. A disabled engine is not mapped at all, so its
// routes 404 rather than existing in a half-wired state - which is the point of the flag.
if (features.StarGrunt)
{
    app.MapStarGruntEndpoints();
}

if (features.Dirtside)
{
    app.MapDirtsideEndpoints();
}

app.MapPost("/api/matches", (CreateMatchRequest request, IMatchService matches) =>
    Results.Ok(matches.CreateMatch(request)))
    .WithName("CreateMatch")
    .WithTags("Matches")
    .WithSummary("Creates a new match and owner participant session.")
    .Produces<MatchCreatedResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapPost("/api/matches/join", (JoinMatchRequest request, IMatchService matches) =>
    Results.Ok(matches.JoinMatch(request)))
    .WithName("JoinMatch")
    .WithTags("Matches")
    .WithSummary("Joins a match by room code.")
    .RequireRateLimiting(RoomCodeLookupPolicy)
    .Produces<MatchJoinedResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapPost("/api/matches/restore", async (HttpRequest http, IMatchService matches) =>
{
    // A restore reads a file from whoever asks and turns it into allocated state, so the size is
    // checked before a byte of it is parsed. Both the declared length and the actual stream are
    // bounded, because Content-Length is a claim rather than a fact.
    if (http.ContentLength > MaxRestoreBodyBytes)
    {
        throw new InvalidOperationException(
            $"That snapshot is larger than the {MaxRestoreBodyBytes / (1024 * 1024)}MB a match file can be.");
    }

    // Parse the body here rather than binding JsonElement: model-binding failures surface as a
    // plain-text 400, and this endpoint must answer malformed files with problem+json.
    JsonElement body;
    try
    {
        using var bounded = new BoundedStream(http.Body, MaxRestoreBodyBytes);
        body = await JsonSerializer.DeserializeAsync<JsonElement>(bounded, RestoreJson);
    }
    catch (JsonException ex)
    {
        throw new InvalidOperationException($"Snapshot file could not be read as JSON: {ex.Message}");
    }

    // Accept either the exported {savedAt, snapshot} wrapper or a bare snapshot, because a user
    // will hand over whichever file they kept.
    var hasWrapper = body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("snapshot", out var wrapped)
        && wrapped.ValueKind == JsonValueKind.Object;
    var snapshotElement = hasWrapper ? body.GetProperty("snapshot") : body;
    DateTimeOffset? savedAt = body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("savedAt", out var saved)
        && saved.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(saved.GetString(), out var parsed)
            ? parsed
            : null;

    MatchSnapshotDto? snapshot;
    try
    {
        snapshot = snapshotElement.Deserialize<MatchSnapshotDto>(RestoreJson);
    }
    catch (JsonException ex)
    {
        throw new InvalidOperationException($"Snapshot could not be read: {ex.Message}");
    }

    if (snapshot is null)
    {
        throw new InvalidOperationException("Snapshot payload was empty.");
    }

    return Results.Ok(matches.RestoreMatch(snapshot, savedAt));
})
    .WithName("RestoreMatch")
    .WithTags("Matches")
    .WithSummary("Rebuilds a match from an exported snapshot backup.")
    .RequireRateLimiting(RestorePolicy)
    .Produces<MatchRestoredResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapGet("/api/matches/by-code/{joinCode}", (string joinCode, IMatchService matches) =>
    Results.Ok(matches.FindMatchByCode(joinCode)))
    .WithName("GetMatchByCode")
    .WithTags("Matches")
    .WithSummary("Resolves a room code to a match id and whether seats are still claimable.")
    .RequireRateLimiting(RoomCodeLookupPolicy)
    .Produces<MatchIdentityDto>()
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapGet("/api/matches/{matchId:guid}/seats", (Guid matchId, IMatchService matches) =>
    Results.Ok(matches.GetSeats(matchId)))
    .WithName("GetMatchSeats")
    .WithTags("Matches")
    .WithSummary("Lists claimable seats in a restored match.")
    .Produces<IReadOnlyList<MatchSeatDto>>()
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/seats/{participantId:guid}/claim", async (
    Guid matchId,
    Guid participantId,
    ClaimSeatRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var session = matches.ClaimSeat(matchId, participantId, request);
    await NotifySnapshotChanged(hub, matches.GetSnapshot(matchId), "SeatClaimed");
    return Results.Ok(session);
})
    .WithName("ClaimMatchSeat")
    .WithTags("Matches")
    .WithSummary("Claims a seat in a restored match and issues a participant token.")
    .Produces<MatchJoinedResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapGet("/api/matches/{matchId:guid}/snapshot", (Guid matchId, IMatchService matches) =>
    Results.Ok(matches.GetSnapshot(matchId)))
    .WithName("GetMatchSnapshot")
    .WithTags("Matches")
    .WithSummary("Gets the authoritative match snapshot.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/participants/me/ready", async (
    Guid matchId,
    ReadyRequest request,
    HttpRequest http,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.SetReady(matchId, ReadParticipantToken(http), request.IsReady);
    await NotifySnapshotChanged(hub, snapshot, "ParticipantReadyChanged");
    return Results.Ok(snapshot);
})
    .WithName("SetParticipantReady")
    .WithTags("Participants")
    .WithSummary("Sets the caller's ready state for fleet setup.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status403Forbidden);

app.MapPost("/api/matches/{matchId:guid}/table", async (
    Guid matchId,
    UpdateMatchTableRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.UpdateTable(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "TableUpdated");
    return Results.Ok(snapshot);
})
    .WithName("UpdateMatchTable")
    .WithTags("Matches")
    .WithSummary("Updates table dimensions used by the play map.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapPost("/api/matches/{matchId:guid}/rules-layer", async (
    Guid matchId,
    UpdateRulesLayerRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.UpdateRulesLayer(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "RulesLayerChanged");
    return Results.Ok(snapshot);
})
    .WithName("UpdateRulesLayer")
    .WithTags("Matches")
    .WithSummary("Switches the rules layer the match is played under, during fleet setup.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/points-limit", async (
    Guid matchId,
    UpdateMatchPointsLimitRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.UpdatePointsLimit(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "PointsLimitUpdated");
    return Results.Ok(snapshot);
})
    .WithName("UpdateMatchPointsLimit")
    .WithTags("Matches")
    .WithSummary("Sets the agreed points ceiling per player. Zero means unlimited.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status403Forbidden);

app.MapPost("/api/matches/{matchId:guid}/fleets", async (
    Guid matchId,
    CreateFleetRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.CreateFleet(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "FleetChanged");
    return Results.Ok(snapshot);
})
    .WithName("CreateFleet")
    .WithTags("Fleets")
    .WithSummary("Creates a fleet for the caller.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapPost("/api/fleets/{fleetId:guid}/ships", async (
    Guid fleetId,
    CreateShipRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.CreateShip(fleetId, request);
    await NotifySnapshotChanged(hub, snapshot, "FleetChanged");
    return Results.Ok(snapshot);
})
    .WithName("CreateShip")
    .WithTags("Ships")
    .WithSummary("Creates a ship in an owned fleet.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/ships/{shipId:guid}/damage", async (
    Guid shipId,
    UpdateShipDamageRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.UpdateShipDamage(shipId, request);
    await NotifySnapshotChanged(hub, snapshot, "ShipDamageUpdated");
    return Results.Ok(snapshot);
})
    .WithName("UpdateShipDamage")
    .WithTags("Ships")
    .WithSummary("Updates the tracked damage record for an owned ship.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/ships/{shipId:guid}/repair", async (
    Guid shipId,
    AttemptRepairsRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.AttemptRepairs(shipId, request);
    await NotifySnapshotChanged(hub, snapshot, "RepairsAttempted");
    return Results.Ok(snapshot);
})
    .WithName("AttemptRepairs")
    .WithTags("Damage")
    .WithSummary("Puts a ship's damage control parties to work on systems lost to threshold checks.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/ships/{shipId:guid}/fighter-ops", async (
    Guid shipId,
    UpdateFighterOperationsRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.UpdateFighterOperations(shipId, request);
    await NotifySnapshotChanged(hub, snapshot, "FighterOperationsUpdated");
    return Results.Ok(snapshot);
})
    .WithName("UpdateFighterOperations")
    .WithTags("Ships")
    .WithSummary("Updates fighter launch, recovery, endurance, and carrier assignment.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/ordnance", async (
    Guid matchId,
    CreateOrdnanceMarkerRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.CreateOrdnanceMarker(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "OrdnanceMarkerCreated");
    return Results.Ok(snapshot);
})
    .WithName("CreateOrdnanceMarker")
    .WithTags("Ordnance")
    .WithSummary("Adds a launched ordnance marker to the play map.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/ordnance/{markerId:guid}", async (
    Guid markerId,
    UpdateOrdnanceMarkerRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.UpdateOrdnanceMarker(markerId, request);
    await NotifySnapshotChanged(hub, snapshot, "OrdnanceMarkerUpdated");
    return Results.Ok(snapshot);
})
    .WithName("UpdateOrdnanceMarker")
    .WithTags("Ordnance")
    .WithSummary("Updates a launched ordnance marker on the play map.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/ordnance/{markerId:guid}/remove", async (
    Guid markerId,
    RemoveOrdnanceMarkerRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.RemoveOrdnanceMarker(markerId, request);
    await NotifySnapshotChanged(hub, snapshot, "OrdnanceMarkerRemoved");
    return Results.Ok(snapshot);
})
    .WithName("RemoveOrdnanceMarker")
    .WithTags("Ordnance")
    .WithSummary("Removes a launched ordnance marker from the play map.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/ships/{shipId:guid}/profile", async (
    Guid shipId,
    UpdateShipProfileRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.UpdateShipProfile(shipId, request);
    await NotifySnapshotChanged(hub, snapshot, "ShipProfileUpdated");
    return Results.Ok(snapshot);
})
    .WithName("UpdateShipProfile")
    .WithTags("Ships")
    .WithSummary("Updates editable ship profile, position, and equipment fields.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/ships/{shipId:guid}/duplicate", async (
    Guid shipId,
    DuplicateShipRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.DuplicateShip(shipId, request);
    await NotifySnapshotChanged(hub, snapshot, "ShipDuplicated");
    return Results.Ok(snapshot);
})
    .WithName("DuplicateShip")
    .WithTags("Ships")
    .WithSummary("Duplicates an owned ship record.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/turns/current/orders/commit", async (
    Guid matchId,
    CommitOrderRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.CommitOrder(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "OrderLockStatusChanged");
    return Results.Ok(snapshot);
})
    .WithName("CommitMovementOrder")
    .WithTags("Orders")
    .WithSummary("Commits a hidden movement order by salted hash.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/fighters/move", async (
    Guid matchId,
    MoveFighterGroupRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.MoveFighterGroup(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "FighterGroupMoved");
    return Results.Ok(snapshot);
})
    .WithName("MoveFighterGroup")
    .WithTags("Fighters")
    .WithSummary("Flies a fighter group up to its move allowance in any direction.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/turns/current/orders/complete", async (
    Guid matchId,
    DeclareOrdersCompleteRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.DeclareOrdersComplete(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "OrdersDeclaredComplete");
    return Results.Ok(snapshot);
})
    .WithName("DeclareOrdersComplete")
    .WithTags("Orders")
    .WithSummary("Declares a participant has finished plotting; unordered ships hold course and speed.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/turns/current/orders/reveal", async (
    Guid matchId,
    RevealOrderRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.RevealOrder(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "OrdersRevealed");
    return Results.Ok(snapshot);
})
    .WithName("RevealMovementOrder")
    .WithTags("Orders")
    .WithSummary("Reveals and verifies a committed movement order.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/turns/current/fire", async (
    Guid matchId,
    FireWeaponRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.FireWeapon(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "WeaponFired");
    return Results.Ok(snapshot);
})
    .WithName("FireWeapon")
    .WithTags("Combat")
    .WithSummary("Resolves one weapon attack during the firing phase.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/turns/current/cease-fire", async (
    Guid matchId,
    CeaseFireRequest request,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.CeaseFire(matchId, request);
    await NotifySnapshotChanged(hub, snapshot, "FireCompleted");
    return Results.Ok(snapshot);
})
    .WithName("CeaseFire")
    .WithTags("Combat")
    .WithSummary("Ends a ship's fire for the turn and rolls the threshold checks it earned.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/api/matches/{matchId:guid}/turns/current/advance", async (
    Guid matchId,
    HttpRequest http,
    IMatchService matches,
    IHubContext<MatchHub> hub) =>
{
    var snapshot = matches.AdvanceTurn(matchId, ReadParticipantToken(http));
    await NotifySnapshotChanged(hub, snapshot, "TurnAdvanced");
    return Results.Ok(snapshot);
})
    .WithName("AdvanceTurn")
    .WithTags("Turns")
    .WithSummary("Advances the match phase or starts the next turn.")
    .Produces<MatchSnapshotDto>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status403Forbidden);

app.Run();

static string ReadParticipantToken(HttpRequest request)
{
    // A header that is present but empty is the same as no header at all: the service would
    // otherwise be asked to match a blank token against its participants.
    if (request.Headers.TryGetValue("X-Participant-Token", out var value))
    {
        var token = value.ToString();
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }
    }

    throw new UnauthorizedAccessException("X-Participant-Token is required.");
}

static Task NotifySnapshotChanged(IHubContext<MatchHub> hub, MatchSnapshotDto snapshot, string reason) =>
    hub.Clients.Group(snapshot.MatchId.ToString()).SendAsync("MatchSnapshotChanged", snapshot.MatchId, snapshot.Version, reason);

static Task WriteProblem(HttpContext context, int status, string title, string detail)
{
    context.Response.StatusCode = status;
    return Results.Problem(detail, statusCode: status, title: title).ExecuteAsync(context);
}

static CorsOriginSettings ReadAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
{
    var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? SplitOrigins(configuration["FORCESIGNAL_CORS_ALLOWED_ORIGINS"])
        ?? SplitOrigins(configuration["Cors__AllowedOrigins"]);

    if (configured is { Length: > 0 })
    {
        var origins = configured
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (origins.Length > 0)
        {
            return new CorsOriginSettings(false, origins);
        }
    }

    if (environment.IsDevelopment())
    {
        return new CorsOriginSettings(true, []);
    }

    throw new InvalidOperationException("Cors:AllowedOrigins must be configured outside Development.");
}

static string[]? SplitOrigins(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? null
        : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

static string[] ReadDeploymentWarnings(
    IConfiguration configuration,
    IHostEnvironment environment,
    CorsOriginSettings allowedOrigins,
    FeatureFlags features)
{
    // The in-memory persistence warning is reported in every environment so readiness never
    // implies durable match storage.
    var warnings = new List<string>
    {
        "Matches are currently stored in memory and will be lost on API restart.",
    };

    // An in-progress engine being switched on is worth saying out loud, because the reason to
    // check readiness before a game is to find out what is about to be in the way.
    if (features.StarGrunt)
    {
        warnings.Add("StarGrunt ground combat is enabled and is still in development.");
    }

    if (features.Dirtside)
    {
        warnings.Add("Dirtside ground combat is enabled and is still in development.");
    }

    if (environment.IsDevelopment())
    {
        return [.. warnings];
    }

    if (allowedOrigins.Origins.Any(IsLocalOrigin))
    {
        warnings.Add("CORS includes a localhost origin; replace it before public deployment.");
    }

    return [.. warnings];
}

static bool IsLocalOrigin(string origin) =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase));

internal sealed record CorsOriginSettings(bool AllowAnyOrigin, string[] Origins);

/// <summary>
/// Adapts the host's configuration to the one-method view the Application layer asks for, so that
/// layer does not take a dependency on the hosting configuration stack to read two booleans.
/// </summary>
internal sealed class ConfigurationFeatureSource(IConfiguration configuration) : FeatureFlags.IConfigurationSource
{
    public string? GetValue(string key) => configuration[key];
}

/// <summary>
/// Wraps a request body so it cannot deliver more than the caller said it would. Content-Length is
/// a claim the client makes, not a fact, so an upload that keeps going past the ceiling is cut off
/// here rather than being read to its end.
/// </summary>
internal sealed class BoundedStream(Stream inner, long maxBytes) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Track(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Track(await inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Track(int count)
    {
        _read += count;
        if (_read > maxBytes)
        {
            throw new InvalidOperationException(
                $"That snapshot is larger than the {maxBytes / (1024 * 1024)}MB a match file can be.");
        }

        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Provides a public marker type for API integration test hosting.
/// </summary>
public partial class Program;
