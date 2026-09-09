using ForceSignal.Api;
using ForceSignal.Api.Endpoints;
using ForceSignal.Api.Hubs;
using ForceSignal.Application;
using ForceSignal.Application.Features;
using ForceSignal.Application.Ground;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Features;
using ForceSignal.Contracts.Ground;
using ForceSignal.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Scalar.AspNetCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = ReadAllowedOrigins(builder.Configuration, builder.Environment);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();

        if (allowedOrigins.AllowAnyOrigin)
        {
            // Development reflects the caller's origin rather than naming one, because the whole
            // point of this app is being served off a laptop at a table and reached from tablets on
            // the same wifi - the origin is whatever address that laptop happened to get, and
            // nobody wants to configure it before a game.
            //
            // But reflecting *anything* while also allowing credentials means any page a player
            // browses to can call their instance and read the answers. So the reflection is limited
            // to origins that are on this machine or on a private network: a page on the open
            // internet is refused, and the tablet across the table is not.
            policy.SetIsOriginAllowed(IsLocalNetworkOrigin);
        }
        else
        {
            policy.WithOrigins(allowedOrigins.Origins);
        }
    });
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
// Where matches are kept so a restart does not end the game. A configured path switches durable
// storage on; leaving it unset keeps everything in memory, which is what a throwaway session or a
// test wants and which readiness then warns about.
//
// Read from the built configuration rather than the builder's, so settings a host layers in during
// startup - a container, or an integration test - are seen rather than missed.
builder.Services.AddSingleton<IMatchStore>(sp => OpenMatchStore(sp, "Full Thrust", "matches"));
builder.Services.AddSingleton<IMatchService>(sp =>
    new InMemoryMatchService(null, sp.GetRequiredService<IMatchStore>(), loadPersisted: true));

// StarGrunt games go in a table of their own, in the same file. LoadAll hands back everything a
// store holds and each service parses all of it, so a shared table would mean each game being
// handed the other's saves at startup. Registered unconditionally: the flag governs whether any
// route reaches this, and a service nobody can call costs a dictionary.
builder.Services.AddSingleton<IStarGruntGameService>(sp =>
    new StarGruntGameService(null, OpenMatchStore(sp, "StarGrunt", "stargrunt_games")));

builder.Services.AddSingleton<IDirtsideGameService>(sp =>
    new DirtsideGameService(null, OpenMatchStore(sp, "Dirtside", "dirtside_games")));

// Which optional game engines this server offers. Both ground-combat engines default to off: they
// are built alongside the working Full Thrust game and must not be able to reach a table that
// turned up to play it. See FeatureFlags for why the flag is checked in more than one place.
// Resolved from the built configuration rather than the builder's, so a host that layers settings
// in during startup - a container, or an integration test - is read rather than missed.
builder.Services.AddSingleton(sp => FeatureFlags.Read(
    new ConfigurationFeatureSource(sp.GetRequiredService<IConfiguration>())));

