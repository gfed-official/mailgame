namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Namespace of a <see cref="StringId"/>: one value per def type (assigned by content-data,
/// ADR-0010) plus <see cref="Run"/> for the per-run intern table.
/// </summary>
public enum StringNamespace : byte
{
    /// <summary>No namespace; the <see cref="StringId.None"/> value.</summary>
    None = 0,

    /// <summary>Item defs; equals <c>DefType.Item</c>.</summary>
    Item = 1,

    /// <summary>Container defs; equals <c>DefType.Container</c>.</summary>
    Container = 2,

    /// <summary>Mail-kind defs; equals <c>DefType.MailKind</c>.</summary>
    MailKind = 3,

    /// <summary>Destination-type defs; equals <c>DefType.DestinationType</c>.</summary>
    DestinationType = 4,

    /// <summary>Mail-mix defs; equals <c>DefType.MailMix</c>.</summary>
    MailMix = 5,

    /// <summary>
    /// Framework and owner UI reason and prompt keys (ADR-0004 section 6 erratum 2026-10-23; indices 0 to 15 are the framework's
    /// <c>reason.*</c> keys). Evan approved (TD-194 item 1), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019": appended here by player-character 019 for
    /// <c>PlayerReasons</c> rather than waiting for manual-delivery 010 AC-4.
    /// </summary>
    Reason = 6,

    /// <summary>Per-run interned strings.</summary>
    Run = 0xFF,
}

/// <summary>
/// A string reference <c>(StringNamespace ns, ushort index)</c>. <c>default</c> equals
/// <see cref="None"/> (<c>ns == None</c>). Hashes as <c>((int)Ns &lt;&lt; 16) | Index</c> so equal
/// indexes in different namespaces do not collide.
/// </summary>
/// <example>
/// <code>
/// var id = new StringId(StringNamespace.Run, 3);
/// int hash = id.GetHashCode();   // 0xFF0003
/// </code>
/// </example>
public readonly struct StringId : IEquatable<StringId>
{
    /// <summary>The namespace.</summary>
    public readonly StringNamespace Ns;

    /// <summary>The index within <see cref="Ns"/>.</summary>
    public readonly ushort Index;

    /// <summary>Creates a string id.</summary>
    /// <param name="ns">The namespace.</param>
    /// <param name="index">The index within the namespace.</param>
    public StringId(StringNamespace ns, ushort index)
    {
        Ns = ns;
        Index = index;
    }

    /// <summary>The "no string" id; equal to <c>default</c>.</summary>
    public static StringId None => default;

    /// <summary>True when <see cref="Ns"/> is <see cref="StringNamespace.None"/>.</summary>
    public bool IsNone => Ns == StringNamespace.None;

    /// <inheritdoc />
    public bool Equals(StringId other) => Ns == other.Ns && Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StringId other && Ns == other.Ns && Index == other.Index;

    /// <inheritdoc />
    public override int GetHashCode() => ((int)Ns << 16) | Index;

    /// <summary>Equality on namespace and index.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(StringId left, StringId right) => left.Ns == right.Ns && left.Index == right.Index;

    /// <summary>Inequality on namespace or index.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(StringId left, StringId right) => left.Ns != right.Ns || left.Index != right.Index;
}
