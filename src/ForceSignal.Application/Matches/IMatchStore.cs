namespace ForceSignal.Application.Matches;

/// <summary>
/// Somewhere a match can survive the process that was serving it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a key and an opaque string rather than a schema. The shape of a match is the
/// application's business and changes with the rules; a store's business is only that what went in
/// comes back out. That keeps the two free of each other - the SQLite store shipped today and a
/// networked database later differ in one class and no rules code - and it keeps the format
/// versioned by the application rather than by a migration.
/// </para>
/// <para>
/// Every method is synchronous, because the match service does its work under one lock and has
/// nowhere useful to await. A store that needs to be slow should be fast instead.
/// </para>
/// </remarks>
public interface IMatchStore
{
    /// <summary>Writes a match, replacing whatever was there under the same id.</summary>
    /// <param name="matchId">The match being written.</param>
    /// <param name="state">The serialized match.</param>
    void Save(Guid matchId, string state);

    /// <summary>Forgets a match. Does nothing when it was not there.</summary>
    /// <param name="matchId">The match to remove.</param>
    void Remove(Guid matchId);

    /// <summary>Reads back every match held, for rebuilding them at startup.</summary>
    /// <returns>Each match's id and its serialized state.</returns>
    IReadOnlyList<StoredMatch> LoadAll();
}

/// <summary>One match as it sits in a store.</summary>
/// <param name="MatchId">The match's id.</param>
/// <param name="State">The serialized match.</param>
public readonly record struct StoredMatch(Guid MatchId, string State);

/// <summary>
/// A store that keeps nothing, for when durable storage is switched off.
/// </summary>
/// <remarks>
/// This is the default rather than a null reference so the service never has to ask whether it has
/// a store before using one. Choosing not to persist is a configuration, not a special case.
/// </remarks>
public sealed class NoMatchStore : IMatchStore
{
    /// <summary>The single instance. It holds nothing, so one is enough.</summary>
    public static NoMatchStore Instance { get; } = new();

    /// <inheritdoc />
    public void Save(Guid matchId, string state)
    {
    }

    /// <inheritdoc />
    public void Remove(Guid matchId)
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<StoredMatch> LoadAll() => [];
}
