using System.Reflection;
using ForceSignal.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Keeps the hub's remotely invokable surface to what is actually called.
/// </summary>
/// <remarks>
/// Every public method on a <see cref="Hub"/> can be invoked by anyone who can reach the hub, which
/// is a different thing from an unused method on an ordinary class: it is not dead code, it is
/// reachable code with no caller to say what it is for. LeaveMatchGroup was one - a connection lives
/// exactly as long as a seat at a match, so leaving is stopping, and stopping is already answered.
/// </remarks>
public sealed class MatchHubSurfaceTests
{
    [Fact]
    public void TheHubOffersOnlyTheCallsTheClientMakes()
    {
        var callable = typeof(MatchHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialAccessor())
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // JoinMatchGroup is what main.tsx invokes, on connect and again after every reconnect.
        // OnDisconnectedAsync is an override rather than a call a client can make.
        Assert.Equal(["JoinMatchGroup", "OnDisconnectedAsync"], callable);
    }
}

file static class MethodInfoExtensions
{
    /// <summary>True for the compiler-generated get_/set_ pair behind a property.</summary>
    internal static bool IsSpecialAccessor(this MethodInfo method) => method.IsSpecialName;
}
