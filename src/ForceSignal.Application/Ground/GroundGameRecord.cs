using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForceSignal.Application.Ground;

/// <summary>
/// The row a ground game is stored as: the game the module serializes, wrapped with what the
/// service knows about it and the module does not.
/// </summary>
/// <remarks>
/// <para>
/// The module writes a game out as a value with no idea who is allowed to touch it or when it was
/// last touched; both of those belong to the service, and both have to survive a restart. The token
/// especially: a restart that minted a new one would lock every device at the table out of a game
/// that is otherwise still there. So the service wraps the module's document rather than asking the
/// module to carry fields it has no use for.
/// </para>
/// <para>
/// Versioned like the Full Thrust record, and for the same reason. A row that predates the wrapper
/// is skipped rather than guessed at: reading it would mean inventing a token nobody holds, which
/// is a game that exists and cannot be played - no better than one that is gone, and harder to
/// explain.
/// </para>
/// </remarks>
internal static class GroundGameRecord
{
    /// <summary>The format written into every row.</summary>
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Wraps a serialized game with the service's own state.</summary>
    /// <param name="token">The token a caller has to present to touch the game.</param>
    /// <param name="lastActivity">When the game was last read or changed.</param>
    /// <param name="game">The game, as the module wrote it.</param>
    /// <returns>The row to store.</returns>
    public static string Wrap(string token, DateTimeOffset lastActivity, string game)
    {
        using var document = JsonDocument.Parse(game);
        return JsonSerializer.Serialize(
            new Stored(FormatVersion, token, lastActivity, document.RootElement.Clone()),
            Json);
    }

    /// <summary>Reads a row back into its parts.</summary>
    /// <param name="state">The stored row.</param>
    /// <returns>The token, the last activity and the game as the module wrote it.</returns>
    /// <exception cref="InvalidOperationException">The row is not one this version wrote.</exception>
    /// <exception cref="JsonException">The row is not JSON.</exception>
    public static Unwrapped Unwrap(string state)
    {
        var stored = JsonSerializer.Deserialize<Stored>(state, Json)
            ?? throw new InvalidOperationException("The row is empty.");

        if (stored.FormatVersion != FormatVersion)
        {
            throw new InvalidOperationException(
                $"The row is format {stored.FormatVersion}; this version reads {FormatVersion}.");
        }

        if (string.IsNullOrWhiteSpace(stored.Token))
        {
            throw new InvalidOperationException("The row carries no game token.");
        }

        if (stored.Game.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The row holds no game.");
        }

        return new Unwrapped(stored.Token, stored.LastActivity, stored.Game.GetRawText());
    }

    /// <summary>A row taken apart.</summary>
    /// <param name="Token">The game token.</param>
    /// <param name="LastActivity">When the game was last touched.</param>
    /// <param name="Game">The game, for the module to restore.</param>
    public readonly record struct Unwrapped(string Token, DateTimeOffset LastActivity, string Game);

    /// <summary>The row's shape.</summary>
    private sealed record Stored(
        int FormatVersion,
        string Token,
        DateTimeOffset LastActivity,
        [property: JsonPropertyName("game")] JsonElement Game);
}
