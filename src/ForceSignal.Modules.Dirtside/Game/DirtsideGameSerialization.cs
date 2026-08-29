using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Game;

/// <summary>
/// Writing a Dirtside game out and reading it back.
/// </summary>
/// <remarks>
/// <para>
/// The same surrogate the other game uses, and for the same narrow reason: the roster is a
/// dictionary keyed by <see cref="UnitId"/>, and a record struct cannot be a JSON property name. The
/// per-element status inside a platoon has the same problem one level down, so it gets the same
/// treatment.
/// </para>
/// <para>
/// Restore fidelity is an equality assertion rather than a bespoke comparer, which is the whole
/// reason the game is a value - and it is only true because the definitions and statuses compare
/// structurally. They did not, at first, and a test caught it.
/// </para>
/// </remarks>
public static class DirtsideGameSerialization
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Writes a game out.</summary>
    /// <param name="game">The game to save.</param>
    /// <returns>The serialized game.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    public static string Save(DirtsideGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return JsonSerializer.Serialize(
            new Saved(
                game.Name,
                [.. game.Units.Values],
                [.. game.Statuses.Select(entry => new SavedStatus(
                    entry.Key.Value,
                    entry.Value,
                    [.. entry.Value.Elements.Select(element => new SavedElement(element.Key.Value, element.Value))]))],
                game.Session,
                game.Assault,
                game.Log),
            Options);
    }

    /// <summary>Reads a game back.</summary>
    /// <param name="saved">A string produced by <see cref="Save"/>.</param>
    /// <returns>The restored game, equal to the one that was saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="saved"/> is blank or does not hold a game.</exception>
    public static DirtsideGame Restore(string saved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saved);

        var read = Read(saved);
        if (read is null || string.IsNullOrWhiteSpace(read.Name))
        {
            throw new ArgumentException("That is not a saved game.", nameof(saved));
        }

        return new DirtsideGame
        {
            Name = read.Name,
            Units = read.Units.ToImmutableDictionary(unit => unit.Id),
            Statuses = read.Statuses.ToImmutableDictionary(
                entry => new UnitId(entry.UnitId),
                entry => entry.Status with
                {
                    Elements = entry.Elements.IsDefaultOrEmpty
                        ? ImmutableDictionary<ElementId, ElementStatus>.Empty
                        : entry.Elements.ToImmutableDictionary(
                            element => new ElementId(element.ElementId),
                            element => element.Status),
                }),

            // Taken as saved rather than rebuilt from the roster: a game put down mid-activation has
            // a frame stack, and rebuilding the session would throw the open activation away.
            Session = read.Session ?? new GroundCombatSession(),
            Assault = read.Assault,
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
        ImmutableArray<PlatoonDefinition> Units,
        ImmutableArray<SavedStatus> Statuses,
        GroundCombatSession? Session,
        DirtsideAssault? Assault,
        ImmutableArray<string> Log);

    /// <summary>One platoon's status, with the platoon it belongs to alongside rather than as a key.</summary>
    private sealed record SavedStatus(
        string UnitId,
        PlatoonStatus Status,
        ImmutableArray<SavedElement> Elements);

    /// <summary>One element's status, with the element it belongs to alongside rather than as a key.</summary>
    private sealed record SavedElement(string ElementId, ElementStatus Status);
}
