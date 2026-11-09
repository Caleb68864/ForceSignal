using System.Reflection;
using System.Security.Claims;
using ForceSignal.Api.Hubs;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ForceSignal.Api.Tests;

/// <summary>
/// The hub is the only thing that tells one device whether the other is there, so what it reports
/// has to follow the connections that exist - and leave a trace someone debugging a two-device game
/// can read.
/// </summary>
public sealed class MatchHubPresenceTests
{
    [Fact]
    public async Task JoiningTwiceOnOneConnection_DoesNotLeaveTheSeatConnectedAfterItCloses()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Hub", Rules: RulesProfile.Empty));
        var hub = Hub(service, new CapturingLogger(), new FakeContext("c1"));

        await hub.JoinMatchGroup(owner.MatchId.ToString(), owner.ParticipantToken);
        await hub.JoinMatchGroup(owner.MatchId.ToString(), owner.ParticipantToken);
        await hub.OnDisconnectedAsync(null);

        Assert.False(service.GetSnapshot(owner.MatchId).Participants.Single().IsConnected);
    }

    [Fact]
    public async Task ARefusedJoinAndADroppedConnection_AreBothLogged()
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Hub", Rules: RulesProfile.Empty));
        var logger = new CapturingLogger();
        var hub = Hub(service, logger, new FakeContext("c2"));

        await Assert.ThrowsAsync<HubException>(() => hub.JoinMatchGroup(owner.MatchId.ToString(), "not-the-token"));
        await hub.JoinMatchGroup(owner.MatchId.ToString(), owner.ParticipantToken);
        await hub.OnDisconnectedAsync(new IOException("transport died"));

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("refused", StringComparison.Ordinal) && e.Message.Contains("c2", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Exception is IOException && e.Message.Contains("c2", StringComparison.Ordinal));
        // The token is a credential; it never goes into the log.
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(owner.ParticipantToken, StringComparison.Ordinal));
    }

    private static MatchHub Hub(IMatchService service, CapturingLogger logger, HubCallerContext context) => new(service, logger)
    {
        Context = context,
        Groups = new FakeGroups(),
        Clients = ProxyOf<IHubCallerClients>(),
    };

    private static T ProxyOf<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, InertProxy>();
        return proxy;
    }

    /// <summary>Answers every interface call with another inert proxy, or a completed task.</summary>
    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returns = targetMethod!.ReturnType;
            if (returns == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returns.IsInterface)
            {
                var create = typeof(DispatchProxy).GetMethod(nameof(DispatchProxy.Create), 2, Type.EmptyTypes)!
                    .MakeGenericMethod(returns, typeof(InertProxy));
                return create.Invoke(null, null);
            }

            return null;
        }
    }

    private sealed class FakeGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId => connectionId;

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }

    internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    internal sealed class CapturingLogger : ILogger<MatchHub>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