// A room code is the only thing standing between a stranger and a seat, and it is short enough to
// be read aloud. Guessing one is a matter of trying codes quickly, so the two endpoints that turn
// a code into a match are the ones worth slowing down. The window is generous enough that a player
// mistyping their code a few times never notices it.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.RoomCodeLookup, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Restoring is expensive - it parses a file and allocates a whole match - so it gets its own,
    // tighter budget.
    options.AddPolicy(RateLimitPolicies.Restore, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Opening a game needs no credentials and allocates state the server keeps for a day, so these
    // are the routes a stranger can use to fill the server up. Ten a minute is more than any table
    // opens and far fewer than it takes to reach the ceiling. The two ground creates share this one
    // budget rather than getting their own: they cost the same machine the same memory, and three
    // separate allowances would just mean three times as much of it.
    options.AddPolicy(RateLimitPolicies.MatchCreate, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Behind a reverse proxy every request arrives from the proxy's address, so the per-address
// budgets above collapse into one shared bucket: a player retrying a mistyped code locks out the
// whole table. The proxy says who really called in X-Forwarded-For, but that header is a claim
// anyone can make, so it is only believed when the operator says there is a proxy in front. Opting
// in trusts whatever is directly in front of this process, which is what a container behind an
// ingress it does not own can do.
//
// Read from the built configuration, like the persistence path above, so a host that layers the
// setting in during startup is seen rather than missed.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
{
    if (!ReadTrustForwardedHeaders(configuration))
    {
        return;
    }

    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
var features = app.Services.GetRequiredService<FeatureFlags>();
var matchDatabasePath = ReadMatchDatabasePath(app.Configuration);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Every failure a request can end in is turned into problem+json here. The deliberate refusals -
// the types the services throw on purpose, with a message written for the player - carry that
// message to the client. Anything else is a fault in the server, not in the request: it is logged
// with its stack and answered with a line that says so and nothing more, because an exception
// message from somewhere unexpected is exactly the kind of text that should not leave the machine.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The caller hung up. Nothing to answer and nobody to answer it to.
        throw;
    }
#pragma warning disable CA1031 // The whole point of this middleware is to answer everything.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        // Once any of the response has gone out there is no status line left to rewrite, and
        // trying throws a second exception on top of the first. Let the host tear the connection
        // down instead of masking the original fault.
        if (context.Response.HasStarted)
        {
            throw;
        }

        var (status, title, detail) = ex switch
        {
            // Checked before its base type, InvalidOperationException, or it would land as a 400.
            NotFoundException => (StatusCodes.Status404NotFound, "Request could not be completed", ex.Message),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Action not allowed", ex.Message),
            // The write failed after memory had already moved on; the game is playable and the
            // file is behind it. Told as a busy server, which is what it almost always is, with a
            // stable line rather than SQLite's.
            SqliteException => (StatusCodes.Status503ServiceUnavailable, "Storage unavailable",
                "The server could not write the game to storage. Try again in a moment."),
            InvalidOperationException or ArgumentException or KeyNotFoundException
                or JsonException or FormatException or OverflowException
                => (StatusCodes.Status400BadRequest, "Request could not be completed", ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "Something went wrong on the server."),
        };

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            ServerLog.StorageFailed(app.Logger, ex, context.Request.Method, context.Request.Path);
        }
        else if (status == StatusCodes.Status500InternalServerError)
        {
            ServerLog.Unhandled(app.Logger, ex, context.Request.Method, context.Request.Path);
        }

        await WriteProblem(context, status, title, detail);
    }
});

// Placed ahead of everything that reads the caller's address - the rate limiter above all - and
// only when the operator has said there is a proxy to believe.
if (ReadTrustForwardedHeaders(app.Configuration))
{
    app.UseForwardedHeaders();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    // The API serves JSON, never a page, so nothing it returns has any business being framed.
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    await next();
});

app.UseCors();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ForceSignal.Api" }))
    .WithName("GetHealth")
    .WithTags("Operations")
    .WithSummary("Reports API liveness.")
    .Produces(StatusCodes.Status200OK);
// Readiness reports what the server is doing, not what it was asked to do. Those differ in exactly
// one case and it is the case that matters: a database file that will not open leaves the server
// running on memory, deliberately, so one bad file does not end every game on the machine. Reading
// the configured path alone said "sqlite" anyway - so the operator believed their games survived a
// restart when they did not, and the container's own healthcheck called that stack healthy. The
// store the match service was actually handed is the only honest answer to the question.
app.MapGet("/ready", (IMatchStore matchStore) =>
{
    var durable = matchStore is not NoMatchStore;
    return Results.Ok(new
    {
        status = "ready",
        service = "ForceSignal.Api",
        environment = app.Environment.EnvironmentName,
        cors = allowedOrigins.AllowAnyOrigin ? "development-private-network" : "configured",
        persistence = durable ? "sqlite" : "in-memory",
        features = features.ToDto(),
        warnings = ReadDeploymentWarnings(builder.Configuration, app.Environment, allowedOrigins, features, matchDatabasePath, durable)
    });
})
    .WithName("GetReadiness")
    .WithTags("Operations")
    .WithSummary("Reports API readiness and deployment-critical configuration state.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/api/features", () => Results.Ok(features.ToDto()))
    .WithName("GetFeatures")
    .WithTags("Operations")
    .WithSummary("Reports which optional game engines this server offers.")
    .Produces<FeatureFlagsDto>();

