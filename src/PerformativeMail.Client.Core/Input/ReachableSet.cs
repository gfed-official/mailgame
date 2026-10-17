using System.Collections.Immutable;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The set of reachable router states: every id of the 1008-tuple space that no <see cref="ReachabilityRule"/> forbids,
/// built once (a 1008-bit bitset plus an ascending id list). <see cref="Default"/> is built from <see cref="ReachableRules.All"/>
/// and is the instance the conflict validator (story 006), the router's SIM_ASSERT and the H-R2-8 golden test share.
/// <see cref="Contains"/> is one bit test, 0 B. Immutable, thread-safe.
/// </summary>
/// <example>
/// <code>
/// bool ok = ReachableSet.Default.Contains(stateId);
/// foreach (ushort id in ReachableSet.Default.Ids) { /* visit every reachable state, ascending */ }
/// ReachableSet mutated = ReachableSet.Build(ReachableRules.All.RemoveAt(ReachableRules.AimingIndex));
/// </code>
/// </example>
public sealed class ReachableSet
{
    private const int WordCount = (RouterStateInfo.StateCount + 63) / 64;

    /// <summary>The set built from <see cref="ReachableRules.All"/>.</summary>
    public static readonly ReachableSet Default = Build(ReachableRules.All);

    private readonly ulong[] _bits;
    private readonly ushort[] _ids;

    private ReachableSet(ulong[] bits, ushort[] ids)
    {
        _bits = bits;
        _ids = ids;
    }

    /// <summary>The number of reachable states.</summary>
    public int Count => _ids.Length;

    /// <summary>Every reachable id, ascending. The backing array is never exposed for mutation.</summary>
    public ReadOnlySpan<ushort> Ids => _ids;

    /// <summary>Builds the set of states no rule in <paramref name="rules"/> forbids. Allocates; run once at startup or in tests.</summary>
    /// <param name="rules">The rule list.</param>
    /// <returns>The set.</returns>
    /// <example><code>ReachableSet set = ReachableSet.Build(ReachableRules.All);</code></example>
    public static ReachableSet Build(ImmutableArray<ReachabilityRule> rules)
    {
        var bits = new ulong[WordCount];
        var ids = new List<ushort>(RouterStateInfo.StateCount);
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            ushort id = (ushort)i;
            if (ReachableRules.FirstViolation(rules, id) < 0)
            {
                bits[id >> 6] |= 1UL << (id & 63);
                ids.Add(id);
            }
        }

        return new ReachableSet(bits, ids.ToArray());
    }

    /// <summary>True when <paramref name="stateId"/> is a valid id that no rule forbids.</summary>
    /// <param name="stateId">The packed router state id.</param>
    /// <returns>Whether the state is in the set.</returns>
    /// <example><code>SimAssert.That(ReachableSet.Default.Contains(next), 201);</code></example>
    public bool Contains(ushort stateId) =>
        stateId < RouterStateInfo.StateCount && (_bits[stateId >> 6] & (1UL << (stateId & 63))) != 0;
}
