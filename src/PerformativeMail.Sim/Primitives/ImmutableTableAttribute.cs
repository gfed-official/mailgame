namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Marks a wrapper type whose instances are immutable tables (ADR-0003 section 4 static-state rule): once constructed, no
/// member can change what the instance holds. The SC-ARCH-1 static-state architecture test accepts a <c>static readonly</c>
/// field of a type carrying this attribute; a bare <c>static readonly T[]</c> stays forbidden because an array is mutable.
/// The attribute is a promise checked by review, not by the compiler: the wrapper must keep its storage private and expose
/// only reads (for example a <see cref="ReadOnlySpan{T}"/> or an indexer without a setter).
/// </summary>
/// <example>
/// <code>
/// [ImmutableTable]
/// public sealed class SpeedTable
/// {
///     private readonly int[] _values;
///     public SpeedTable(ReadOnlySpan&lt;int&gt; values) { _values = values.ToArray(); }
///     public int this[int i] => _values[i];
/// }
///
/// internal static class Speeds
/// {
///     public static readonly SpeedTable Default = new([10, 20, 30]); // allowed by the static-state rule
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ImmutableTableAttribute : Attribute
{
}
