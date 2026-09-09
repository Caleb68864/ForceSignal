using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Features;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Covers the transport boundary's guards: the headers every response carries, what an oversized
/// or deeply-nested snapshot file gets back, how a missing or blank participant token is answered,
/// the budgets on the endpoints a stranger can reach, and what a fault the server did not plan for
/// is allowed to say.
/// </summary>
public sealed class ApiHardeningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task EveryResponse_CarriesTheBaselineBrowserSecurityHeaders()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        // The API answers with JSON and never a page, so nothing it returns should be framable.
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task ProblemResponses_AlsoCarryTheSecurityHeaders()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/matches/by-code/NOPE-NOPE-NOPE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task BlankParticipantTokenHeader_IsRefusedAsIfItWereMissing()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Blank Token", 72, 48, Rules: TestRules.Invented)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/matches/{created.MatchId}/participants/me/ready")
        {
            Content = JsonContent.Create(new ReadyRequest(true)),
        };
        request.Headers.TryAddWithoutValidation("X-Participant-Token", "   ");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AMatchTheServerNoLongerHolds_IsNotFoundBeforeTheTokenIsJudged()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // A device that kept its session across a restart of an in-memory server asks for a match
        // that is simply gone. Answering 403 - which is what an unknown match used to get, since no
        // participant could be found in it - told the client its token was bad, and the client only
        // knows to let go of a session on a 404. Every table was stuck on a dead room until someone
        // found "Leave Device Session".
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{Guid.NewGuid()}/snapshot");
        request.Headers.TryAddWithoutValidation("X-Participant-Token", "a-token-from-before-the-restart");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingParticipantTokenHeader_IsRefused()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "No Token", 72, 48, Rules: TestRules.Invented)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        using var response = await client.PostAsJsonAsync(
            $"/api/matches/{created.MatchId}/participants/me/ready",
            new ReadyRequest(true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeeplyNestedSnapshotFile_IsRefusedAsProblemJsonRatherThanRecursedInto()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Well-formed JSON, but nested far past anything a saved match is.
        var payload = new StringBuilder();
        const int depth = 400;
        payload.Append("{\"snapshot\":");
        for (var i = 0; i < depth; i++)
        {
            payload.Append("{\"a\":");
        }

        payload.Append('1');
        payload.Append('}', depth);
        payload.Append('}');

        using var response = await client.PostAsync("/api/matches/restore",
            new StringContent(payload.ToString(), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OversizedSnapshotFile_IsRefusedBeforeItIsParsed()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Twelve megabytes of valid JSON string, past the eight-megabyte ceiling.
        var payload = "{\"name\":\"" + new string('x', 12 * 1024 * 1024) + "\"}";

        using var response = await client.PostAsync("/api/matches/restore",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("larger than", problem.GetProperty("detail").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RoomCodeLookup_IsBudgetedSoCodesCannotBeWalkedQuickly()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var response = await client.GetAsync($"/api/matches/by-code/GUESS-{attempt:D3}-CODE");
            statuses.Add(response.StatusCode);
        }

        // The first tries answer normally; the budget cuts in well before forty.
        Assert.Contains(HttpStatusCode.NotFound, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task AGenuinePlayerMistypingTheirCode_IsNotLockedOut()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Typo Room", 72, 48, Rules: TestRules.Invented)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        // Three fat-fingered attempts, then the real code, is well inside the budget.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var miss = await client.GetAsync($"/api/matches/by-code/WRONG-CODE-{attempt}");
            Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);
        }

        using var hit = await client.GetAsync($"/api/matches/by-code/{created.JoinCode}");
        hit.EnsureSuccessStatusCode();
        var identity = await hit.Content.ReadFromJsonAsync<MatchIdentityDto>(JsonOptions);
        Assert.Equal(created.MatchId, identity!.MatchId);
    }

    [Fact]
    public async Task TheMatchSnapshotIsOnlyReadableBySomeoneInTheMatch()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Private", 72, 48, Rules: TestRules.Invented)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);

        // The snapshot is the whole game: positions, damage, every shot and its dice, the log, and
        // who is playing. The match id is not a secret - the room-code lookup hands it out and
        // every notification echoes it - so the id alone must not open it.
        using var anonymous = await client.GetAsync($"/api/matches/{created.MatchId}/snapshot");
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);

        // A token from a different match is no better.
        var other = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Red", "Elsewhere", 72, 48, Rules: TestRules.Invented)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        using var wrongMatch = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{created.MatchId}/snapshot");
        wrongMatch.Headers.TryAddWithoutValidation("X-Participant-Token", other!.ParticipantToken);
        using var refused = await client.SendAsync(wrongMatch);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The player who is actually in the match reads it normally.
        using var mine = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{created.MatchId}/snapshot");
        mine.Headers.TryAddWithoutValidation("X-Participant-Token", created.ParticipantToken);
        using var allowed = await client.SendAsync(mine);
        allowed.EnsureSuccessStatusCode();
    }

    [Theory]
    // The tablet across the table, and this machine.
    [InlineData("http://localhost:6297", true)]
    [InlineData("http://127.0.0.1:6297", true)]
    [InlineData("http://192.168.1.50:6297", true)]
    [InlineData("http://10.0.0.7:6297", true)]
    [InlineData("http://172.20.1.4:6297", true)]
    [InlineData("http://forcesignal.local:6297", true)]
    // A page on the open internet. Development reflects the caller's origin and allows credentials,
    // so without this any site a player browsed to could call their instance and read the answers.
    [InlineData("https://evil.example", false)]
    [InlineData("http://8.8.8.8", false)]
    [InlineData("http://172.32.0.1", false)]
    [InlineData("http://notlocalhost.example.com", false)]
    public async Task DevelopmentOnlyReflectsOriginsOnThisMachineOrAPrivateNetwork(string origin, bool allowed)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        using var response = await client.SendAsync(request);

        var reflected = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            && values.Contains(origin);
        Assert.Equal(allowed, reflected);
    }

    [Fact]
    public async Task WithADatabaseConfigured_ReadinessStopsWarningThatMatchesAreLostOnRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"forcesignal-ready-{Guid.NewGuid():n}");
        try
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Persistence:MatchDatabasePath"] = Path.Combine(directory, "matches.db"),
            });
            using var client = factory.CreateClient();

            var ready = await client.GetFromJsonAsync<JsonElement>("/ready");

            Assert.Equal("sqlite", ready.GetProperty("persistence").GetString());
            var warnings = ready.GetProperty("warnings").EnumerateArray()
                .Select(warning => warning.GetString() ?? string.Empty)
                .ToArray();
            Assert.DoesNotContain(warnings, warning => warning.Contains("stored in memory", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                    // A stray temp directory is untidy, not a failing test.
                }
            }
        }
    }

    [Fact]
    public async Task ADatabaseFileThatWillNotOpenCostsThePersistenceAndNotTheServer()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"forcesignal-corrupt-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "matches.db");

        // A file where the database should be, that is not one. This is what a bad shutdown, a
        // half-finished copy, or a restore from the wrong backup leaves behind.
        await File.WriteAllTextAsync(databasePath, "this is not a database, it is a text file");

        try
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Persistence:MatchDatabasePath"] = databasePath,
                // Both ground engines on, so all three stores are opened and all three have to
                // survive the same file.
                ["Features:StarGrunt"] = "true",
                ["Features:Dirtside"] = "true",
            });

            // The store is opened from a DI factory, and the host resolves the match service at
            // startup to report what it could not restore - so an exception let out of that factory
            // does not fail one request, it stops the server coming up at all. One damaged file then
            // costs every table on the machine, including the ones with nothing stored, which
            // inverts the entire point of writing games to disk.
            using var client = factory.CreateClient();

            var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest(
                "Blue", "Bad Disk", 72, 48, Rules: TestRules.Invented))).Content.ReadFromJsonAsync<MatchCreatedResponse>();
            Assert.NotNull(created);

            // And the game is playable, just not durable - which is exactly the mode a server with no
            // path configured has always run in.
            using var read = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/{created.MatchId}/snapshot");
            read.Headers.TryAddWithoutValidation("X-Participant-Token", created.ParticipantToken);
            using var snapshot = await client.SendAsync(read);
            snapshot.EnsureSuccessStatusCode();

            // And readiness says so. Reporting the configured intent instead said "sqlite" for a
            // server running on memory, so the operator believed their games survived a restart and
            // the container healthcheck agreed with them - right up until the restart that ended
            // every game on the machine.
            var ready = await client.GetFromJsonAsync<JsonElement>("/ready");
            Assert.Equal("in-memory", ready.GetProperty("persistence").GetString());
            var warnings = ready.GetProperty("warnings").EnumerateArray()
                .Select(warning => warning.GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains(warnings, warning => warning.Contains("stored in memory", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(warnings, warning => warning.Contains("could not be", StringComparison.OrdinalIgnoreCase));

            // The path is not in the warning: this route answers anyone who can reach it, and the
            // API log already names the file for whoever has to go and recover it.
            Assert.DoesNotContain(warnings, warning => warning.Contains(databasePath, StringComparison.Ordinal));

            // The bad file is left where it is, so whoever has to recover it still can.
            Assert.Equal("this is not a database, it is a text file", await File.ReadAllTextAsync(databasePath));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A stray temp directory is untidy, not a failing test.
            }
        }
    }

    [Fact]
    public async Task ReadinessReportsEveryStoreThisServerOpened()
    {
        // Readiness used to answer from the single injectable IMatchStore, which is Full Thrust's.
        // The two ground engines open stores of their own, as constructor arguments, reachable from
        // nowhere - so "persistence: sqlite" was a statement about one table out of three, and a
        // machine whose Dirtside table had failed reported itself healthy while every Dirtside game
        // on it went to memory. One entry per store, collected as each is opened.
        var directory = Path.Combine(Path.GetTempPath(), $"forcesignal-stores-{Guid.NewGuid():n}");
        try
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Persistence:MatchDatabasePath"] = Path.Combine(directory, "matches.db"),
                ["Features:StarGrunt"] = "true",
                ["Features:Dirtside"] = "true",
            });
            using var client = factory.CreateClient();

            var ready = await client.GetFromJsonAsync<JsonElement>("/ready");

            var stores = ready.GetProperty("stores").EnumerateArray()
                .ToDictionary(
                    store => store.GetProperty("engine").GetString() ?? string.Empty,
                    store => store.GetProperty("persistence").GetString() ?? string.Empty,
                    StringComparer.Ordinal);

            Assert.Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Full Thrust"] = "sqlite",
                    ["StarGrunt"] = "sqlite",
                    ["Dirtside"] = "sqlite",
                },
                stores);
            Assert.Equal("sqlite", ready.GetProperty("persistence").GetString());
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A stray temp directory is untidy, not a failing test.
            }
        }
    }

    [Fact]
    public async Task AWrongShapedTableCostsThatEnginesPersistenceAndNotTheServer()
    {
        // The other half of "a database file that will not open costs the persistence, not the
        // server". A file that will not open is caught; a file that opens and holds the wrong table
        // was not. CREATE TABLE IF NOT EXISTS is a no-op against an object of that name whatever its
        // shape, so a drifted or hand-restored dirtside_games table opened cleanly and then threw
        // out of the DI factory on the first SELECT - and the API did not start at all.
        var directory = Path.Combine(Path.GetTempPath(), $"forcesignal-shape-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "matches.db");

        await using (var raw = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = "CREATE TABLE dirtside_games (game_id TEXT PRIMARY KEY, payload TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Persistence:MatchDatabasePath"] = databasePath,
                ["Features:Dirtside"] = "true",
            });
            using var client = factory.CreateClient();

            // The server comes up, and Full Thrust - whose table is fine - is on disk.
            var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest(
                "Blue", "Wrong Shape", 72, 48, Rules: TestRules.Invented))).Content.ReadFromJsonAsync<MatchCreatedResponse>();
            Assert.NotNull(created);

            var ready = await client.GetFromJsonAsync<JsonElement>("/ready");
            var stores = ready.GetProperty("stores").EnumerateArray()
                .ToDictionary(
                    store => store.GetProperty("engine").GetString() ?? string.Empty,
                    store => store.GetProperty("persistence").GetString() ?? string.Empty,
                    StringComparer.Ordinal);

            Assert.Equal("sqlite", stores["Full Thrust"]);
            Assert.Equal("in-memory", stores["Dirtside"]);

            // And readiness says the machine is not what it was asked to be. This is the state that
            // used to report a healthy "sqlite" while every Dirtside game on the machine was going
            // to memory - the operator's readiness check green, and the games gone at the restart.
            Assert.Equal("mixed", ready.GetProperty("persistence").GetString());
            var warnings = ready.GetProperty("warnings").EnumerateArray()
                .Select(warning => warning.GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains(warnings, warning =>
                warning.Contains("Dirtside", StringComparison.Ordinal)
                && warning.Contains("stored in memory", StringComparison.OrdinalIgnoreCase));

            // The Dirtside table is left exactly as it was found, so whoever has to recover it can.
            await using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await check.OpenAsync();
            await using var columns = check.CreateCommand();
            columns.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'dirtside_games';";
            Assert.Equal(
                "CREATE TABLE dirtside_games (game_id TEXT PRIMARY KEY, payload TEXT NOT NULL)",
                (string?)await columns.ExecuteScalarAsync());
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A stray temp directory is untidy, not a failing test.
            }
        }
    }

    [Fact]
    public async Task EveryEngineThisServerOffers_ReportsAStoreOfItsOwn()
    {
        // The drift guard, and the reason this was fixed with a report rather than with two more
        // special cases: a fifth engine arrives as a feature flag, and if its store is not opened
        // the way the other three are it never reaches readiness and nobody finds out until the
        // restart. Read off the flags themselves rather than from a list kept here, so adding one
        // fails this test until its store is reported too.
        var directory = Path.Combine(Path.GetTempPath(), $"forcesignal-engines-{Guid.NewGuid():n}");
        try
        {
            var everyEngineOn = typeof(FeatureFlagsDto)
                .GetProperties()
                .ToDictionary(flag => $"Features:{flag.Name}", _ => (string?)"true", StringComparer.Ordinal);
            everyEngineOn["Persistence:MatchDatabasePath"] = Path.Combine(directory, "matches.db");

            using var factory = CreateFactory(everyEngineOn);
            using var client = factory.CreateClient();

            var ready = await client.GetFromJsonAsync<JsonElement>("/ready");
            var reported = ready.GetProperty("stores").EnumerateArray()
                .Select(store => store.GetProperty("engine").GetString() ?? string.Empty)
                .ToArray();

            // Full Thrust has no flag: it is the game this server exists for and is always on.
            Assert.Contains("Full Thrust", reported);
            foreach (var flag in typeof(FeatureFlagsDto).GetProperties())
            {
                Assert.True(
                    ready.GetProperty("features").GetProperty(JsonNamingPolicy.CamelCase.ConvertName(flag.Name)).GetBoolean(),
                    $"{flag.Name} was switched on for this test and readiness says it is off.");
                Assert.Contains(flag.Name, reported);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A stray temp directory is untidy, not a failing test.
            }
        }
    }

    [Fact]
    public async Task MatchCreation_IsBudgetedSoAStrangerCannotFillTheServer()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 15; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/matches",
                new CreateMatchRequest("Blue", $"Flood {attempt}", 72, 48, Rules: TestRules.Invented));
            statuses.Add(response.StatusCode);
        }

        // Creating a match needs no credentials, so this is the route a loop reaches for. The
        // first few go through; the budget cuts in well before fifteen.
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task GroundGameCreation_SharesTheBudgetThatGuardsOpeningAMatch()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:StarGrunt"] = "true",
            ["Features:Dirtside"] = "true",
        });
        using var client = factory.CreateClient();

        // Same threat model as POST /api/matches, and for a while the only routes beside it that a
        // stranger could loop on with no credentials at all. One budget covers all three, so
        // alternating between the engines does not buy three times the allowance.
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 15; attempt++)
        {
            var path = attempt % 2 == 0 ? "/api/dirtside/games" : "/api/stargrunt/games";
            using var response = await client.PostAsJsonAsync(path, new { name = $"Flood {attempt}" });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task ATableOpeningOneGroundGame_IsNotTurnedAway()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:Dirtside"] = "true",
        });
        using var client = factory.CreateClient();

        // The budget exists to stop a loop, not a table. Opening a game has to still just work.
        using var response = await client.PostAsJsonAsync("/api/dirtside/games", new { name = "Ridge 9" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SeatClaim_SharesTheRoomCodeBudget()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Claiming a seat is authenticated by room code like the lookups, and it was the one
        // code-guarded route with no budget at all.
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                $"/api/matches/{Guid.NewGuid()}/seats/{Guid.NewGuid()}/claim",
                new ClaimSeatRequest("Guesser", $"GUESS-{attempt:D3}-CODE"));
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.NotFound, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task ANullOrderInTheBody_IsABadRequestNotAServerFault()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Null Order", 72, 48, Rules: TestRules.Invented)))
            .Content.ReadFromJsonAsync<MatchCreatedResponse>();
        Assert.NotNull(created);
        var fleet = await (await client.PostAsJsonAsync($"/api/matches/{created.MatchId}/fleets",
            new CreateFleetRequest(created.ParticipantToken, "Blue Watch", null))).Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);
        var ship = await (await client.PostAsJsonAsync($"/api/fleets/{fleet!.Fleets.Single().Id}/ships",
            new CreateShipRequest(created.ParticipantToken, "Valiant", "Cruiser", 4, 6, 3, 12, 2, StartX: 20, StartY: 24)))
            .Content.ReadFromJsonAsync<MatchSnapshotDto>(JsonOptions);

        // The contract says the order is required; the serializer binds a JSON null to it anyway.
        using var response = await client.PostAsync(
            $"/api/matches/{created.MatchId}/turns/current/orders/preview",
            new StringContent(
                $$"""{"participantToken":"{{created.ParticipantToken}}","shipId":"{{ship!.Ships.Single().Id}}","order":null}""",
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("order", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFaultTheServerDidNotPlanFor_IsAGenericProblemThatSaysNothingAboutIt()
    {
        // A store that falls over inside the service, with a message that must not reach the wire.
        using var factory = CreateFactory(store: new FaultingStore(new InvalidCastException("internal detail: connection string was C:\\secrets")));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Fault", 72, 48, Rules: TestRules.Invented));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Something went wrong on the server.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AStorageFailure_IsA503WithAStableLine()
    {
        using var factory = CreateFactory(store: new FaultingStore(new SqliteException("database is locked", 5)));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Busy", 72, 48, Rules: TestRules.Invented));

        // The in-memory match has moved on and the file has not; the caller is told the server is
        // busy, in words that do not change with the SQLite error text.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("storage", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFound_IsDecidedByTheExceptionsTypeAndNotByItsWording()
    {
        // A refusal whose message happens to say "not found" is still a refusal. The status used
        // to be read off the words, so this would have been a 404 for the wrong reason.
        using var factory = CreateFactory(store: new FaultingStore(new InvalidOperationException("The disk was not found to be writable.")));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/matches", new CreateMatchRequest("Blue", "Wording", 72, 48, Rules: TestRules.Invented));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForwardedHeaders_AreIgnoredUnlessTheOperatorOptsIn()
    {
        // Forty lookups, each claiming a different client behind a proxy. Without the opt-in the
        // claim is ignored and they share one budget; with it, each is its own caller.
        using var untrusting = CreateFactory();
        using (var client = untrusting.CreateClient())
        {
            var statuses = await LookupsClaimingDistinctClients(client);
            Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        }

        using var trusting = CreateFactory(new Dictionary<string, string?> { ["Proxy:TrustForwardedHeaders"] = "true" });
        using (var client = trusting.CreateClient())
        {
            var statuses = await LookupsClaimingDistinctClients(client);
            Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
        }
    }

    private static async Task<List<HttpStatusCode>> LookupsClaimingDistinctClients(HttpClient client)
    {
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/matches/by-code/GUESS-{attempt:D3}-CODE");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"203.0.113.{attempt + 1}");
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    /// <summary>A store whose every write fails the way a test says, standing in for a broken disk.</summary>
    private sealed class FaultingStore(Exception fault) : IMatchStore
    {
        public void Save(Guid matchId, string state) => throw fault;

        public void Remove(Guid matchId)
        {
        }

        public IReadOnlyList<StoredMatch> LoadAll() => [];
    }

    private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? settings = null, IMatchStore? store = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                if (settings is not null)
                {
                    builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings));
                }

                if (store is not null)
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<IMatchStore>();
                        services.AddSingleton(store);
                    });
                }
            });
}
