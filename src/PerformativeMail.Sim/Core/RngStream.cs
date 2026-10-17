using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// One deterministic random stream: a PCG32 generator plus a draw counter. Not thread-safe; a stream has a
/// single owner. There is deliberately no float, bool-with-probability, rejection-sampling or <c>int</c> API
/// (ADR-0005 section 4); a Bernoulli trial of p permille is <c>NextInt(1000) &lt; p</c>.
/// </summary>
/// <example>
/// <code>
/// IRngStreams view = registry.ViewFor(SystemId.MailDestinations);
/// RngStream stream = view.Create(in RngNames.Mail, 1);
/// uint roll = stream.NextInt(10);   // 0..9, exactly one draw
/// </code>
/// </example>
public sealed class RngStream
{
    // Not readonly: Pcg32 is a mutable struct and a readonly field would mutate a defensive copy.
    private Pcg32 _pcg;
    private readonly IRngSource? _source;
    private ulong _draws;

    private StreamName _name;
    private uint _index;

    /// <summary>Creates a stream for (<paramref name="name"/>, <paramref name="index"/>) seeded from a derived seed (see <see cref="RngDerive.Derive(ulong, in StreamName, uint)"/>).</summary>
    /// <param name="name">The stream name.</param>
    /// <param name="index">The stream index.</param>
    /// <param name="derived">The derived 64-bit seed.</param>
    internal RngStream(in StreamName name, uint index, ulong derived)
    {
        _name = name;
        _index = index;
        _pcg = RngDerive.Seed(derived);
    }

    /// <summary>Test constructor: draws come from <paramref name="source"/> instead of PCG32; <see cref="Name"/> is <c>default</c> and <see cref="Index"/> 0.</summary>
    /// <param name="source">The stub supplying raw 32-bit draws.</param>
    internal RngStream(IRngSource source)
    {
        _source = source;
    }

    /// <summary>The stream's name (<c>default</c> for a stub-backed stream).</summary>
    public StreamName Name => _name;

    /// <summary>The stream's index within its name's range (the requested index for a spare-slot stream).</summary>
    public uint Index => _index;

    /// <summary>The number of draws taken since creation or the last reseed (every <see cref="Next32"/> and <see cref="NextInt"/> call, including <c>NextInt(0)</c>).</summary>
    public ulong Draws => _draws;

    /// <summary>The PCG32 state (for the state dump). Meaningless for a stub-backed stream.</summary>
    internal ulong State => _pcg.State;

    /// <summary>The PCG32 increment (for the state dump). Meaningless for a stub-backed stream.</summary>
    internal ulong Inc => _pcg.Inc;

    /// <summary>Re-seeds in place from a derived seed and resets <see cref="Draws"/> to 0; <see cref="Name"/> and <see cref="Index"/> are unchanged.</summary>
    /// <param name="derived">The derived 64-bit seed.</param>
    internal void Reseed(ulong derived)
    {
        _pcg = RngDerive.Seed(derived);
        _draws = 0;
    }

    /// <summary>Re-seeds in place for a (possibly different) key, as used for spare slots, and resets <see cref="Draws"/> to 0.</summary>
    /// <param name="name">The stream name.</param>
    /// <param name="index">The stream index.</param>
    /// <param name="derived">The derived 64-bit seed.</param>
    internal void Reseed(in StreamName name, uint index, ulong derived)
    {
        _name = name;
        _index = index;
        Reseed(derived);
    }

    /// <summary>Returns the next 32 random bits. Counts one draw. Allocation-free.</summary>
    /// <returns>A 32-bit value.</returns>
    public uint Next32()
    {
        _draws++;
        return _source is null ? _pcg.Next32() : _source.Next32();
    }

    /// <summary>
    /// Returns a value in [0, <paramref name="bound"/>) via <c>(next32 * bound) &gt;&gt; 32</c>. Always exactly one
    /// draw, including <paramref name="bound"/> = 0 (returns 0; asserts under SIM_ASSERT after consuming the draw).
    /// </summary>
    /// <param name="bound">The exclusive upper bound; integer literals bind here.</param>
    /// <returns>A value below <paramref name="bound"/>, or 0 when it is 0.</returns>
    public uint NextInt(uint bound)
    {
        uint x = Next32();
        SimAssert.That(bound != 0, SimAssertCode.RngBoundZero);
        return unchecked((uint)(((ulong)x * bound) >> 32));
    }
}