app.MapHub<MatchHub>("/hubs/match");

// Ground-combat engines mount their endpoints here. A disabled engine is not mapped at all, so its
// routes 404 rather than existing in a half-wired state - which is the point of the flag.
if (features.StarGrunt)
{
    app.MapStarGruntEndpoints();
}

if (features.Dirtside)
{
    app.MapDirtsideEndpoints();
}

// What each service could not bring back from the file is said at startup, once per row, so a
// game that vanished on restart is explained in the log rather than nowhere. The ground services
// are only asked when their engine is on: resolving one opens its table, and an engine that is off
// should leave no trace.
ReportSkippedSaves(app, "Full Thrust", app.Services.GetRequiredService<IMatchService>().SkippedSaves);
if (features.StarGrunt)
{
    ReportSkippedSaves(app, "StarGrunt", app.Services.GetRequiredService<IStarGruntGameService>().SkippedSaves);
}

if (features.Dirtside)
{
    ReportSkippedSaves(app, "Dirtside", app.Services.GetRequiredService<IDirtsideGameService>().SkippedSaves);
}

// The Full Thrust match, fleet, ship and ordnance routes. Always mapped: this is the game the
// server exists for.
app.MapMatchEndpoints();

app.Run();

static Task WriteProblem(HttpContext context, int status, string title, string detail)
{
    context.Response.StatusCode = status;
    return Results.Problem(detail, statusCode: status, title: title).ExecuteAsync(context);
}

static CorsOriginSettings ReadAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
{
    var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? SplitOrigins(configuration["FORCESIGNAL_CORS_ALLOWED_ORIGINS"])
        ?? SplitOrigins(configuration["Cors__AllowedOrigins"]);

    if (configured is { Length: > 0 })
    {
        var origins = configured
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (origins.Length > 0)
        {
            return new CorsOriginSettings(false, origins);
        }
    }

    if (environment.IsDevelopment())
    {
        return new CorsOriginSettings(true, []);
    }

    throw new InvalidOperationException("Cors:AllowedOrigins must be configured outside Development.");
}

