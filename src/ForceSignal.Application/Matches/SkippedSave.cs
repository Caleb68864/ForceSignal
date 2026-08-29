namespace ForceSignal.Application.Matches;

/// <summary>
/// One stored row a service could not bring back at startup, and why.
/// </summary>
/// <remarks>
/// A row that cannot be read is skipped so the other games still load - but skipped silently, a
/// player's game vanishes on restart and nothing anywhere says why. Every service that restores from
/// a store keeps a list of these so the host can write them to its log and a test can count them.
/// </remarks>
/// <param name="MatchId">The row's key.</param>
/// <param name="Reason">What stopped it loading, in the words the failure gave.</param>
public readonly record struct SkippedSave(Guid MatchId, string Reason);
