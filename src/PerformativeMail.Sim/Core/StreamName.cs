namespace PerformativeMail.Sim.Core;

/// <summary>
/// Identity of one RNG stream family (ADR-0005 section 2): ordinal, UTF-8 name, owner, allowed index range
/// and the precomputed <c>Fnv1a64</c> of the name. The only meaningful instances are the
/// <c>static readonly</c> fields of <see cref="RngNames"/>; the constructor is <c>internal</c> (C# cannot
/// grant a private constructor to a sibling class), so code outside the Sim assembly cannot mint one.
/// <c>default(StreamName)</c> is not a registered name and the registry refuses it.
/// </summary>
/// <example>
/// <code>
/// ulong seed = RngDerive.Derive(runSeed, in RngNames.Mail, 2);
/// bool mine = RngNames.Mail.Owner == SystemId.MailDestinations;   // true
/// </code>
/// </example>
public readonly struct StreamName
{
    private readonly ushort _ordinal;
    private readonly ulong _fnv;
    private readonly SystemId _owner;
    private readonly uint _minIndex;
    private readonly uint _maxIndex;

    internal StreamName(ushort ordinal, ulong fnv, SystemId owner, uint minIndex, uint maxIndex)
    {
        _ordinal = ordinal;
        _fnv = fnv;
        _owner = owner;
        _minIndex = minIndex;
        _maxIndex = maxIndex;
    }

    /// <summary>Position in <see cref="RngNames"/> declaration order (0-based); the "name ordinal" of the state dump.</summary>
    public ushort Ordinal => _ordinal;

    /// <summary>The ASCII name as UTF-8 bytes (a literal-backed span, no allocation); empty for <c>default</c>.</summary>
    public ReadOnlySpan<byte> Name => _fnv == 0 ? default : RngNames.NameOf(_ordinal);

    /// <summary>The FNV-1a 64 hash of <see cref="Name"/>, computed once when <see cref="RngNames"/> initialises.</summary>
    public ulong Fnv => _fnv;

    /// <summary>The system allowed to create this stream.</summary>
    public SystemId Owner => _owner;

    /// <summary>Lowest allowed stream index (inclusive).</summary>
    public uint MinIndex => _minIndex;

    /// <summary>Highest allowed stream index (inclusive).</summary>
    public uint MaxIndex => _maxIndex;

    /// <summary>Number of allowed indices, <c>MaxIndex - MinIndex + 1</c> (0 for <c>default</c>).</summary>
    public int IndexCount => _fnv == 0 ? 0 : (int)(_maxIndex - _minIndex) + 1;
}
