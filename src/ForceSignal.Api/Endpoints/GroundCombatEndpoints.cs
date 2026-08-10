using ForceSignal.Application.Features;
using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;

namespace ForceSignal.Api.Endpoints;

/// <summary>
/// Where the ground-combat engines mount their routes.
/// </summary>
/// <remarks>
/// These are only called when the matching flag in <see cref="FeatureFlags"/> is on, so a disabled
/// engine contributes no routes at all - its paths 404 exactly as if the code had never been
/// written. That is deliberate: an engine that is half-built should be absent rather than present
/// and broken, because the failure mode that matters is one of these interfering with a Full
/// Thrust game someone actually turned up to play.
/// </remarks>
public static class GroundCombatEndpoints
{
    /// <summary>Maps the infantry-scale StarGrunt routes.</summary>
    /// <param name="app">Route builder to map onto.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapStarGruntEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/stargrunt/status", () => Results.Ok(new { engine = "StarGrunt", status = "in-development" }))
            .WithName("GetStarGruntStatus")
            .WithTags("StarGrunt")
            .WithSummary("Reports that the StarGrunt engine is mounted on this server.");

        app.MapPost("/api/stargrunt/games", (CreateStarGruntGameRequest request, IStarGruntGameService games) =>
            Results.Ok(games.CreateGame(request)))
            .WithName("CreateStarGruntGame")
            .WithTags("StarGrunt")
            .WithSummary("Starts a game.")
            .Produces<StarGruntGameCreatedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/stargrunt/games/{gameId:guid}", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.GetSnapshot(gameId)))
            .WithName("GetStarGruntGame")
            .WithTags("StarGrunt")
            .WithSummary("Reads a game.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/units", (
            Guid gameId,
            AddStarGruntUnitRequest request,
            IStarGruntGameService games) => Results.Ok(games.AddUnit(gameId, request)))
            .WithName("AddStarGruntUnit")
            .WithTags("StarGrunt")
            .WithSummary("Puts a unit on the table.")
            .WithDescription("Every die is a face count the user typed off their own record card. This engine ships no stats.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/begin", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.BeginTurn(gameId)))
            .WithName("BeginStarGruntTurn")
            .WithTags("StarGrunt")
            .WithSummary("Opens the next turn.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/current/first-activator", (
            Guid gameId,
            ChooseFirstActivatorRequest request,
            IStarGruntGameService games) => Results.Ok(games.ChooseFirstActivator(gameId, request)))
            .WithName("ChooseStarGruntFirstActivator")
            .WithTags("StarGrunt")
            .WithSummary("Settles who takes the first activation this turn.")
            .WithDescription("The side with fewer units on the table has the choice, and may take it or give it away.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations", (
            Guid gameId,
            BeginStarGruntActivationRequest request,
            IStarGruntGameService games) => Results.Ok(games.BeginActivation(gameId, request)))
            .WithName("BeginStarGruntActivation")
            .WithTags("StarGrunt")
            .WithSummary("Opens an activation.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/steps", (
            Guid gameId,
            StarGruntStepRequest request,
            IStarGruntGameService games) => Results.Ok(games.TakeStep(gameId, request)))
            .WithName("TakeStarGruntStep")
            .WithTags("StarGrunt")
            .WithSummary("Spends an action on something other than shooting.")
            .WithDescription("Firing has a route of its own, because it has to name the weapon it spends.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/fire", (
            Guid gameId,
            StarGruntFireRequest request,
            IStarGruntGameService games) => Results.Ok(games.Fire(gameId, request)))
            .WithName("FireStarGruntWeapon")
            .WithTags("StarGrunt")
            .WithSummary("Fires one weapon at another unit.")
            .WithDescription("Range and cover are declared by the players, as they are at a table. Nothing here computes line of sight.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/remove-suppression", (
            Guid gameId,
            StarGruntUnitActionRequest request,
            IStarGruntGameService games) => Results.Ok(games.RemoveSuppression(gameId, request)))
            .WithName("RemoveStarGruntSuppression")
            .WithTags("StarGrunt")
            .WithSummary("Spends an action trying to get a pinned unit's head back up.")
            .WithDescription("One action, one roll, one marker at best - the unit's quality die must exceed its leadership value. The action is spent whether or not it works.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/end", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.EndActivation(gameId)))
            .WithName("EndStarGruntActivation")
            .WithTags("StarGrunt")
            .WithSummary("Closes the open activation.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/current/pass", (
            Guid gameId,
            StarGruntPassRequest request,
            IStarGruntGameService games) => Results.Ok(games.Pass(gameId, request)))
            .WithName("PassStarGruntActivation")
            .WithTags("StarGrunt")
            .WithSummary("Declines to activate anything.")
            .WithDescription("Legal only while you have fewer unactivated units than your opponent.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/current/end", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.EndTurn(gameId)))
            .WithName("EndStarGruntTurn")
            .WithTags("StarGrunt")
            .WithSummary("Ends the turn.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    /// <summary>Maps the vehicle-scale Dirtside routes.</summary>
    /// <param name="app">Route builder to map onto.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDirtsideEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/dirtside/status", () => Results.Ok(new { engine = "Dirtside", status = "in-development" }))
            .WithName("GetDirtsideStatus")
            .WithTags("Dirtside")
            .WithSummary("Reports that the Dirtside engine is mounted on this server.");

        return app;
    }
}
