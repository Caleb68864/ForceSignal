using System.Net.Http.Json;
using System.Text.Json;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Readiness after something goes wrong, rather than readiness at startup.
/// </summary>
/// <remarks>
/// <para>
/// <c>/ready</c>'s <c>durable</c> was a record of what each store <em>turned out to be when it was
/// opened</em>, written once and never revisited. A volume unmounted mid-session, a disk that
/// filled, a file gone read-only: every write 503s, every game stops being written down, and
/// <c>/ready</c> goes on saying <c>sqlite</c> with no warning while the container's own healthcheck
/// calls the stack healthy. That is the same defect this endpoint has now been fixed for twice -
/// reporting the configuration rather than the reality, and then reporting one store's reality as
/// three - arriving a third time along the time axis instead of the space one.
/// </para>
/// <para>
/// The store here is a double rather than a real file made unwritable, because "what happens when
/// the disk goes" has to be provoked the same way on every machine that runs this suite. What it
/// stands in for is exact: <c>SqliteMatchStore.Save</c> throws, the middleware turns that into a
/// 503, and the engine carries on in memory.
/// </para>
/// </remarks>
public sealed class ReadinessStaysCurrentTests
{
    [Fact]
    public void TheWrapperIsWhatTheHostActuallyHandsOut()
    {
        // Reached-the-subject, and the check that stops this whole file being about a class nobody
        // calls: with a database configured, the store injected into every request really is the
        // watched one. Without this, the two tests below could pass against a wrapper that the
        // running host never builds.
        var file = Path.Combine(Path.GetTempPath(), $"forcesignal-ready-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = Factory(new Dictionary<string, string?>
            {
                ["Persistence:MatchDatabasePath"] = file,
            });

            Assert.IsType<ReportingMatchStore>(factory.Services.GetRequiredService<IMatchStore>());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task AStoreThatStopsWritingStopsBeingReportedAsDurable()
    {
        var failing = new FailingStore();
        using var factory = WithStore(failing);
        using var client = factory.CreateClient();

        // Reached-the-subject: before anything goes wrong, this really is a server reporting a
        // database. If it were not, the assertion after the failure would prove nothing.
        Assert.Equal("sqlite", (await Readiness(client)).GetProperty("persistence").GetString());

        failing.Working = false;
        using var refused = await client.PostAsJsonAsync(
            "/api/matches",
            new CreateMatchRequest("Blue", "Readiness", 72, 48, TestRules.Invented));
        Assert.False(refused.IsSuccessStatusCode);

        var after = await Readiness(client);
        Assert.Equal("in-memory", after.GetProperty("persistence").GetString());
        Assert.Contains(
            after.GetProperty("warnings").EnumerateArray().Select(entry => entry.GetString() ?? string.Empty),
            entry => entry.Contains("will be lost on API restart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStoreThatStartsWritingAgainIsReportedAsDurableAgain()
    {
        // The control that must be accepted. A report that latched on the first failure would
        // satisfy the test above and then be wrong for the rest of the session - a disk that came
        // back, or one slow write that timed out, would leave the operator chasing a fault that had
        // already fixed itself.
        var failing = new FailingStore();
        using var factory = WithStore(failing);
        using var client = factory.CreateClient();

        failing.Working = false;
        using var refused = await client.PostAsJsonAsync(
            "/api/matches",
            new CreateMatchRequest("Blue", "Readiness", 72, 48, TestRules.Invented));
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Equal("in-memory", (await Readiness(client)).GetProperty("persistence").GetString());

        failing.Working = true;
        using var accepted = await client.PostAsJsonAsync(
            "/api/matches",
            new CreateMatchRequest("Blue", "Readiness", 72, 48, TestRules.Invented));
        accepted.EnsureSuccessStatusCode();

        var after = await Readiness(client);
        Assert.Equal("sqlite", after.GetProperty("persistence").GetString());
        Assert.DoesNotContain(
            after.GetProperty("warnings").EnumerateArray().Select(entry => entry.GetString() ?? string.Empty),
            entry => entry.Contains("will be lost on API restart", StringComparison.Ordinal));
    }

    private static async Task<JsonElement> Readiness(HttpClient client)
    {
        using var ready = await client.GetAsync(new Uri("/ready", UriKind.Relative));
        ready.EnsureSuccessStatusCode();
        return await ready.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A host whose Full Thrust store is the double, watched exactly as a real one is.</summary>
    private static WebApplicationFactory<Program> WithStore(IMatchStore store) =>
        Factory(new Dictionary<string, string?>
        {
            // Set so the warning under test is the fell-back one rather than the never-configured
            // one, which is a different sentence about a different situation.
            ["Persistence:MatchDatabasePath"] = Path.Combine(Path.GetTempPath(), "forcesignal-not-opened.db"),
        }).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IMatchStore>(provider => new ReportingMatchStore(
                store,
                provider.GetRequiredService<MatchStoreReport>(),
                "Full Thrust"))));

    private static WebApplicationFactory<Program> Factory(Dictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });

    /// <summary>A store that can be switched off and on, standing in for the disk going away.</summary>
    private sealed class FailingStore : IMatchStore
    {
        public bool Working { get; set; } = true;

        public void Save(Guid matchId, string state) => Check();

        public void Remove(Guid matchId) => Check();

        public IReadOnlyList<StoredMatch> LoadAll()
        {
            Check();
            return [];
        }

        private void Check()
        {
            if (!Working)
            {
                throw new IOException("The volume holding the match database is no longer mounted.");
            }
        }
    }
}
