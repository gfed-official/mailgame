using System.Diagnostics.CodeAnalysis;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>One wire-reachable op kind: its Sim descriptor paired with the codec its <c>WireCodecId</c> names.</summary>
/// <param name="Descriptor">The Sim's descriptor of the kind.</param>
/// <param name="Codec">The codec registered under <c>Descriptor.WireCodecId</c>.</param>
/// <example><code>OpCodecBinding b = registry.Bindings[0];</code></example>
public readonly record struct OpCodecBinding(OpDescriptor Descriptor, IOpCodec Codec);

/// <summary>
/// The adapter core's codec table (ADR-0011 section 3): built once at session start from the frozen world's op descriptors and the
/// composition root's codecs, then immutable. Codecs are keyed by <c>WireCodecId</c>; at build each descriptor with a non-zero
/// codec id is bound to its codec and the bindings are stored sorted by <see cref="OpKind"/>, so a lookup is a binary search over an
/// array (no dictionary, no allocation). Descriptors with codec id 0 are not wire-reachable: their kinds decode as
/// <see cref="DecodeResult.BadRequest"/>. Single-threaded after construction (read-only, so safe to share).
/// </summary>
/// <example>
/// <code>
/// var registry = new OpCodecRegistry(world.OpDescriptors, SimCoreCodecs.Create());
/// bool known = registry.TryGet(OpKind.PlayerJoin, out OpDescriptor? d, out IOpCodec? c);
/// </code>
/// </example>
public sealed class OpCodecRegistry
{
    private readonly OpCodecBinding[] _bindings;

    /// <summary>Builds the registry.</summary>
    /// <param name="descriptors">The world's op descriptors, ascending by kind (<c>SimWorld.OpDescriptors</c>).</param>
    /// <param name="codecs">The codecs the composition root registers; may contain codecs no descriptor uses yet.</param>
    /// <exception cref="ArgumentNullException">A codec is null.</exception>
    /// <exception cref="ArgumentException">Two codecs share an id, a codec id is 0, a descriptor names a codec id nobody registered, a descriptor's wire size exceeds <see cref="OpPayload.Size"/> (so <see cref="RequestEnvelope.MaxSize"/> would not hold), a codec's <see cref="IOpCodec.WireSize"/> differs from the descriptor's, a codec sample's <see cref="Op.PayloadLength"/> differs from the descriptor's <see cref="OpDescriptor.PayloadLength"/>, or the descriptors are not strictly ascending by kind.</exception>
    /// <remarks>Every check here guards composition-root (programmer) errors at session start; none is reachable from wire input, so decode stays exception-free.</remarks>
    public OpCodecRegistry(ReadOnlySpan<OpDescriptor> descriptors, ReadOnlySpan<IOpCodec> codecs)
    {
        for (int i = 0; i < codecs.Length; i++)
        {
            IOpCodec codec = codecs[i] ?? throw new ArgumentNullException(nameof(codecs), "A codec is null.");
            if (codec.CodecId == 0)
            {
                throw new ArgumentException("Codec id 0 means 'no wire codec' and cannot be registered.", nameof(codecs));
            }

            for (int j = 0; j < i; j++)
            {
                if (codecs[j].CodecId == codec.CodecId)
                {
                    throw new ArgumentException($"Codec id {codec.CodecId} is registered twice.", nameof(codecs));
                }
            }
        }

        int count = 0;
        for (int i = 0; i < descriptors.Length; i++)
        {
            if (i > 0 && (ushort)descriptors[i].Kind <= (ushort)descriptors[i - 1].Kind)
            {
                throw new ArgumentException("Op descriptors must be strictly ascending by kind.", nameof(descriptors));
            }

            if (descriptors[i].WireCodecId != 0)
            {
                count++;
            }
        }

        _bindings = new OpCodecBinding[count];
        int next = 0;
        for (int i = 0; i < descriptors.Length; i++)
        {
            OpDescriptor descriptor = descriptors[i];
            if (descriptor.WireCodecId == 0)
            {
                continue;
            }

            IOpCodec? found = null;
            for (int c = 0; c < codecs.Length; c++)
            {
                if (codecs[c].CodecId == descriptor.WireCodecId)
                {
                    found = codecs[c];
                    break;
                }
            }

            if (found is null)
            {
                throw new ArgumentException($"Op kind 0x{(ushort)descriptor.Kind:X4} names codec id {descriptor.WireCodecId} but no codec with that id is registered.", nameof(codecs));
            }

            if (descriptor.WireSize > OpPayload.Size)
            {
                throw new ArgumentException(
                    $"Op kind 0x{(ushort)descriptor.Kind:X4}: descriptor wire size {descriptor.WireSize} exceeds the {OpPayload.Size} B payload area.",
                    nameof(descriptors));
            }

            if (found.WireSize != descriptor.WireSize)
            {
                throw new ArgumentException(
                    $"Op kind 0x{(ushort)descriptor.Kind:X4}: descriptor wire size {descriptor.WireSize} differs from codec {found.CodecId} wire size {found.WireSize}.",
                    nameof(codecs));
            }

            for (int s = 0; s < found.SampleCount; s++)
            {
                byte sampleLength = found.GetSample(s).PayloadLength;
                if (sampleLength != descriptor.PayloadLength)
                {
                    throw new ArgumentException(
                        $"Op kind 0x{(ushort)descriptor.Kind:X4}: descriptor payload length {descriptor.PayloadLength} differs from codec {found.CodecId} sample {s} payload length {sampleLength}.",
                        nameof(codecs));
                }
            }

            _bindings[next++] = new OpCodecBinding(descriptor, found);
        }
    }

    /// <summary>The wire-reachable kinds, ascending by <see cref="OpKind"/>. Read-only view of the frozen table.</summary>
    public ReadOnlySpan<OpCodecBinding> Bindings => _bindings;

    /// <summary>Looks up the descriptor and codec of <paramref name="kind"/> by binary search. Allocation-free.</summary>
    /// <param name="kind">The op kind from the envelope.</param>
    /// <param name="descriptor">The kind's descriptor when this returns true; null when it returns false (not wire-reachable).</param>
    /// <param name="codec">The kind's codec when this returns true; null when it returns false (not wire-reachable).</param>
    /// <returns>True when the kind has a descriptor with a registered codec.</returns>
    public bool TryGet(OpKind kind, [MaybeNullWhen(false)] out OpDescriptor descriptor, [MaybeNullWhen(false)] out IOpCodec codec)
    {
        ushort key = (ushort)kind;
        int lo = 0;
        int hi = _bindings.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            ushort probe = (ushort)_bindings[mid].Descriptor.Kind;
            if (probe == key)
            {
                descriptor = _bindings[mid].Descriptor;
                codec = _bindings[mid].Codec;
                return true;
            }

            if (probe < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        descriptor = null;
        codec = null;
        return false;
    }
}
