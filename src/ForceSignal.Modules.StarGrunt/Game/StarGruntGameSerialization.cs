using System.Collections.Immutable;
using System.Text.Json;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>
/// Writing a game out and reading it back.
/// </summary>
/// <remarks>
/// <para>
/// The reason a game is a value. Because it is a record of immutable collections with no behaviour
/// hanging off it, restore fidelity is an equality assertion rather than a comparer somebody has to
/// remember to extend, and there is no hand-written companion object to drift from the type.
/// </para>
/// <para>
/// It goes through a surrogate rather than serializing the game directly, for one narrow reason:
/// the roster is a dictionary keyed by <see cref="UnitId"/>, and a record struct cannot be a JSON
/// property name. The alternative was a key converter in the shared layer, which would have changed
/// how sessions serialize everywhere to solve a problem only this type has. The surrogate keeps the
/// cost local - and the dictionaries it rebuilds compare structurally, so equality still holds.
/// </para>
/// </remarks>
public static class StarGruntGameSerialization
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
    };

    /// <summary>Writes a game out.</summary>
    /// <param name="game">The game to save.</param>
    /// <returns>The serialized game.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    public static string Save(StarGruntGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return JsonSerializer.Serialize(
            new Saved(
                game.Name,
                [.. game.Units.Values],
                [.. game.Statuses.Select(entry => new SavedStatus(entry.Key.Value, entry.Value))],
                game.Session,
                game.Log),
            Options);
    }

    /// <summary>Reads a game back.</summary>
    /// <param name="saved">A string produced by <see cref="Save"/>.</param>
    /// <returns>The restored game, equal to the one that was saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="saved"/> is blank or does not hold a game.</exception>
    public static StarGruntGame Restore(string saved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saved);

        var read = Read(saved);
        if (read is null || string.IsNullOrWhiteSpace(read.Name))
        {
            throw new ArgumentException("That is not a saved game.", nameof(saved));
        }

        return new StarGruntGame
        {
            Name = read.Name,
            Units = read.Units.ToImmutableDictionary(unit => unit.Id),
            Statuses = read.Statuses.ToImmutableDictionary(entry => new UnitId(entry.UnitId), entry => entry.Status),
            // Taken as saved rather than rebuilt from the roster: a game put down mid-activation has
            // a frame stack, and rebuilding the session would throw the open activation away.
            Session = read.Session ?? new GroundCombatSession(),
            Log = read.Log,
        };
    }

    private static Saved? Read(string saved)
    {
        try
        {
            return JsonSerializer.Deserialize<Saved>(saved, Options);
        }
        catch (JsonException error)
        {
            throw new ArgumentException($"That is not a saved game: {error.Message}", nameof(saved), error);
        }
    }

    /// <summary>The file's shape.</summary>
    private sealed record Saved(
        string Name,
        ImmutableArray<UnitDefinition> Units,
        ImmutableArray<SavedStatus> Statuses,
        GroundCombatSession? Session,
        ImmutableArray<string> Log);

    /// <summary>One unit's status, with the unit it belongs to alongside rather than as a key.</summary>
    private sealed record SavedStatus(string UnitId, UnitStatus Status);
}
