using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// One authored balance knob: key, unit, M-1a default and the registry hard range, with an optional data-file safe range.
/// An owner's table of knobs yields its registry rows and its default entries from one place, so a key's range and its
/// value cannot drift apart. Internal; immutable.
/// </summary>
/// <param name="Key">The key within its owner (<c>walkMmPerSec</c>, <c>throwSpeed.light</c>).</param>
/// <param name="Unit">The stored unit.</param>
/// <param name="Default">The M-1a value, already scaled (CD4).</param>
/// <param name="HardMin">Registry hard minimum.</param>
/// <param name="HardMax">Registry hard maximum.</param>
/// <param name="SafeMin">Safe minimum, when <see cref="HasSafe"/>.</param>
/// <param name="SafeMax">Safe maximum, when <see cref="HasSafe"/>.</param>
/// <param name="HasSafe">Whether the entry carries a safe-range annotation.</param>
/// <example>
/// <code>
/// var k = new BalanceKnob("ticksAccel", BalanceUnit.Ticks, 4, 0, 100_000, 3, 8);
/// BalanceRow row = BalanceKnob.ToRows("player", ImmutableArray.Create(k))[0];
/// </code>
/// </example>
internal readonly record struct BalanceKnob(
    string Key, BalanceUnit Unit, long Default, long HardMin, long HardMax, long SafeMin, long SafeMax, bool HasSafe)
{
    public BalanceKnob(string key, BalanceUnit unit, long value, long hardMin, long hardMax)
        : this(key, unit, value, hardMin, hardMax, 0, 0, false)
    {
    }

    public BalanceKnob(string key, BalanceUnit unit, long value, long hardMin, long hardMax, long safeMin, long safeMax)
        : this(key, unit, value, hardMin, hardMax, safeMin, safeMax, true)
    {
    }

    /// <summary>The registry rows of <paramref name="knobs"/> under <paramref name="owner"/>.</summary>
    public static ImmutableArray<BalanceRow> ToRows(string owner, ImmutableArray<BalanceKnob> knobs)
    {
        ImmutableArray<BalanceRow>.Builder rows = ImmutableArray.CreateBuilder<BalanceRow>(knobs.Length);
        foreach (BalanceKnob k in knobs)
        {
            rows.Add(new BalanceRow(owner, k.Key, k.Unit, k.HardMin, k.HardMax));
        }

        return rows.MoveToImmutable();
    }

    /// <summary>The default entries of <paramref name="knobs"/> under <paramref name="owner"/>, from the built-in source.</summary>
    public static ImmutableArray<BalanceEntry> ToEntries(string owner, ImmutableArray<BalanceKnob> knobs)
    {
        ImmutableArray<BalanceEntry>.Builder entries = ImmutableArray.CreateBuilder<BalanceEntry>(knobs.Length);
        SourceLocation where = SourceLocation.BuiltIn("builtin/balance/" + owner);
        foreach (BalanceKnob k in knobs)
        {
            var entry = new BalanceEntry(owner, owner + "." + k.Key, k.Default, where);
            entries.Add(k.HasSafe ? entry with { Safe = new BalanceSafeRange(k.SafeMin, k.SafeMax) } : entry);
        }

        return entries.MoveToImmutable();
    }
}
