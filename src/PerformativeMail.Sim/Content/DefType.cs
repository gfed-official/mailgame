namespace PerformativeMail.Sim.Content;

/// <summary>
/// The def types, in the append-only order used by the canonical encoding (content-data F6) and as
/// the numeric value of the matching <see cref="Primitives.StringNamespace"/> member. A member is
/// never renumbered, reordered or reused; later types are appended with the next value.
/// </summary>
/// <example>
/// <code>
/// StringNamespace ns = (StringNamespace)(byte)DefType.MailKind;   // StringNamespace.MailKind
/// </code>
/// </example>
public enum DefType : byte
{
    /// <summary>Item defs.</summary>
    Item = 1,

    /// <summary>Container defs.</summary>
    Container = 2,

    /// <summary>Mail-kind defs.</summary>
    MailKind = 3,

    /// <summary>Destination-type defs.</summary>
    DestinationType = 4,

    /// <summary>Mail-mix defs.</summary>
    MailMix = 5,
}
