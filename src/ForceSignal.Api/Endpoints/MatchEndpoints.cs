using ForceSignal.Api.Hubs;
using ForceSignal.Application;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForceSignal.Api.Endpoints;

/// <summary>
/// Where the Full Thrust match, fleet, ship and ordnance routes are mounted.
/// </summary>
/// <remarks>
/// <para>
/// Always mapped, unlike the ground engines beside it: this is the game the server exists for, and
/// there is no flag that turns it off. Moved out of <c>Program.cs</c> so that file is wiring and
/// middleware and nothing else - it had grown to a thousand lines with thirty-four routes inline,
/// and the ground engines had already shown where routes belong.
/// </para>
/// <para>
/// Every mutating route broadcasts that the snapshot changed, after the service call returns and
/// therefore outside its lock. The two read-only routes - the firing solution and the order
/// preview - deliberately do not, because a player working something out would otherwise wake every
/// device at the table on every adjustment.
/// </para>
/// </remarks>
public static class MatchEndpoints
{
    // A snapshot of a real match is tens of kilobytes. The ceiling is far above that and far below
    // anything that would strain the machine, so a mistaken upload fails fast instead of being read
    // into memory in full.
    private const long MaxRestoreBodyBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions RestoreJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        // A snapshot is four or five objects deep. A file nested far past that is not a snapshot
        // someone saved, and parsing it would recurse as far as it was told to.
        MaxDepth = 32,
    };

    /// <summary>Maps the Full Thrust routes.</summary>
    /// <param name="app">Route builder to map onto.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/matches", (CreateMatchRequest request, IMatchService matches) =>
            Results.Ok(matches.CreateMatch(request)))
            .WithName("CreateMatch")
            .WithTags("Matches")
            .WithSummary("Creates a new match and owner participant session.")
            .RequireRateLimiting(RateLimitPolicies.MatchCreate)
            .Produces<MatchCreatedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/matches/join", (JoinMatchRequest request, IMatchService matches) =>
            Results.Ok(matches.JoinMatch(request)))
            .WithName("JoinMatch")
            .WithTags("Matches")
            .WithSummary("Joins a match by room code.")
            .RequireRateLimiting(RateLimitPolicies.RoomCodeLookup)
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
            .RequireRateLimiting(RateLimitPolicies.Restore)
            .Produces<MatchRestoredResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/matches/by-code/{joinCode}", (string joinCode, IMatchService matches) =>
            Results.Ok(matches.FindMatchByCode(joinCode)))
            .WithName("GetMatchByCode")
            .WithTags("Matches")
            .WithSummary("Resolves a room code to a match id and whether seats are still claimable.")
            .RequireRateLimiting(RateLimitPolicies.RoomCodeLookup)
            .Produces<MatchIdentityDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // The room code stands in for a token here, because a player coming back to a restored match has
        // no token yet - that is what they are here to get. Handing seat ids to anyone who knows the match
        // id is what made a seat takeover possible, since the id is not a secret.
        app.MapGet("/api/matches/{matchId:guid}/seats", (Guid matchId, string joinCode, IMatchService matches) =>
            Results.Ok(matches.GetSeats(matchId, joinCode)))
            .WithName("GetMatchSeats")
            .WithTags("Matches")
            .WithSummary("Lists claimable seats in a restored match. Requires the room code.")
            .RequireRateLimiting(RateLimitPolicies.RoomCodeLookup)
            .Produces<IReadOnlyList<MatchSeatDto>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
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
            // Authenticated by room code like the lookups above, so it shares their budget: it was the one
            // code-guarded route a guesser could hammer freely.
            .RequireRateLimiting(RateLimitPolicies.RoomCodeLookup)
            .Produces<MatchJoinedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // The snapshot is the whole game: every ship's position and damage, every shot and its dice, the
        // battle log, and who is playing. It is only for the people at the table, and the match id is not a
        // secret - the room-code lookup hands it out and every notification echoes it.
        app.MapGet("/api/matches/{matchId:guid}/snapshot", (Guid matchId, HttpRequest http, IMatchService matches) =>
        {
            var token = ReadParticipantToken(http);

            // A match that is gone is said to be gone, before the token is judged. The id is not a secret,
            // and a device holding a session from before a restart needs the 404 to know to let go of it;
            // a 403 would tell it the token was wrong, which is a different problem with a different cure.
            if (!matches.MatchExists(matchId))
            {
                throw new NotFoundException("Match was not found.");
            }

            if (!matches.IsMatchParticipant(matchId, token))
            {
                throw new UnauthorizedAccessException("Only a player in this match can read its state.");
            }

            return Results.Ok(matches.GetSnapshot(matchId));
        })
            .WithName("GetMatchSnapshot")
            .WithTags("Matches")
            .WithSummary("Gets the authoritative match snapshot. Requires a participant token.")
            .Produces<MatchSnapshotDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
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

        app.MapPost("/api/matches/{matchId:guid}/rules-profile", async (
            Guid matchId,
            UpdateRulesProfileRequest request,
            IMatchService matches,
            IHubContext<MatchHub> hub) =>
        {
            var snapshot = matches.UpdateRulesProfile(matchId, request);
            await NotifySnapshotChanged(hub, snapshot, "RulesProfileChanged");
            return Results.Ok(snapshot);
        })
            .WithName("UpdateRulesProfile")
            .WithTags("Matches")
            .WithSummary("Replaces the numbers the match is played against, during fleet setup.")
            .WithDescription("This app ships no rulebook numbers. A match is played against a profile its players fill in from their own rulebook and record cards, and it is refused until that profile is complete.")
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

        // Read-only like the order preview, and quiet for the same reason.
        app.MapPost("/api/matches/{matchId:guid}/turns/current/firing-solution", (
            Guid matchId,
            FiringSolutionRequest request,
            IMatchService matches) => Results.Ok(matches.GetFiringSolution(matchId, request)))
            .WithName("GetFiringSolution")
            .WithTags("Firing")
            .WithSummary("Answers whether a shot could be taken, and what it would need.")
            .WithDescription("Held to the same checks as firing, so a console cannot offer a shot the server would refuse. Reports the blocker rather than throwing it.")
            .Produces<FiringSolutionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Deliberately does not notify the hub: a preview changes nothing, and a player working out a
        // course would otherwise wake every other device at the table on every adjustment.
        app.MapPost("/api/matches/{matchId:guid}/turns/current/orders/preview", (
            Guid matchId,
            PreviewOrderRequest request,
            IMatchService matches) => Results.Ok(matches.PreviewOrder(matchId, request)))
            .WithName("PreviewMovementOrder")
            .WithTags("Orders")
            .WithSummary("Resolves a draft movement order without committing it.")
            .WithDescription("Answers with the legs, path, and endpoint the turn would actually be flown as, so a client needs no movement rules of its own. An illegal draft is described rather than refused.")
            .Produces<OrderPreviewDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
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
            .WithDescription("Send complete=false to take the declaration back, which is allowed while order entry is still open.")
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

        return app;
    }

    private static string ReadParticipantToken(HttpRequest request)
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

    private static Task NotifySnapshotChanged(IHubContext<MatchHub> hub, MatchSnapshotDto snapshot, string reason) =>
        hub.Clients.Group(snapshot.MatchId.ToString()).SendAsync("MatchSnapshotChanged", snapshot.MatchId, snapshot.Version, reason);
}
