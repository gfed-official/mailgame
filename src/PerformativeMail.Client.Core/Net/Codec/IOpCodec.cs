using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The wire codec of one op payload shape (ADR-0011 section 3). Implementations are pure functions over spans: no state, no
/// allocation, no exceptions on malformed input. A codec is registered in an <see cref="OpCodecRegistry"/> under
/// <see cref="CodecId"/>, which must equal the <c>WireCodecId</c> of every <see cref="OpDescriptor"/> that uses it. The Sim never
/// calls a codec; the spans handed to the transport sink are valid only during the callback, so a codec copies what it keeps
/// into the destination payload and never retains the span.
/// </summary>
/// <example>
/// <code>
/// IOpCodec codec = new PlayerLeaveCodec();
/// bool ok = codec.TryDecode(wire, ref op.Payload, out byte len);
/// </code>
/// </example>
public interface IOpCodec
{
    /// <summary>The codec id the Sim's <see cref="OpDescriptor.WireCodecId"/> names (non-zero; 0 means "no wire codec").</summary>
    ushort CodecId { get; }

    /// <summary>The exact encoded payload size in bytes; must equal the descriptor's <see cref="OpDescriptor.WireSize"/>.</summary>
    byte WireSize { get; }

    /// <summary>
    /// Decodes <paramref name="payload"/> (exactly the bytes after the envelope header, already checked to be
    /// <see cref="WireSize"/> long by the registry; a codec still validates its own length) into <paramref name="dst"/>. The caller
    /// has zeroed <paramref name="dst"/>; the codec writes only the meaningful payload bytes. Returns false for a value outside the
    /// wire range (the caller then discards <paramref name="dst"/>). Never throws, never allocates.
    /// </summary>
    /// <param name="payload">The wire payload bytes, valid only for this call.</param>
    /// <param name="dst">The op's zeroed payload area to fill.</param>
    /// <param name="payloadLength">The Sim payload length written (must equal the descriptor's <c>PayloadLength</c>); 0 when false is returned.</param>
    /// <returns>True when the payload is well-formed and in range.</returns>
    bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength);

    /// <summary>
    /// Encodes the payload of <paramref name="src"/> into <paramref name="dst"/> (no envelope header). Never throws, never allocates.
    /// </summary>
    /// <param name="src">The op whose payload to encode; its <see cref="Op.PayloadLength"/> must be the codec's Sim payload size.</param>
    /// <param name="dst">The destination; must hold at least <see cref="WireSize"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="WireSize"/>), or -1 when <paramref name="dst"/> is too small or the payload length is wrong.</returns>
    int Encode(in Op src, Span<byte> dst);

    /// <summary>Number of deterministic sample payloads the codec offers to the shared round-trip test (at least 1, including edge values).</summary>
    int SampleCount { get; }

    /// <summary>
    /// Returns sample number <paramref name="index"/>: an <see cref="Op"/> whose payload and <see cref="Op.PayloadLength"/> are set and
    /// valid for this codec (<see cref="Op.Kind"/> is left <c>None</c>; the test sets it from the descriptor). Registering a codec
    /// is all a feature epic does to add round-trip rows (OpCodecRoundTripTests).
    /// </summary>
    /// <param name="index">0 to <see cref="SampleCount"/> - 1.</param>
    /// <returns>The sample op.</returns>
    Op GetSample(int index);
}
