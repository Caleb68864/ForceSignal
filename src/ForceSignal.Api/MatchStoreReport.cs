namespace ForceSignal.Api;

/// <summary>
/// What each engine's store actually turned out to be, recorded as that store is opened.
/// </summary>
/// <remarks>
/// <para>
/// Readiness has to answer "will the games on this machine survive a restart?", and this server has
/// one store per engine. Answering it from a single injected <c>IMatchStore</c> answered it for one
/// engine and reported that as the state of the machine: with a database file whose Full Thrust
/// table was healthy and whose Dirtside table was not, <c>/ready</c> said <c>sqlite</c> with no
/// warning while every Dirtside game went to memory and died at the next restart. That is the same
/// bug the match store was fixed for, un-carried to its twins.
/// </para>
/// <para>
/// So the answer is collected rather than deduced, and it is collected in the one place a store can
/// be opened at all. A fourth engine cannot be missed by this: opening its store is what puts it in
/// here, and <c>ReadinessReportsEveryStoreThisServerOpened</c> fails if a flag exists for an engine
/// that never turns up.
/// </para>
/// <para>
/// Only engines that were actually resolved appear. An engine whose flag is off opens no table -
/// deliberately, see the service registrations - and so has no reality to report.
/// </para>
/// </remarks>
internal sealed class MatchStoreReport
{
    private readonly Lock _gate = new();
    private readonly List<MatchStoreState> _states = [];

    /// <summary>Records what one engine's store turned out to be.</summary>
    /// <param name="engine">The engine, named as readiness reports it.</param>
    /// <param name="durable">True when it opened a database, false when it fell back to memory.</param>
    public void Record(string engine, bool durable)
    {
        lock (_gate)
        {
            _states.RemoveAll(state => string.Equals(state.Engine, engine, StringComparison.Ordinal));
            _states.Add(new MatchStoreState(engine, durable));
        }
    }

    /// <summary>Every store opened so far, in the order they were opened.</summary>
    public IReadOnlyList<MatchStoreState> Stores
    {
        get
        {
            lock (_gate)
            {
                return [.. _states];
            }
        }
    }
}

/// <summary>One engine's store, as it turned out.</summary>
/// <param name="Engine">The engine, named as readiness reports it.</param>
/// <param name="Durable">True when it opened a database, false when it fell back to memory.</param>
internal readonly record struct MatchStoreState(string Engine, bool Durable);
