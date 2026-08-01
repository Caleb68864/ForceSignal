using ForceSignal.Api.Hubs;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;
using System.Text.Json;
using System.Text.Json.Serialization;

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

var app = builder.Build();
var RestoreJson = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
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
    catch (UnauthorizedAccessException ex)
    {
        await WriteProblem(context, StatusCodes.Status403Forbidden, "Action not allowed", ex.Message);
    }
    catch (InvalidOperationException ex)
    {
        var status = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        await WriteProblem(context, status, "Request could not be completed", ex.Message);
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.UseCors();

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
    warnings = ReadDeploymentWarnings(builder.Configuration, app.Environment, allowedOrigins)
}))
    .WithName("GetReadiness")
    .WithTags("Operations")
    .WithSummary("Reports API readiness and deployment-critical configuration state.")
    .Produces(StatusCodes.Status200OK);
app.MapHub<MatchHub>("/hubs/match");

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
    .Produces<MatchJoinedResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapPost("/api/matches/restore", async (HttpRequest http, IMatchService matches) =>
{
    // Parse the body here rather than binding JsonElement: model-binding failures surface as a
    // plain-text 400, and this endpoint must answer malformed files with problem+json.
    JsonElement body;
    try
    {
        body = await JsonSerializer.DeserializeAsync<JsonElement>(http.Body, RestoreJson);
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
    .Produces<MatchRestoredResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapGet("/api/matches/by-code/{joinCode}", (string joinCode, IMatchService matches) =>
    Results.Ok(matches.FindMatchByCode(joinCode)))
    .WithName("GetMatchByCode")
    .WithTags("Matches")
    .WithSummary("Resolves a room code to a match id and whether seats are still claimable.")
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
    return request.Headers.TryGetValue("X-Participant-Token", out var value)
        ? value.ToString()
        : throw new UnauthorizedAccessException("X-Participant-Token is required.");
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
    CorsOriginSettings allowedOrigins)
{
    // The in-memory persistence warning is reported in every environment so readiness never
    // implies durable match storage.
    var warnings = new List<string>
    {
        "Matches are currently stored in memory and will be lost on API restart.",
    };

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
/// Provides a public marker type for API integration test hosting.
/// </summary>
public partial class Program;
