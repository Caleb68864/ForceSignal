using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace ForceSignal.Infrastructure.Tests;

/// <summary>
/// The store against a real file, rather than a stand-in.
///
/// The application tests prove a match survives a restart; these prove the thing it survives into
/// is a file on disk that a second process can open - which is the part a fake cannot tell you.
/// </summary>
public sealed class SqliteMatchStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"forcesignal-{Guid.NewGuid():n}");

    private string DatabasePath => Path.Combine(_directory, "matches.db");

    [Fact]
    public void WhatWentInComesBackOut()
    {
        var id = Guid.NewGuid();
        using (var store = new SqliteMatchStore(DatabasePath))
        {
            store.Save(id, """{"hello":"world"}""");
        }

        // A different instance, as a restarted process would be.
        using var reopened = new SqliteMatchStore(DatabasePath);
        var stored = Assert.Single(reopened.LoadAll());
        Assert.Equal(id, stored.MatchId);
        Assert.Equal("""{"hello":"world"}""", stored.State);
    }

    [Fact]
    public void SavingTwiceReplacesRatherThanDuplicating()
    {
        var id = Guid.NewGuid();
        using var store = new SqliteMatchStore(DatabasePath);

        store.Save(id, "first");
        store.Save(id, "second");

        var stored = Assert.Single(store.LoadAll());
        Assert.Equal("second", stored.State);
    }

    [Fact]
    public void RemovingIsForgettingAndRemovingNothingIsFine()
    {
        var id = Guid.NewGuid();
        using var store = new SqliteMatchStore(DatabasePath);
        store.Save(id, "state");

        store.Remove(id);
        store.Remove(Guid.NewGuid());

        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void TheFileAndItsFolderAreCreatedRatherThanRequired()
    {
        // A fresh container has an empty volume, so the store has to make its own way.
        var nested = Path.Combine(_directory, "deeper", "still", "matches.db");
        using var store = new SqliteMatchStore(nested);
        store.Save(Guid.NewGuid(), "state");

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void AWholeMatchSurvivesARealRestartThroughARealFile()
    {
        var owner = default(MatchCreatedResponse)!;
        using (var store = new SqliteMatchStore(DatabasePath))
        {
            var service = new InMemoryMatchService(null, store, loadPersisted: true);
            owner = service.CreateMatch(new CreateMatchRequest("Blue", "Through The File", Rules: TestRules.Invented));
            var fleet = service.CreateFleet(owner.MatchId, new CreateFleetRequest(owner.ParticipantToken, "Blue", null)).Fleets.Single();
            service.CreateShip(fleet.Id, new CreateShipRequest(
                owner.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 4, StartX: 20, StartY: 24));
        }

        using var reopened = new SqliteMatchStore(DatabasePath);
        var restarted = new InMemoryMatchService(null, reopened, loadPersisted: true);

        var snapshot = restarted.GetSnapshot(owner.MatchId);
        Assert.Equal("Through The File", snapshot.Name);
        Assert.Equal("Valiant", snapshot.Ships.Single().Name);
        Assert.True(restarted.IsMatchParticipant(owner.MatchId, owner.ParticipantToken));
    }

    [Fact]
    public void ARowThatIsNotOneOfOursIsSkippedRatherThanFatal()
    {
        using var store = new SqliteMatchStore(DatabasePath);
        store.Save(Guid.NewGuid(), "good");

        // Something else wrote into the table. One bad row must not cost every game.
        // Pooling is off deliberately: a pooled connection keeps the file open after disposal and
        // the cleanup below cannot then delete it.
        using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = "INSERT INTO matches (match_id, state, written_at) VALUES ('not-a-guid', 'x', 'now');";
            command.ExecuteNonQuery();
        }

        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void TwoGamesInOneFileDoNotSeeEachOthersSaves()
    {
        var path = Path.Combine(_directory, "shared.db");
        using var matches = new SqliteMatchStore(path);
        using var ground = new SqliteMatchStore(path, "stargrunt_games");
        var spaceGame = Guid.NewGuid();
        var groundGame = Guid.NewGuid();

        matches.Save(spaceGame, "a fleet action");
        ground.Save(groundGame, "a firefight");

        // LoadAll hands back everything a store holds and the application parses all of it, so a
        // shared table would mean each game being handed the other's saves at startup.
        Assert.Equal(spaceGame, Assert.Single(matches.LoadAll()).MatchId);
        Assert.Equal(groundGame, Assert.Single(ground.LoadAll()).MatchId);
    }

    [Theory]
    [InlineData("matches; DROP TABLE matches--")]
    [InlineData("2fast")]
    [InlineData("has space")]
    [InlineData("")]
    public void ATableNameThatIsNotAPlainIdentifierIsRefused(string tableName)
    {
        // The name cannot be a query parameter, so it is interpolated - which means it has to be
        // proved safe rather than trusted.
        Assert.Throws<ArgumentException>(() =>
            new SqliteMatchStore(Path.Combine(_directory, "guarded.db"), tableName));
    }

    [Fact]
    public async Task AWriteThatFindsTheFileBusyWaitsForItRatherThanFailing()
    {
        using var store = new SqliteMatchStore(DatabasePath);
        store.Save(Guid.NewGuid(), """{"first":true}""");

        // Somebody else - another of the three stores over this one file - holds the write lock
        // for a moment. The save has to land after they let go, not fail because they had it.
        using var other = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        other.Open();
        using (var hold = other.BeginTransaction())
        {
            using (var write = other.CreateCommand())
            {
                write.Transaction = hold;
                write.CommandText = "INSERT INTO matches (match_id, state, written_at) VALUES ('not-a-match', '{}', 'now');";
                write.ExecuteNonQuery();
            }

            var release = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400));
                hold.Commit();
            });

            var id = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow;
            store.Save(id, """{"second":true}""");
            await release;

            Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
            Assert.Contains(store.LoadAll(), row => row.MatchId == id);
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is untidy, not a failing test.
        }
    }
}
