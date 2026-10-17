using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The adapter core's event codec registry (ADR-0011 section 3 and 6; network-adapter-loopback story 004): resolves each
/// declared <see cref="EventKind"/> to its <see cref="IEventCodec"/> once at construction, keyed by the descriptor's
/// <c>WireCodecId</c>, into a kind-indexed table. <c>OpReply</c> always resolves to <see cref="ReplyEventCodec"/>; an
/// <c>Internal</c> kind or a <c>WireCodecId</c> of 0 has no codec (never leaves). A descriptor naming an unregistered codec id
/// also has none and is counted in <see cref="MissingCodecCount"/> (lenient, so a world with feature kinds whose codecs are
/// not written yet still builds; the adapter counts the dropped entries; a composition test should assert 0).
/// </summary>
/// <example><code>var events = new EventCodecRegistry(world.EventDescriptors, new IEventCodec[] { new FixedLengthEventCodec(910, 4) });</code></example>
public sealed class EventCodecRegistry
{
    private readonly IEventCodec?[] _byKind;

    /// <summary>Builds the table.</summary>
    /// <param name="descriptors">The world's declared event kinds.</param>
    /// <param name="codecs">The session's event codecs (no nulls, unique non-zero ids).</param>
    /// <exception cref="ArgumentNullException"><paramref name="codecs"/> or an element is null.</exception>
    /// <exception cref="ArgumentException">Two codecs share an id, a codec uses id 0 or is wider than 255 B, or a kind is above <see cref="EventBatch.MaxKind"/>.</exception>
    public EventCodecRegistry(ReadOnlySpan<EventDescriptor> descriptors, IEventCodec[] codecs)
    {
        ValidateCodecs(codecs);
        int maxKind = (int)EventKind.OpReply;
        for (int i = 0; i < descriptors.Length; i++)
        {
            int kind = (int)descriptors[i].Kind;
            if (kind > EventBatch.MaxKind)
            {
                throw new ArgumentException($"Event kind 0x{kind:X4} is above 0x{EventBatch.MaxKind:X4} and cannot be framed.", nameof(descriptors));
            }

            maxKind = Math.Max(maxKind, kind);
        }

        _byKind = new IEventCodec?[maxKind + 1];
        _byKind[(int)EventKind.OpReply] = ReplyEventCodec.Instance;
        for (int i = 0; i < descriptors.Length; i++)
        {
            EventDescriptor d = descriptors[i];
            if (d.Kind != EventKind.OpReply)
            {
                _byKind[(int)d.Kind] = Resolve(d, codecs);
            }
        }
    }

    /// <summary>
    /// Declared non-<c>Internal</c> kinds (other than <c>OpReply</c>) whose <c>WireCodecId</c> is 0: replicated by class but with
    /// no wire form yet, so their entries never leave (the adapter counts each dropped entry in
    /// <c>HostAdapter.NoCodecDropCount</c>). A composition test should assert 0 once every feature codec exists.
    /// </summary>
    public int NoWireKindCount { get; private set; }

    /// <summary>Declared kinds whose non-zero <c>WireCodecId</c> named no registered codec.</summary>
    public int MissingCodecCount { get; private set; }

    /// <summary>The codec for <paramref name="kind"/>, or null when the kind never leaves (undeclared, <c>Internal</c>, no codec).</summary>
    /// <param name="kind">The event kind.</param>
    /// <returns>The codec or null.</returns>
    public IEventCodec? CodecFor(EventKind kind)
    {
        int index = (int)kind;
        IEventCodec?[] table = _byKind;
        return index < table.Length ? table[index] : null;
    }

    private IEventCodec? Resolve(EventDescriptor descriptor, IEventCodec[] codecs)
    {
        if (descriptor.Class == ReplicationClass.Internal)
        {
            return null;
        }

        if (descriptor.WireCodecId == 0)
        {
            NoWireKindCount++;
            return null;
        }

        for (int i = 0; i < codecs.Length; i++)
        {
            if (codecs[i].CodecId == descriptor.WireCodecId)
            {
                return codecs[i];
            }
        }

        MissingCodecCount++;
        return null;
    }

    private static void ValidateCodecs(IEventCodec[] codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        for (int i = 0; i < codecs.Length; i++)
        {
            IEventCodec codec = codecs[i] ?? throw new ArgumentNullException(nameof(codecs), "A codec is null.");
            if (codec.CodecId == 0 || codec.MaxWireSize < 1 || codec.MaxWireSize > byte.MaxValue)
            {
                throw new ArgumentException($"Codec {codec.CodecId} must have a non-zero id and a wire size of 1 to 255.", nameof(codecs));
            }

            for (int j = 0; j < i; j++)
            {
                if (codecs[j].CodecId == codec.CodecId)
                {
                    throw new ArgumentException($"Two event codecs share id {codec.CodecId}.", nameof(codecs));
                }
            }
        }
    }
}
