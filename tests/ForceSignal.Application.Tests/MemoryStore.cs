using ForceSignal.Application.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A store that keeps its rows in memory, standing in for the SQLite one.
/// </summary>
/// <remarks>
/// One copy. It was three private nested ones - in <c>DirtsidePersistenceTests</c>,
/// <c>StarGruntPersistenceTests</c> and <c>DirtsideChitPotTests</c> - all identical, and a fourth
/// was about to be written for the die tables. Restart-survives-a-write is the same claim in every
/// one of those files, so it should be the same double.
/// </remarks>
internal sealed class MemoryStore : IMatchStore
{
    private readonly Dictionary<Guid, string> _rows = [];

    /// <summary>The rows as they stand, for a test that wants to read or doctor one.</summary>
    public IReadOnlyDictionary<Guid, string> Rows => _rows;

    /// <inheritdoc />
    public void Save(Guid matchId, string state) => _rows[matchId] = state;

    /// <inheritdoc />
    public void Remove(Guid matchId) => _rows.Remove(matchId);

    /// <inheritdoc />
    public IReadOnlyList<StoredMatch> LoadAll() =>
        [.. _rows.Select(row => new StoredMatch(row.Key, row.Value))];

    /// <summary>Puts a row in directly, for a test standing in for an older version's writer.</summary>
    /// <param name="matchId">The game.</param>
    /// <param name="state">The row as it would have been stored.</param>
    public void Seed(Guid matchId, string state) => _rows[matchId] = state;
}
