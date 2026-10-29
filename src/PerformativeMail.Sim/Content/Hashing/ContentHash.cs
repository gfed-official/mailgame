using System.Buffers;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Hashing;

/// <summary>
/// The content hash (content-data CD25 / F6, ADR-0010 section 6):
/// <c>SHA-256(hashSchemaVersion u8 || BalanceBlock || DefBlocks)</c> over the LOADED VALUES of a <see cref="ContentSet"/> (never
/// file bytes), computed with the Sim's own <see cref="Sha256"/> (no platform crypto). The BalanceBlock is
/// <c>u16 keyCount</c> then every registry key (all <c>sim.*</c> keys included, no opt-in flag) in ordinal order as
/// <c>u16 len, UTF-8 key, i64 LE value</c>. The DefBlocks follow in the fixed append-only <see cref="DefType"/> order (Item,
/// Container, MailKind, DestinationType, MailMix), each <c>u16 count</c> then every def in DefIndex order encoded by
/// <see cref="DefCanonical"/>. Not hashed: <c>ResolvedStatBounds</c> (derived), safe ranges, source locations, suppress
/// entries, <c>$schema</c>, comments (TD-055 item 9).
/// <para>Pure and thread-safe: no static mutable state, no I/O, time or randomness. Computed once at load.</para>
/// </summary>
/// <example>
/// <code>
/// ContentSet set = BuiltInContent.CreateM1a();
/// Hash256 hash = ContentHash.Compute(set);
/// string shown = hash.ToDisplayString();               // 8 lowercase hex characters
/// Hash256 header = ContentHash.ComputeSettings(set.Settings);   // ADR-0006 section 7 recorder header
/// </code>
/// </example>
public static class ContentHash
{
    /// <summary>The layout version byte at the start of the canonical stream; bump whenever any encoding here or in <see cref="DefCanonical"/> changes.</summary>
    public const byte HashSchemaVersion = 1;

    /// <summary>Hashes the whole content set.</summary>
    /// <param name="content">The accepted set.</param>
    /// <returns>The 32-byte digest; display form is <see cref="Hash256.ToDisplayString"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ContentEncodingException">A list or string is over 65 535 entries or bytes (CV-GEN-12).</exception>
    /// <example><code>Hash256 h = ContentHash.Compute(BuiltInContent.CreateM1a());</code></example>
    public static Hash256 Compute(ContentSet content)
    {
        var sink = new Sha256Sink();
        WriteCanonical(content, sink);
        return sink.Finish();
    }

    /// <summary>
    /// Hashes the <see cref="SimSettings"/> canonical encoding (ADR-0006 section 7 recorder header):
    /// <c>SHA-256(hashSchemaVersion u8 || BalanceBlock of the sim.* keys)</c>, the keys being the same names and values the full
    /// content hash includes. <see cref="SimSettings.MaxEventReaders"/> is a constant, not a key, so it is not encoded.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The 32-byte digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    /// <example><code>Hash256 h = ContentHash.ComputeSettings(content.Settings);</code></example>
    public static Hash256 ComputeSettings(SimSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var sink = new Sha256Sink();
        WriteSettings(settings, sink);
        return sink.Finish();
    }

    /// <summary>Writes the full canonical stream of <paramref name="content"/> (the bytes <see cref="Compute"/> hashes), for equality checks and tools.</summary>
    /// <param name="content">The accepted set.</param>
    /// <param name="output">The destination.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ContentEncodingException">A list or string is over 65 535 entries or bytes (CV-GEN-12).</exception>
    /// <example><code>var buf = new ArrayBufferWriter&lt;byte&gt;(); ContentHash.WriteCanonical(set, buf);</code></example>
    public static void WriteCanonical(ContentSet content, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(output);
        var w = new CanonicalContentWriter(output);
        w.WriteU8(HashSchemaVersion);
        BalanceTable balance = content.Balance;
        WriteBalanceBlock(ref w, balance.Registry.Names.AsSpan(), balance.Values);
        WriteDefBlocks(ref w, content);
    }

