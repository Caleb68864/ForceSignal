namespace ForceSignal.Api.Endpoints;

/// <summary>
/// The names of the rate-limit policies, shared between where they are defined and where a route
/// asks for one.
/// </summary>
/// <remarks>
/// A policy is registered by name and required by name, in two different files, and a typo in
/// either is a route with no limit and no error. One set of constants is what keeps the two in step.
/// </remarks>
internal static class RateLimitPolicies
{
    /// <summary>Routes that turn a room code into a match, which is what a guesser hammers.</summary>
    public const string RoomCodeLookup = "room-code-lookup";

    /// <summary>Restoring a match from a file, which parses and allocates a whole match.</summary>
    public const string Restore = "match-restore";

    /// <summary>Creating a match, which needs no credentials and allocates state kept for a day.</summary>
    public const string MatchCreate = "match-create";
}
