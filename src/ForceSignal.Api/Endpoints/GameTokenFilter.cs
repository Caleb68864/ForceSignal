using ForceSignal.Application.Ground;

namespace ForceSignal.Api.Endpoints;

/// <summary>
/// Guards a ground-combat route with the game's token.
/// </summary>
/// <remarks>
/// <para>
/// A ground game used to be reachable by anyone holding its id, and the id is a bearer capability
/// that turns up in URLs, proxy logs and browser history. The token is minted when the game is
/// created, handed back once, and has to come in the <c>X-Game-Token</c> header on everything
/// after - every read as well as every write, because the snapshot is the whole game.
/// </para>
/// <para>
/// A header that is missing or blank is a 401: the caller has not tried to prove anything, and the
/// answer says what was expected. A header that is present and wrong is a 403, through the same
/// exception the Full Thrust side uses for a wrong participant token. A game that does not exist
/// is a 404 before either, because the id is not a secret and there is nothing to protect.
/// </para>
/// <para>
/// One class for both engines, because the check is the same and the services share the surface it
/// needs. The service is resolved per request rather than captured at map time, so a test that
/// swaps one in still gets the filter.
/// </para>
/// </remarks>
/// <typeparam name="TService">Which engine's service to ask.</typeparam>
internal sealed class GameTokenFilter<TService> : IEndpointFilter
    where TService : IGroundGameService
{
    /// <summary>The header the token travels in.</summary>
    public const string HeaderName = "X-Game-Token";

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var token = http.Request.Headers.TryGetValue(HeaderName, out var values) ? values.ToString() : string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                $"{HeaderName} is required.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Game token required"));
        }

        // The route constraint has already proved this is a Guid; a route without one has no
        // business carrying this filter, and says so loudly rather than letting a request through.
        var gameId = Guid.Parse(http.GetRouteValue("gameId")?.ToString()
            ?? throw new InvalidOperationException("A route guarded by a game token must carry a gameId."));

        http.RequestServices.GetRequiredService<TService>().RequireToken(gameId, token);
        return next(context);
    }
}

/// <summary>Where the token guard is attached to a route.</summary>
internal static class GameTokenRouteExtensions
{
    /// <summary>
    /// Requires the game's token on this route, and documents the two ways it can be refused.
    /// </summary>
    /// <typeparam name="TService">Which engine's service checks it.</typeparam>
    /// <param name="builder">The route.</param>
    /// <returns>The same route, for chaining.</returns>
    public static RouteHandlerBuilder RequireGameToken<TService>(this RouteHandlerBuilder builder)
        where TService : IGroundGameService
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddEndpointFilter(new GameTokenFilter<TService>())
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