    /// <summary>Writes the settings canonical stream (the bytes <see cref="ComputeSettings"/> hashes).</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="output">The destination.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <example><code>ContentHash.WriteSettings(set.Settings, buffer);</code></example>
    public static void WriteSettings(SimSettings settings, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(output);
        // The key to value mapping is restated by hand: a new sim.* key must be added here too. The test
        // ComputeSettings_CoversEveryRegisteredSimKey_WithTheValuesOfTheBalanceTable fails if one is missed.
        const string Prefix = BalanceKeys.Sim.Owner + ".";
        var pairs = new NamedValue[]
        {
            new(Prefix + BalanceKeys.Sim.OpQueueCapacityKey, settings.Core.OpQueueCapacity),
            new(Prefix + BalanceKeys.Sim.SystemOpReserveKey, settings.Core.SystemOpReserve),
            new(Prefix + BalanceKeys.Sim.MaxOpsDrainedPerTickKey, settings.Core.MaxOpsDrainedPerTick),
            new(Prefix + BalanceKeys.Sim.EventLogCapacityKey, settings.Core.EventLogCapacity),
            new(Prefix + BalanceKeys.Sim.EventLogReserveKey, settings.Core.EventLogReserve),
            new(Prefix + BalanceKeys.Sim.MaxCatchUpTicksPerFrameKey, settings.Core.MaxCatchUpTicksPerFrame),
            new(Prefix + BalanceKeys.Sim.CheckRingCapacityKey, settings.Core.CheckRingCapacity),
            new(Prefix + BalanceKeys.Sim.WarmupTicksKey, settings.WarmupTicks),
            new(Prefix + BalanceKeys.Sim.ModifierPoolPerTeamKey, settings.Stats.ModifierPoolPerTeam),
            new(Prefix + BalanceKeys.Sim.ModifierPoolPerPlayerKey, settings.Stats.ModifierPoolPerPlayer),
            new(Prefix + BalanceKeys.Sim.MailTablePagesKey, settings.MailTablePages),
            new(Prefix + BalanceKeys.Sim.StartingWalletCentsKey, settings.StartingWalletCents),
        };
        StableSort.Sort<NamedValue, ByName>(pairs.AsSpan(), new NamedValue[pairs.Length].AsSpan(), new ByName());
        string[] names = new string[pairs.Length];
        long[] values = new long[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
        {
            names[i] = pairs[i].Name;
            values[i] = pairs[i].Value;
        }

        var w = new CanonicalContentWriter(output);
        w.WriteU8(HashSchemaVersion);
        WriteBalanceBlock(ref w, names, values);
    }

    private static void WriteBalanceBlock(ref CanonicalContentWriter w, ReadOnlySpan<string> orderedNames, ReadOnlySpan<long> values)
    {
        w.WriteCount(orderedNames.Length);
        for (int i = 0; i < orderedNames.Length; i++)
        {
            w.WriteString(orderedNames[i]);
            w.WriteI64(values[i]);
        }
    }

    private static void WriteDefBlocks(ref CanonicalContentWriter w, ContentSet c)
    {
        // Fixed append-only DefType order: Item, Container, MailKind, DestinationType, MailMix. A new type appends a block
        // here and bumps HashSchemaVersion.
        ReadOnlySpan<ItemDef> items = c.Items.AsSpan();
        w.WriteCount(items.Length);
        foreach (ItemDef d in items)
        {
            d.WriteCanonical(ref w);
        }

        ReadOnlySpan<ContainerDef> containers = c.Containers.AsSpan();
        w.WriteCount(containers.Length);
        foreach (ContainerDef d in containers)
        {
            d.WriteCanonical(ref w);
        }

        ReadOnlySpan<MailKindDef> kinds = c.MailKinds.AsSpan();
        w.WriteCount(kinds.Length);
        foreach (MailKindDef d in kinds)
        {
            d.WriteCanonical(ref w);
        }

        ReadOnlySpan<DestinationTypeDef> destinations = c.DestinationTypes.AsSpan();
        w.WriteCount(destinations.Length);
        foreach (DestinationTypeDef d in destinations)
        {
            d.WriteCanonical(ref w);
        }

        ReadOnlySpan<MailMixDef> mixes = c.MailMixes.AsSpan();
        w.WriteCount(mixes.Length);
        foreach (MailMixDef d in mixes)
        {
            d.WriteCanonical(ref w);
        }
    }

    private readonly record struct NamedValue(string Name, long Value);

    private readonly struct ByName : IComparer<NamedValue>
    {
        public int Compare(NamedValue x, NamedValue y) => string.CompareOrdinal(x.Name, y.Name);
    }

    // Feeds the canonical writer straight into the hasher through a reusable scratch span, so the stream is never materialised.
    private sealed class Sha256Sink : IBufferWriter<byte>
    {
        private readonly Sha256 _sha = new();
        private byte[] _scratch = new byte[512];

        public Hash256 Finish() => _sha.Finish();

        public void Advance(int count) => _sha.Update(_scratch.AsSpan(0, count));

        public Memory<byte> GetMemory(int sizeHint = 0) => Ensure(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => Ensure(sizeHint);

        private byte[] Ensure(int sizeHint)
        {
            if (sizeHint > _scratch.Length)
            {
                _scratch = new byte[sizeHint];
            }

            return _scratch;
        }
    }
}
