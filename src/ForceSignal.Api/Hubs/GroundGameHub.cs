using ForceSignal.Application.Features;
using ForceSignal.Application.Ground;
using Microsoft.AspNetCore.SignalR;

namespace ForceSignal.Api.Hubs;

/// <summary>
/// Where a device follows a ground game it holds the code for, so a second device at the table sees
/// every change as it is made.
/// </summary>
/// <remarks>
/// <para>
/// A ground game has no seats: it is one table, and its token is the whole credential, minted on
/// create and handed to another device only inside the share code. So following a game takes the
/// token, exactly as every route does, and a device that follows one can do everything the first
/// can - which is what one table on two devices means. The token is 256 random bits; there is
/// nothing shorter to guess.
/// </para>
/// <para>
/// Its own hub rather than another method on <see cref="MatchHub"/>, so presence and the Full Thrust
/// session it keeps per connection are untouched, and a connection may follow one ground game. The
/// pushes are sent by <c>GameTokenFilter</c> after any route that changed the game answers.
/// </para>
/// </remarks>
public sealed partial class GroundGameHub(IServiceProvider services, FeatureFlags features, ILogger<GroundGameHub> logger) : Hub
{
    private const string FollowingKey = "forcesignal.ground";

    /// <summary>The name a game's followers are grouped under.</summary>
    /// <param name="engine">"dirtside" or "stargrunt".</param>
    /// <param name="gameId">The game.</param>
    public static string GroupFor(string engine, Guid gameId) => $"{engine}:{gameId}";

    /// <summary>Follows a ground game, given its code. Following another game stops following the last.</summary>
    /// <param name="engine">"dirtside" or "stargrunt".</param>
    /// <param name="gameId">The game.</param>
    /// <param name="token">The game's token, out of the share code.</param>
    public async Task JoinGroundGame(string engine, string gameId, string token)
    {
        if (!Guid.TryParse(gameId, out var parsed) || Service(engine) is not { } games || !Holds(games, parsed, token))
        {
            LogJoinRefused(logger, Context.ConnectionId, engine, gameId);
            throw new HubException("That game code does not open a game on this server.");
        }

        var group = GroupFor(engine, parsed);
        if (Context.Items.TryGetValue(FollowingKey, out var previous) && previous is string previousGroup && previousGroup != group)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, previousGroup);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        Context.Items[FollowingKey] = group;
        LogJoined(logger, Context.ConnectionId, engine, parsed);
    }

    private IGroundGameService? Service(string engine) => engine switch
    {
        "dirtside" when features.Dirtside => services.GetRequiredService<IDirtsideGameService>(),
        "stargrunt" when features.StarGrunt => services.GetRequiredService<IStarGruntGameService>(),
        _ => null,
    };

    private static bool Holds(IGroundGameService games, Guid gameId, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            games.RequireToken(gameId, token);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            // NotFoundException derives from InvalidOperationException: an unknown game and a wrong
            // token are refused alike, so the refusal says nothing about which ids exist.
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hub connection {ConnectionId} refused for {Engine} game {GameId}: not a code for it")]
    private static partial void LogJoinRefused(ILogger logger, string connectionId, string engine, string gameId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hub connection {ConnectionId} is following {Engine} game {GameId}")]
    private static partial void LogJoined(ILogger logger, string connectionId, string engine, Guid gameId);
}
