using ForceSignal.Application.Matches;

namespace ForceSignal.Application.Ground;

/// <summary>
/// What every ground-combat service has in common with the others, above the rules: a token that
/// gates a game, and a record of what it could not bring back at startup.
/// </summary>
/// <remarks>
/// The StarGrunt and Dirtside services are deliberately separate - different rules, different
/// wire shapes - but the API needs one way to ask "may this caller touch this game?" so a single
/// endpoint filter can guard both engines rather than each growing its own copy.
/// </remarks>
public interface IGroundGameService
{
    /// <summary>
    /// Checks the token a caller presented against the game it is trying to reach.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="token">The token from the request.</param>
    /// <exception cref="NotFoundException">There is no such game.</exception>
    /// <exception cref="UnauthorizedAccessException">The token is not this game's.</exception>
    void RequireToken(Guid gameId, string token);

    /// <summary>The stored rows that could not be brought back at startup, so the host can say so.</summary>
    IReadOnlyList<SkippedSave> SkippedSaves { get; }
}
