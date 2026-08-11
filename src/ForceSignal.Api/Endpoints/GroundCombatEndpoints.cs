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

        app.MapPost("/api/stargrunt/games/{gameId:guid}/confidence-tests", (
            Guid gameId,
            StarGruntConfidenceTestRequest request,
            IStarGruntGameService games) => Results.Ok(games.TakeConfidenceTest(gameId, request)))
            .WithName("TakeStarGruntConfidenceTest")
            .WithTags("StarGrunt")
            .WithSummary("Puts a unit's nerve to the test.")
            .WithDescription("Not an action and not tied to an activation: a test is taken the moment something happens, to whichever unit it happened to. The threat level comes off the player's own table.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/rally", (
            Guid gameId,
            StarGruntRallyRequest request,
            IStarGruntGameService games) => Results.Ok(games.Rally(gameId, request)))
            .WithName("RallyStarGruntUnit")
            .WithTags("StarGrunt")
            .WithSummary("Spends a command element's action steadying a subordinate.")
            .WithDescription("The action belongs to the rallying unit and the roll belongs to the rallied one, against both leadership values added together.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/reorganise", (
            Guid gameId,
            StarGruntUnitActionRequest request,
            IStarGruntGameService games) => Results.Ok(games.Reorganise(gameId, request)))
            .WithName("ReorganiseStarGruntUnit")
            .WithTags("StarGrunt")
            .WithSummary("Spends an action putting a scattered unit back in order.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/units/disorganised", (
            Guid gameId,
            StarGruntDisorganisedRequest request,
            IStarGruntGameService games) => Results.Ok(games.SetDisorganised(gameId, request)))
            .WithName("SetStarGruntDisorganised")
            .WithTags("StarGrunt")
            .WithSummary("Declares whether a unit has scattered out of integrity.")
            .WithDescription("Integrity is measured with a ruler at the table, so this is declared rather than computed.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/reaction-tests", (
            Guid gameId,
            StarGruntReactionTestRequest request,
            IStarGruntGameService games) => Results.Ok(games.TakeReactionTest(gameId, request)))
            .WithName("TakeStarGruntReactionTest")
            .WithTags("StarGrunt")
            .WithSummary("Rolls to see whether troops have the nerve for a risky order.")
            .WithDescription("The same roll as a confidence test, with one difference: failing costs the action and never a confidence level. Passing spends nothing by itself.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/units/leaves-cover", (
            Guid gameId,
            StarGruntLeavesCoverRequest request,
            IStarGruntGameService games) => Results.Ok(games.SetLeavesCover(gameId, request)))
            .WithName("SetStarGruntLeavesCover")
            .WithTags("StarGrunt")
            .WithSummary("Declares that a unit's next move would take it out of cover.")
            .WithDescription("Whether a move counts as leaving cover is an eyeball judgement at a table, so it is declared rather than computed.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/charge", (
            Guid gameId,
            StarGruntChargeRequest request,
            IStarGruntGameService games) => Results.Ok(games.DeclareCharge(gameId, request)))
            .WithName("DeclareStarGruntCharge")
            .WithTags("StarGrunt")
            .WithSummary("Declares a close assault and rolls the attacker's nerve to make it.")
            .WithDescription("The threat level is supplied, off the player's own table. What is enforced here is the rule beside it: a unit that has already lost its nerve will not charge at all.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/assaults/stand", (
            Guid gameId,
            StarGruntStandRequest request,
            IStarGruntGameService games) => Results.Ok(games.DefenderStands(gameId, request)))
            .WithName("StarGruntDefenderStands")
            .WithTags("StarGrunt")
            .WithSummary("Rolls the defender's nerve to stand and receive a charge.")
            .WithDescription("The threat comes from the odds, counted off the roster with power armour worth two men, and terror doubles it.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/assaults/melee", (
            Guid gameId,
            StarGruntMeleeRequest request,
            IStarGruntGameService games) => Results.Ok(games.FightMelee(gameId, request)))
            .WithName("FightStarGruntMelee")
            .WithTags("StarGrunt")
            .WithSummary("Fights one round of melee, one exchange per pairing.")
            .WithDescription("Who fights whom is sent in rather than worked out: the attacker pairs off one figure per defender and the defender allocates the leftovers, which is a decision between two people.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/stargrunt/games/{gameId:guid}/assaults/downed", (
            Guid gameId,
            StarGruntSettleDownedRequest request,
            IStarGruntGameService games) => Results.Ok(games.SettleTheDowned(gameId, request)))
            .WithName("SettleStarGruntDowned")
            .WithTags("StarGrunt")
            .WithSummary("Rolls what became of the figures a unit had downed.")
            .WithDescription("Rolled once the assault is over, because a stunned man gets up again on the winning side and is taken on the losing one. The bands that read the roll are supplied.")
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
