using ForceSignal.Application.Matches;
using Microsoft.Data.Sqlite;

namespace ForceSignal.Infrastructure.Persistence;

/// <summary>
/// Keeps matches in a SQLite file so a restart does not end the game.
/// </summary>
/// <remarks>
/// <para>
/// SQLite rather than a database server, because of what ForceSignal actually is: one process,
/// serving one table's worth of players, off a laptop that someone carried to the game. It runs in
/// that process, needs no configuration beyond a path, and adds nothing to deploy. A database
/// server would buy concurrency and networking that a service holding a single lock cannot use, in
/// exchange for another container to keep running before anyone can play. If this ever becomes a
/// hosted service with more than one instance, <see cref="IMatchStore"/> is the seam to change and
/// no rules code moves.
/// </para>
/// <para>
/// Writes are synchronous and happen inside the service's lock, which is deliberate. The
/// alternative - queueing writes and flushing behind the players - has a window where the game has
/// moved on but the disk has not, and a crash inside that window loses exactly the turn nobody
/// wanted to replay. A local write in write-ahead mode costs well under a millisecond, which is
/// far cheaper than the argument about whose ship had already fired.
/// </para>
/// </remarks>
public sealed class SqliteMatchStore : IMatchStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();

    private readonly string _table;

    /// <summary>Opens the store, creating the file and its table if they are not there.</summary>
    /// <param name="databasePath">Where the file lives.</param>
    /// <param name="tableName">
    /// Which table to keep these in. Defaults to the Full Thrust matches table.
    /// </param>
    /// <remarks>
    /// The table is a parameter because <see cref="LoadAll"/> hands back everything it holds and the
    /// application parses all of it. Two games sharing one table would therefore mean each of them
    /// being handed the other's saves at startup, which is not something either can do anything
    /// sensible with. A store per game keeps that from being possible rather than merely unlikely.
    /// </remarks>
    /// <exception cref="ArgumentException">The path is blank, or the table name is not a plain identifier.</exception>
    public SqliteMatchStore(string databasePath, string tableName = "matches")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _table = SafeIdentifier(tableName);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _connection.Open();

        try
        {
            Prepare();
        }
        catch
        {
            // A store that failed to open holds nothing, so it holds no handle on the file either:
            // the caller falls back to memory and whoever has to recover the database must be able
            // to move it.
            _connection.Dispose();
            throw;
        }
    }

    private void Prepare()
    {
        // Write-ahead logging is what makes a synchronous write cheap enough to do inline, and it
        // is also what leaves the file readable if the process dies mid-write.
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");

        // Three stores - one per game - open this one file, and write-ahead mode still allows one
        // writer at a time. A write that finds the file busy waits inside SQLite for up to this
        // long rather than failing on the spot, which is what a save from a table across the room
        // landing in the same millisecond deserves. Bounded, because the wait happens inside the
        // service's lock: a file that stays busy for five seconds is a fault to report, not a
        // queue to sit in.
        Execute("PRAGMA busy_timeout=5000;");
        Execute($"""
            CREATE TABLE IF NOT EXISTS {_table} (
                match_id   TEXT PRIMARY KEY,
                state      TEXT NOT NULL,
                written_at TEXT NOT NULL
            );
            """);

        // IF NOT EXISTS is a no-op when an object of that name is already there, whatever shape it
        // is in - a drifted table, a hand-restored backup, a table written by another tool, or an
        // object that is not a table at all. So creating the table proves nothing about the table,
        // and this store would open cleanly and then throw on its first read.
        //
        // That distinction is the whole difference between losing a file and losing the server. The
        // host wraps the opening of a store so that a database it cannot use costs the persistence
        // and not the process; a throw on the first read arrives instead from inside a DI factory,
        // takes the host down with it, and so costs every engine on the machine including the ones
        // whose tables were fine. Reading no rows is the cheapest way to make the file's shape
        // answer for itself, and it answers here, where the caller is still able to fall back.
        Execute($"SELECT match_id, state, written_at FROM {_table} LIMIT 0;");
    }

    /// <inheritdoc />
    public void Save(Guid matchId, string state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {_table} (match_id, state, written_at)
                VALUES ($id, $state, $now)
                ON CONFLICT(match_id) DO UPDATE SET state = $state, written_at = $now;
                """;
            command.Parameters.AddWithValue("$id", matchId.ToString("n"));
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            command.ExecuteNonQuery();
        }
    }

    /// <inheritdoc />
    public void Remove(Guid matchId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = $"DELETE FROM {_table} WHERE match_id = $id;";
            command.Parameters.AddWithValue("$id", matchId.ToString("n"));
            command.ExecuteNonQuery();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<StoredMatch> LoadAll()
    {
        lock (_gate)
        {
            var stored = new List<StoredMatch>();
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT match_id, state FROM {_table} ORDER BY written_at;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // A row whose key is not a match id is not one this application wrote, so it is
                // skipped rather than allowed to stop every other game loading.
                if (Guid.TryParseExact(reader.GetString(0), "n", out var matchId))
                {
                    stored.Add(new StoredMatch(matchId, reader.GetString(1)));
                }
            }

            return stored;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Checks a table name is a plain identifier before it is interpolated into SQL.
    /// </summary>
    /// <remarks>
    /// A table name cannot be a query parameter, so it has to be interpolated - and anything
    /// interpolated into SQL has to be proved safe first. Letters, digits and underscores only, and
    /// not starting with a digit, which is every name this application will ever want.
    /// </remarks>
    /// <param name="tableName">The name to check.</param>
    /// <returns>The name, when it is safe.</returns>
    /// <exception cref="ArgumentException">It is blank or holds anything else.</exception>
    private static string SafeIdentifier(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var safe = tableName.Length <= 64
            && !char.IsDigit(tableName[0])
            && tableName.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

        return safe
            ? tableName
            : throw new ArgumentException(
                $"'{tableName}' is not a plain table name. Use letters, digits and underscores only.",
                nameof(tableName));
    }
}
