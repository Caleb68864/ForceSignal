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
/// <para>
/// <b>Which is exactly why the settings field is optional rather than a version bump.</b> A
/// mismatched format is skipped, not migrated, so a required new field would silently retire every
/// stored ground game on the machine - the operator would learn about it as a table asking where
/// their game went. An optional field costs nothing: a row written before it simply reads back
/// without it, and the service falls back the same way a create request that carries no settings
/// does. Anything genuinely unreadable still goes through <c>SkippedSave</c>, which the host logs at
/// startup, so no stored game disappears in silence.
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
    /// <param name="settings">
    /// What this engine's service knows about the game beyond the module's document and the token,
    /// as JSON, or null when it knows nothing extra. Dirtside stores the chit pot the players
    /// counted and their die tables here; StarGrunt stores the range table its players entered, and
    /// nothing at all when they entered none. A row of either engine written before those became the
    /// players' carries nothing. Opaque on purpose - the wrapper is shared by both engines and has no
    /// business knowing what is in it.
    /// </param>
    /// <returns>The row to store.</returns>
    public static string Wrap(string token, DateTimeOffset lastActivity, string game, string? settings = null)
    {
        using var document = JsonDocument.Parse(game);
        using var settingsDocument = settings is null ? null : JsonDocument.Parse(settings);
        return JsonSerializer.Serialize(
            new Stored(
                FormatVersion,
                token,
                lastActivity,
                document.RootElement.Clone(),
                settingsDocument?.RootElement.Clone()),
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

        // A row from before the settings field, or one from an engine that stores none, reads back
        // as an absent element rather than as a null: JsonElement is a struct, so "not there" and
        // "there and null" both have to be turned into the same nothing here.
        var settings = stored.Settings is { ValueKind: JsonValueKind.Object or JsonValueKind.Array } present
            ? present.GetRawText()
            : null;

        return new Unwrapped(stored.Token, stored.LastActivity, stored.Game.GetRawText(), settings);
    }

    /// <summary>A row taken apart.</summary>
    /// <param name="Token">The game token.</param>
    /// <param name="LastActivity">When the game was last touched.</param>
    /// <param name="Game">The game, for the module to restore.</param>
    /// <param name="Settings">The service's engine-specific settings as JSON, or null when the row carries none.</param>
    public readonly record struct Unwrapped(
        string Token,
        DateTimeOffset LastActivity,
        string Game,
        string? Settings = null);

    /// <summary>The row's shape.</summary>
    private sealed record Stored(
        int FormatVersion,
        string Token,
        DateTimeOffset LastActivity,
        [property: JsonPropertyName("game")] JsonElement Game,
        [property: JsonPropertyName("settings")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JsonElement? Settings = null);
}
