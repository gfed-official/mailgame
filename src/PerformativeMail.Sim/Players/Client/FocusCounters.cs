using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// One focus pass's budget probes (D3, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164): the test seam story 015
/// extends by <c>ref</c>. <see cref="FocusStep"/> overwrites it every tick and raises <see cref="CounterId.FocusCandidates"/> and
/// <see cref="CounterId.FocusCellsVisited"/> to it (running maxima, the <c>CarrierCounterFlush</c> pattern; Mail Lead planning default,
/// pending Evan (TD-170 item 2)).
/// </summary>
/// <example><code>ref readonly FocusCounters c = ref focusStep.Probes; Assert.True(c.CellsVisited &lt;= 25);</code></example>
public struct FocusCounters
{
    /// <summary>Anchors past the context filter and within reach (the ones whose cone and score were evaluated; Mail Lead planning default, pending Evan (TD-170 item 2)).</summary>
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
