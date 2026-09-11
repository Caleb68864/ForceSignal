using ForceSignal.Application.Matches;

namespace ForceSignal.Api;

/// <summary>
/// One engine's store, watched so that readiness keeps telling the truth about it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MatchStoreReport"/> recorded what each store <em>turned out to be when it was
/// opened</em>, and nothing ever revisited it. That is the right answer for exactly as long as
/// nothing changes: a volume unmounted mid-session, a disk that fills, a file that goes read-only
/// produce a 503 on every write while <c>/ready</c> goes on reporting <c>sqlite</c> with no warning,
/// and the container's own healthcheck goes on calling the stack healthy. Readiness has one
/// question to answer - "will the games on this machine survive a restart?" - and after a failed
/// write the answer for that engine is no.
/// </para>
/// <para>
/// So the answer is watched rather than remembered. A store that throws is not writing anything
/// down, and one that succeeds again is: both are recorded, and only on the change, so the ordinary
/// path costs one comparison rather than a report update per save.
/// </para>
/// <para>
/// The exception is rethrown untouched. This observes; the middleware that turns a storage failure
/// into a 503 is still the thing that answers the caller, and the game is still playable in memory
/// while the file is behind it.
/// </para>
/// </remarks>
/// <param name="inner">The store doing the work.</param>
/// <param name="report">Where readiness reads each engine's state from.</param>
/// <param name="engine">The engine, named as readiness reports it.</param>
internal sealed class ReportingMatchStore(IMatchStore inner, MatchStoreReport report, string engine) : IMatchStore
{
    private readonly Lock _gate = new();

    /// <summary>
    /// What was last reported for this engine.
    /// </summary>
    /// <remarks>
    /// Seeded true by the constructor below, which is only reached with a database actually open:
    /// the paths that fall back to memory never build one of these.
    /// </remarks>
    private bool _durable = Record(report, engine, durable: true);

    /// <inheritdoc />
    public void Save(Guid matchId, string state) => Watch(() => inner.Save(matchId, state));

    /// <inheritdoc />
    public void Remove(Guid matchId) => Watch(() => inner.Remove(matchId));

    /// <inheritdoc />
    public IReadOnlyList<StoredMatch> LoadAll()
    {
        IReadOnlyList<StoredMatch> loaded = [];
        Watch(() => loaded = inner.LoadAll());
        return loaded;
    }

    private void Watch(Action work)
    {
        try
        {
            work();
        }
#pragma warning disable CA1031 // Every failure is the same failure here: this store is not writing.
        catch (Exception)
#pragma warning restore CA1031
        {
            Note(durable: false);
            throw;
        }

        Note(durable: true);
    }

    private void Note(bool durable)
    {
        lock (_gate)
        {
            if (_durable == durable)
            {
                return;
            }

            _durable = Record(report, engine, durable);
        }
    }

    private static bool Record(MatchStoreReport report, string engine, bool durable)
    {
        report.Record(engine, durable);
        return durable;
    }
}
