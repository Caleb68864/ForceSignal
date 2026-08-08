using ForceSignal.Application.Features;

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
