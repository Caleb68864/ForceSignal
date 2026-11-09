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

    /// <summary>The store would not open at all, so this engine is running without one.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not open the {Engine} store at {Path}; this engine is running in memory and nothing it holds will survive a restart")]
    public static partial void StoreUnavailable(ILogger logger, Exception exception, string engine, string path);

    /// <summary>An open store's write failed: readiness now reports this engine as in-memory.</summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The {Engine} store stopped writing; /ready now reports it in-memory until a write succeeds")]
    public static partial void StoreStoppedWriting(ILogger logger, string engine);

    /// <summary>A store that had failed wrote successfully again.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Engine} store is writing again; /ready reports it durable")]
    public static partial void StoreWritingAgain(ILogger logger, string engine);
}
