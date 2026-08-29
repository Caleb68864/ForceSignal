namespace ForceSignal.Api;

/// <summary>
/// The lines the host writes about itself: what it could not restore, and what it could not
/// answer. Source-generated, so the message templates are checked at build time and cost nothing
/// when the level is off.
/// </summary>
internal static partial class ServerLog
{
    /// <summary>One stored row that did not come back at startup.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped stored {Engine} game {MatchId}: {Reason}")]
    public static partial void SkippedSave(ILogger logger, string engine, Guid matchId, string reason);

    /// <summary>How many rows did not come back, for the operator reading the top of the log.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} stored {Engine} game(s) could not be restored and were skipped.")]
    public static partial void SkippedSaves(ILogger logger, int count, string engine);

    /// <summary>A request that failed for a reason that was not one of the deliberate refusals.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception answering {Method} {Path}")]
    public static partial void Unhandled(ILogger logger, Exception exception, string method, string path);

    /// <summary>The database refused a write; memory may now be ahead of the file.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Storage failed answering {Method} {Path}; the in-memory game may be ahead of the file")]
    public static partial void StorageFailed(ILogger logger, Exception exception, string method, string path);
}
