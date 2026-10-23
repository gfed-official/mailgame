using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// One focus pass's budget probes (Mail Lead planning default D3, pending Evan, TD-164): the test seam story 015 extends by
/// <c>ref</c>. <see cref="FocusStep"/> overwrites it every tick and raises <see cref="CounterId.FocusCandidates"/> and
/// <see cref="CounterId.FocusCellsVisited"/> to it (running maxima, the <c>CarrierCounterFlush</c> pattern).
/// </summary>
/// <example><code>ref readonly FocusCounters c = ref focusStep.Probes; Assert.True(c.CellsVisited &lt;= 25);</code></example>
public struct FocusCounters
{
    /// <summary>Anchors past the context filter and within reach (the ones whose cone and score were evaluated).</summary>
    public int Candidates;

    /// <summary>Anchor-grid cells walked.</summary>
    public int CellsVisited;

    /// <summary>Raises the two counters in <paramref name="counters"/> to these values.</summary>
    /// <param name="counters">The ClientLayer's counters.</param>
    internal readonly void RaiseInto(SimCounters counters)
    {
        if (Candidates > counters[CounterId.FocusCandidates])
        {
            counters.Set(CounterId.FocusCandidates, Candidates);
        }

        if (CellsVisited > counters[CounterId.FocusCellsVisited])
        {
            counters.Set(CounterId.FocusCellsVisited, CellsVisited);
        }
    }
}
