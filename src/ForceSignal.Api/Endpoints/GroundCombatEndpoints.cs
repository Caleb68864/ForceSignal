using ForceSignal.Application.Features;
using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;

namespace ForceSignal.Api.Endpoints;

/// <summary>
/// Where the ground-combat engines mount their routes.
/// </summary>
/// <remarks>
/// <para>
/// These are only called when the matching flag in <see cref="FeatureFlags"/> is on, so a disabled
/// engine contributes no routes at all - its paths 404 exactly as if the code had never been
/// written. That is deliberate: an engine that is half-built should be absent rather than present
/// and broken, because the failure mode that matters is one of these interfering with a Full
/// Thrust game someone actually turned up to play.
/// </para>
/// <para>
/// Every route that names a game requires that game's token, through <see cref="GameTokenFilter{TService}"/>.
/// Only the create routes are open, because that is where the token comes from. Being open with no
/// credentials to ask for is what makes a create route the one a stranger loops on, so both share
/// the budget that already guards opening a Full Thrust match.
/// </para>
/// <para>
/// There were two further open routes, <c>GET /api/stargrunt/status</c> and
/// <c>GET /api/dirtside/status</c>, and they are gone. Each returned a compile-time literal naming
/// the engine and a readiness word, so the only fact either could establish was that the route had
/// been mapped - which is to say that the flag was on, which <c>GET /api/features</c> answers from
/// the same <see cref="FeatureFlags"/> instance that decides the mapping. No client, script or
/// healthcheck ever called them; the app asks <c>/api/features</c>.
/// </para>
/// <para>
/// The readiness word is why they were removed rather than left dead. <c>"playable"</c> was a third
/// hand-written description of a capability that <c>/api/features</c> and the <c>/ready</c> warnings
/// already describe, and it had already drifted: <c>/ready</c> lists opportunity fire, area-defence
/// interception and indirect fire as unreachable in the same engine this route called playable. It
/// also could not tell a healthy engine from a degraded one - a Dirtside store that had fallen back
/// to memory returned the identical bytes - so it was capable of reassuring an operator about a
/// machine that was losing every game at the next restart.
/// </para>
/// </remarks>
public static class GroundCombatEndpoints
{
    /// <summary>Maps the infantry-scale StarGrunt routes.</summary>
    /// <param name="app">Route builder to map onto.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapStarGruntEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/stargrunt/games", (CreateStarGruntGameRequest request, IStarGruntGameService games) =>
            Results.Ok(games.CreateGame(request)))
            .WithName("CreateStarGruntGame")
            .WithTags("StarGrunt")
            .WithSummary("Starts a game and issues its token.")
            .WithDescription("The token comes back here and nowhere else. Every other route for the game requires it in the X-Game-Token header.")
            .RequireRateLimiting(RateLimitPolicies.MatchCreate)
            .Produces<StarGruntGameCreatedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/stargrunt/games/{gameId:guid}", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.GetSnapshot(gameId)))
            .WithName("GetStarGruntGame")
            .WithTags("StarGrunt")
            .WithSummary("Reads a game.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireGameToken<IStarGruntGameService>();

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
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/begin", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.BeginTurn(gameId)))
            .WithName("BeginStarGruntTurn")
            .WithTags("StarGrunt")
            .WithSummary("Opens the next turn.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/current/first-activator", (
            Guid gameId,
            ChooseFirstActivatorRequest request,
            IStarGruntGameService games) => Results.Ok(games.ChooseFirstActivator(gameId, request)))
            .WithName("ChooseStarGruntFirstActivator")
            .WithTags("StarGrunt")
            .WithSummary("Settles who takes the first activation this turn.")
            .WithDescription("The side with fewer units on the table has the choice, and may take it or give it away.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations", (
            Guid gameId,
            BeginStarGruntActivationRequest request,
            IStarGruntGameService games) => Results.Ok(games.BeginActivation(gameId, request)))
            .WithName("BeginStarGruntActivation")
            .WithTags("StarGrunt")
            .WithSummary("Opens an activation.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/steps", (
            Guid gameId,
            StarGruntStepRequest request,
            IStarGruntGameService games) => Results.Ok(games.TakeStep(gameId, request)))
            .WithName("TakeStarGruntStep")
            .WithTags("StarGrunt")
            .WithSummary("Spends an action on something other than shooting.")
            .WithDescription("Firing has a route of its own, because it has to name the weapon it spends.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/fire", (
            Guid gameId,
            StarGruntFireRequest request,
            IStarGruntGameService games) => Results.Ok(games.Fire(gameId, request)))
            .WithName("FireStarGruntWeapon")
            .WithTags("StarGrunt")
            .WithSummary("Fires one weapon at another unit.")
            .WithDescription("Range and cover are declared by the players, as they are at a table. Nothing here computes line of sight.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/remove-suppression", (
            Guid gameId,
            StarGruntUnitActionRequest request,
            IStarGruntGameService games) => Results.Ok(games.RemoveSuppression(gameId, request)))
            .WithName("RemoveStarGruntSuppression")
            .WithTags("StarGrunt")
            .WithSummary("Spends an action trying to get a pinned unit's head back up.")
            .WithDescription("One action, one roll, one marker at best - the unit's quality die must exceed its leadership value. The action is spent whether or not it works.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/confidence-tests", (
            Guid gameId,
            StarGruntConfidenceTestRequest request,
            IStarGruntGameService games) => Results.Ok(games.TakeConfidenceTest(gameId, request)))
            .WithName("TakeStarGruntConfidenceTest")
            .WithTags("StarGrunt")
            .WithSummary("Puts a unit's nerve to the test.")
            .WithDescription("Not an action and not tied to an activation: a test is taken the moment something happens, to whichever unit it happened to. The threat level comes off the player's own table.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/rally", (
            Guid gameId,
            StarGruntRallyRequest request,
            IStarGruntGameService games) => Results.Ok(games.Rally(gameId, request)))
            .WithName("RallyStarGruntUnit")
            .WithTags("StarGrunt")
            .WithSummary("Spends a command element's action steadying a subordinate.")
            .WithDescription("The action belongs to the rallying unit and the roll belongs to the rallied one, against both leadership values added together.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/reorganise", (
            Guid gameId,
            StarGruntUnitActionRequest request,
            IStarGruntGameService games) => Results.Ok(games.Reorganise(gameId, request)))
            .WithName("ReorganiseStarGruntUnit")
            .WithTags("StarGrunt")
            .WithSummary("Spends an action putting a scattered unit back in order.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/units/disorganised", (
            Guid gameId,
            StarGruntDisorganisedRequest request,
            IStarGruntGameService games) => Results.Ok(games.SetDisorganised(gameId, request)))
            .WithName("SetStarGruntDisorganised")
            .WithTags("StarGrunt")
            .WithSummary("Declares whether a unit has scattered out of integrity.")
            .WithDescription("Integrity is measured with a ruler at the table, so this is declared rather than computed.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/reaction-tests", (
            Guid gameId,
            StarGruntReactionTestRequest request,
            IStarGruntGameService games) => Results.Ok(games.TakeReactionTest(gameId, request)))
            .WithName("TakeStarGruntReactionTest")
            .WithTags("StarGrunt")
            .WithSummary("Rolls to see whether troops have the nerve for a risky order.")
            .WithDescription("The same roll as a confidence test, with one difference: failing costs the action and never a confidence level. Passing spends nothing by itself.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/units/leaves-cover", (
            Guid gameId,
            StarGruntLeavesCoverRequest request,
            IStarGruntGameService games) => Results.Ok(games.SetLeavesCover(gameId, request)))
            .WithName("SetStarGruntLeavesCover")
            .WithTags("StarGrunt")
            .WithSummary("Declares that a unit's next move would take it out of cover.")
            .WithDescription("Whether a move counts as leaving cover is an eyeball judgement at a table, so it is declared rather than computed.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/charge", (
            Guid gameId,
            StarGruntChargeRequest request,
            IStarGruntGameService games) => Results.Ok(games.DeclareCharge(gameId, request)))
            .WithName("DeclareStarGruntCharge")
            .WithTags("StarGrunt")
            .WithSummary("Declares a close assault and rolls the attacker's nerve to make it.")
            .WithDescription("The threat level is supplied, off the player's own table. What is enforced here is the rule beside it: a unit that has already lost its nerve will not charge at all.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/assaults/stand", (
            Guid gameId,
            StarGruntStandRequest request,
            IStarGruntGameService games) => Results.Ok(games.DefenderStands(gameId, request)))
            .WithName("StarGruntDefenderStands")
            .WithTags("StarGrunt")
            .WithSummary("Rolls the defender's nerve to stand and receive a charge.")
            .WithDescription("The threat comes from the odds, counted off the roster with power armour worth two men, and terror doubles it.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/assaults/melee", (
            Guid gameId,
            StarGruntMeleeRequest request,
            IStarGruntGameService games) => Results.Ok(games.FightMelee(gameId, request)))
            .WithName("FightStarGruntMelee")
            .WithTags("StarGrunt")
            .WithSummary("Fights one round of melee, one exchange per pairing.")
            .WithDescription("Who fights whom is sent in rather than worked out: the attacker pairs off one figure per defender and the defender allocates the leftovers, which is a decision between two people.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/assaults/downed", (
            Guid gameId,
            StarGruntSettleDownedRequest request,
            IStarGruntGameService games) => Results.Ok(games.SettleTheDowned(gameId, request)))
            .WithName("SettleStarGruntDowned")
            .WithTags("StarGrunt")
            .WithSummary("Rolls what became of the figures a unit had downed.")
            .WithDescription("Rolled once the assault is over, because a stunned man gets up again on the winning side and is taken on the losing one. The bands that read the roll are supplied.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/activations/current/end", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.EndActivation(gameId)))
            .WithName("EndStarGruntActivation")
            .WithTags("StarGrunt")
            .WithSummary("Closes the open activation.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/current/pass", (
            Guid gameId,
            StarGruntPassRequest request,
            IStarGruntGameService games) => Results.Ok(games.Pass(gameId, request)))
            .WithName("PassStarGruntActivation")
            .WithTags("StarGrunt")
            .WithSummary("Declines to activate anything.")
            .WithDescription("Legal only while you have fewer unactivated units than your opponent.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        app.MapPost("/api/stargrunt/games/{gameId:guid}/turns/current/end", (Guid gameId, IStarGruntGameService games) =>
            Results.Ok(games.EndTurn(gameId)))
            .WithName("EndStarGruntTurn")
            .WithTags("StarGrunt")
            .WithSummary("Ends the turn.")
            .Produces<StarGruntSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IStarGruntGameService>();

        return app;
    }

    /// <summary>Maps the vehicle-scale Dirtside routes.</summary>
    /// <param name="app">Route builder to map onto.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDirtsideEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/dirtside/games", (CreateDirtsideGameRequest request, IDirtsideGameService games) =>
            Results.Ok(games.CreateGame(request)))
            .WithName("CreateDirtsideGame")
            .WithTags("Dirtside")
            .WithSummary("Starts a game and issues its token.")
            .WithDescription("The token comes back here and nowhere else. Every other route for the game requires it in the X-Game-Token header.")
            .RequireRateLimiting(RateLimitPolicies.MatchCreate)
            .Produces<DirtsideGameCreatedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/dirtside/games/{gameId:guid}", (Guid gameId, IDirtsideGameService games) =>
            Results.Ok(games.GetSnapshot(gameId)))
            .WithName("GetDirtsideGame")
            .WithTags("Dirtside")
            .WithSummary("Reads a game without changing it.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/units", (
            Guid gameId,
            AddDirtsidePlatoonRequest request,
            IDirtsideGameService games) => Results.Ok(games.AddPlatoon(gameId, request)))
            .WithName("AddDirtsidePlatoon")
            .WithTags("Dirtside")
            .WithSummary("Puts a platoon on the table.")
            .WithDescription("Every number comes off the player's own record card. This app ships no stats.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/turns", (Guid gameId, IDirtsideGameService games) =>
            Results.Ok(games.BeginTurn(gameId)))
            .WithName("BeginDirtsideTurn")
            .WithTags("Dirtside")
            .WithSummary("Opens the next turn.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/turns/current/first-activator", (
            Guid gameId,
            ChooseDirtsideFirstActivatorRequest request,
            IDirtsideGameService games) => Results.Ok(games.ChooseFirstActivator(gameId, request)))
            .WithName("ChooseDirtsideFirstActivator")
            .WithTags("Dirtside")
            .WithSummary("Settles who takes the first activation this turn.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/activations", (
            Guid gameId,
            BeginDirtsideActivationRequest request,
            IDirtsideGameService games) => Results.Ok(games.BeginActivation(gameId, request)))
            .WithName("BeginDirtsideActivation")
            .WithTags("Dirtside")
            .WithSummary("Turns a platoon's marker over and starts its activation.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/activations/current/moves", (
            Guid gameId,
            MoveDirtsideElementRequest request,
            IDirtsideGameService games) => Results.Ok(games.MoveElement(gameId, request)))
            .WithName("MoveDirtsideElement")
            .WithTags("Dirtside")
            .WithSummary("Moves one element.")
            .WithDescription("Whether the move covered more than half its movement is measured with a tape at the table, so it is sent rather than worked out.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/activations/current/stand-down", (
            Guid gameId,
            DirtsideStandDownRequest request,
            IDirtsideGameService games) => Results.Ok(games.StandDown(gameId, request)))
            .WithName("DirtsideStandDown")
            .WithTags("Dirtside")
            .WithSummary("Declares that an element is sitting this activation out.")
            .WithDescription("A decision with teeth: an element that sits out has given up its go for the whole turn. The activation cannot close until every element has said what it is doing.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/activations/current/sensors", (
            Guid gameId,
            DirtsideSensorsRequest request,
            IDirtsideGameService games) => Results.Ok(games.SetAreaDefenceSensors(gameId, request)))
            .WithName("SetDirtsideAreaDefenceSensors")
            .WithTags("Dirtside")
            .WithSummary("Switches an element's area-defence sensors on or off.")
            .WithDescription("Spends the element's one combat action, which is the price of a standing reaction: live sensors let it intercept on anybody's activation for the rest of the turn, and the engine refuses an interception from an element that has not paid for one. Resolving an interception is not yet reachable from this API - readiness says so - so for now this records the capability rather than exercising it.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/activations/current/fire", (
            Guid gameId,
            DirtsideFireRequest request,
            IDirtsideGameService games) => Results.Ok(games.Fire(gameId, request)))
            .WithName("FireDirtsideWeapon")
            .WithTags("Dirtside")
            .WithSummary("Fires one element's weapon at one designated element.")
            .WithDescription("The declaration is binding: a shot at something an earlier shot destroyed is refused rather than re-pointed, because that is the cost of information the player did not have.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/activations/current/recover-systems", (
            Guid gameId,
            DirtsideRecoverSystemsRequest request,
            IDirtsideGameService games) => Results.Ok(games.RecoverSystems(gameId, request)))
            .WithName("RecoverDirtsideSystems")
            .WithTags("Dirtside")
            .WithSummary("Tries to get an element's Systems Down marker off, spending its combat action.")
            .WithDescription("Refused on the activation the marker went on; retryable on every one after, for as long as the game lasts. Backup systems bought at design time lower the number to reach.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/assaults/launch", (
            Guid gameId,
            LaunchDirtsideAssaultRequest request,
            IDirtsideGameService games) => Results.Ok(games.LaunchAssault(gameId, request)))
            .WithName("LaunchDirtsideAssault")
            .WithTags("Dirtside")
            .WithSummary("Orders the activated platoon in against a position, and rolls its nerve to go.")
            .WithDescription("Every committed element spends its combat action whether or not the troops go: a failed reaction test costs the action, never the nerve. A platoon whose confidence forbids assaulting is refused outright, without a die. The threat level and what the chits may count are the player's numbers.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/assaults/stand", (
            Guid gameId,
            DirtsideAssaultStandRequest request,
            IDirtsideGameService games) => Results.Ok(games.DefenderStands(gameId, request)))
            .WithName("DirtsideDefenderStands")
            .WithTags("Dirtside")
            .WithSummary("Rolls the assaulted platoon's nerve to stand and receive the assault, or give up the position.")
            .WithDescription("A confidence test, so giving way costs morale as well as the position. A defender whose nerve had already gone breaks without a test.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/assaults/round", (
            Guid gameId,
            IDirtsideGameService games) => Results.Ok(games.FightAssaultRound(gameId)))
            .WithName("FightDirtsideAssaultRound")
            .WithTags("Dirtside")
            .WithSummary("Fights one round of the open assault.")
            .WithDescription("Both sides draw before either loses anybody, and chits are never pooled. From the second round on the defender's cover has stopped counting.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/assaults/aftermath", (
            Guid gameId,
            DirtsideAssaultAftermathRequest request,
            IDirtsideGameService games) => Results.Ok(games.ResolveAssaultAftermath(gameId, request)))
            .WithName("ResolveDirtsideAssaultAftermath")
            .WithTags("Dirtside")
            .WithSummary("Takes the tests after a round: who, if anybody, has had enough.")
            .WithDescription("The defender tests first and a broken defender means the attacker is never asked. Each side tests at its own casualty share, against the two threat levels supplied. Whoever falls back comes away Under Fire.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/assaults/follow-through", (
            Guid gameId,
            DirtsideFollowThroughRequest request,
            IDirtsideGameService games) => Results.Ok(games.FollowThrough(gameId, request)))
            .WithName("DirtsideFollowThrough")
            .WithTags("Dirtside")
            .WithSummary("The winner's test to drive on through the position rather than stop on it.")
            .WithDescription("A pass hands every element its move and its combat action again, on the spot. A failure costs nothing but the opportunity. Ending the activation instead declines the test.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/activations/current/end", (
            Guid gameId,
            IDirtsideGameService games) => Results.Ok(games.EndActivation(gameId)))
            .WithName("EndDirtsideActivation")
            .WithTags("Dirtside")
            .WithSummary("Closes the open activation.")
            .WithDescription("Refused while any element still on the table has not said what it is doing, and the refusal names them. Refused too in the middle of an assault; the one stage it may walk away from is the follow-through.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/turns/current/pass", (
            Guid gameId,
            DirtsidePassRequest request,
            IDirtsideGameService games) => Results.Ok(games.Pass(gameId, request)))
            .WithName("PassDirtsideActivation")
            .WithTags("Dirtside")
            .WithSummary("Declines to activate anything.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        app.MapPost("/api/dirtside/games/{gameId:guid}/turns/current/end", (
            Guid gameId,
            IDirtsideGameService games) => Results.Ok(games.EndTurn(gameId)))
            .WithName("EndDirtsideTurn")
            .WithTags("Dirtside")
            .WithSummary("Closes the turn.")
            .WithDescription("Clears what only lasted the turn: an element that moved over half its movement has not done so next turn.")
            .Produces<DirtsideSnapshotDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireGameToken<IDirtsideGameService>();

        return app;
    }
}