static string[]? SplitOrigins(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? null
        : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

// Where matches are written, or null when they are only held in memory.
static string? ReadMatchDatabasePath(IConfiguration configuration) =>
    configuration["Persistence:MatchDatabasePath"] ?? configuration["FORCESIGNAL_MATCH_DB"];

// Opens one engine's durable store, or falls back to memory when the file will not open.
//
// A SQLite file that was truncated by a bad shutdown, is not a database at all, or sits somewhere the
// process cannot write throws from the store's constructor. Left to propagate, that throw comes out of
// a DI factory the first time anything needs a match, and the host dies - so one damaged file costs
// every table on the machine, including the two engines whose own tables were fine and every game that
// had nothing stored in the first place. The whole point of persistence here is that a restart does
// not end the game; taking the server down over it inverts that.
//
// So the server keeps serving without a disk behind it: games still play, they just do not survive a
// restart, which is exactly the in-memory mode a server with no path configured runs in and which
// readiness already warns about. The file is left untouched rather than recreated, because an operator
// reading the error should still be able to recover what is in it.
static IMatchStore OpenMatchStore(IServiceProvider services, string engine, string tableName)
{
    var path = ReadMatchDatabasePath(services.GetRequiredService<IConfiguration>());
    if (string.IsNullOrWhiteSpace(path))
    {
        return NoMatchStore.Instance;
    }

    try
    {
        return new SqliteMatchStore(path, tableName);
    }
    catch (Exception error) when (error is SqliteException or IOException or UnauthorizedAccessException or ArgumentException)
    {
        ServerLog.StoreUnavailable(
            services.GetRequiredService<ILoggerFactory>().CreateLogger("ForceSignal.Api.Persistence"),
            error,
            engine,
            path);
        return NoMatchStore.Instance;
    }
}

// Whether to believe X-Forwarded-For. Off unless the operator says so, and only "true" says so:
// blank, missing or misspelt leaves the proxy untrusted, the same way a feature flag fails closed.
static bool ReadTrustForwardedHeaders(IConfiguration configuration) =>
    bool.TryParse(configuration["Proxy:TrustForwardedHeaders"], out var parsed) && parsed;

static void ReportSkippedSaves(WebApplication app, string engine, IReadOnlyList<SkippedSave> skipped)
{
    if (skipped.Count == 0)
    {
        return;
    }

    ServerLog.SkippedSaves(app.Logger, skipped.Count, engine);
    foreach (var save in skipped)
    {
        ServerLog.SkippedSave(app.Logger, engine, save.MatchId, save.Reason);
    }
}

static string[] ReadDeploymentWarnings(
    IConfiguration configuration,
    IHostEnvironment environment,
    CorsOriginSettings allowedOrigins,
    FeatureFlags features,
    string? matchDatabasePath,
    bool durable)
{
    var warnings = new List<string>();

    // Reported in every environment, because the reason to read readiness before a game is to find
    // out what will happen if the machine hiccups.
    if (string.IsNullOrWhiteSpace(matchDatabasePath))
    {
        warnings.Add("Matches are stored in memory and will be lost on API restart. Set Persistence:MatchDatabasePath to keep them.");
    }
    else if (!durable)
    {
        // Storage was asked for and could not be had. Silence here is worse than the fallback it
        // describes: the games play, so nothing looks wrong until the restart that ends all of them.
        // The path is left out on purpose - this route answers anyone who can reach it, and the API
        // log already names the file for whoever has to go and fix it.
        warnings.Add(
            "Matches are stored in memory and will be lost on API restart: the configured database could not be "
            + "opened, so the server fell back. The file has been left as it is; see the API log to recover it.");
    }

    // An in-progress engine being switched on is worth saying out loud, because the reason to
    // check readiness before a game is to find out what is about to be in the way.
    if (features.StarGrunt)
    {
        warnings.Add("StarGrunt ground combat is enabled and is still in development.");
    }

    if (features.Dirtside)
    {
        warnings.Add(
            "Dirtside ground combat is enabled. Direct fire, close assault, systems-down recovery and the "
            + "activation sequence are playable; opportunity fire, area-defence interception and indirect "
            + "fire are not yet reachable from this API.");
    }

    if (environment.IsDevelopment())
    {
        return [.. warnings];
    }

    if (allowedOrigins.Origins.Any(IsLocalOrigin))
    {
        warnings.Add("CORS includes a localhost origin; replace it before public deployment.");
    }

    return [.. warnings];
}

// Whether an origin is on this machine or on a private network. Used only in Development, to
// decide which origins the CORS policy will reflect. The addresses allowed are the loopbacks, the
// three private IPv4 ranges, IPv4 link-local, IPv6 unique-local and link-local, and mDNS .local
// names - which between them cover every way a tablet reaches a laptop on the same wifi, and none
// of the ways a page on the internet reaches it.
static bool IsLocalNetworkOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    var host = uri.Host;
    if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!System.Net.IPAddress.TryParse(host, out var address))
    {
        return false;
    }

    if (System.Net.IPAddress.IsLoopback(address))
    {
        return true;
    }

    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
    {
        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,
            169 => octets[1] == 254,
            _ => false,
        };
    }

    // IPv6 unique-local (fc00::/7) and link-local (fe80::/10).
    var bytes = address.GetAddressBytes();
    return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
}

static bool IsLocalOrigin(string origin) =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase));

internal sealed record CorsOriginSettings(bool AllowAnyOrigin, string[] Origins);

/// <summary>
/// Adapts the host's configuration to the one-method view the Application layer asks for, so that
/// layer does not take a dependency on the hosting configuration stack to read two booleans.
/// </summary>
internal sealed class ConfigurationFeatureSource(IConfiguration configuration) : FeatureFlags.IConfigurationSource
{
    public string? GetValue(string key) => configuration[key];
}

/// <summary>
/// Provides a public marker type for API integration test hosting.
/// </summary>
public partial class Program;
