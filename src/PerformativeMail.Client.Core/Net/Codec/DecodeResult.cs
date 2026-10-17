namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// Outcome of decoding one reliable request message (ADR-0011 section 3). Anything the codecs cannot turn into a
/// valid <see cref="PerformativeMail.Sim.Core.Op"/> is <see cref="BadRequest"/>; decoding never throws on malformed input.
/// </summary>
/// <example>
/// <code>
/// if (RequestEnvelope.TryDecode(registry, bytes, out Op op) == DecodeResult.BadRequest) { /* answer bad_request (8), enqueue nothing */ }
/// </code>
/// </example>
public enum DecodeResult : byte
{
    /// <summary>The message decoded to a valid op; the <c>out</c> op is fully initialised.</summary>
    Ok = 0,

    /// <summary>
    /// The message is malformed: shorter than the envelope header, an unknown kind (or a kind with no wire codec), a payload
    /// that is too short or has trailing bytes, or a value the codec rejects as out of wire range. The adapter answers
    /// <c>bad_request</c> (<c>CoreReason.BadRequest</c>, 8) itself and enqueues nothing; the <c>out</c> op is all zero.
    /// </summary>
    BadRequest = 1,
}
