namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// An <see cref="IOpCodec"/> whose op has exactly two wire lengths (ADR-0011 section 3 and erratum 2026-10-16: an optional trailing field
/// whose presence is implied by length, as <c>deliver_matching</c>'s <c>selectedEntryId</c>). <see cref="IOpCodec.WireSize"/> is the long
/// form and equals the descriptor's <c>WireSize</c>; <see cref="ShortWireSize"/> is the form without the trailing field. The request
/// envelope accepts a payload of exactly one of the two lengths and refuses every other length as <c>bad_request</c>; the codec still
/// validates the length it is given.
/// </summary>
/// <example><code>bool twoForms = codec is IShortFormOpCodec s &amp;&amp; s.ShortWireSize &lt; codec.WireSize;</code></example>
public interface IShortFormOpCodec : IOpCodec
{
    /// <summary>The encoded payload size without the optional trailing field, in bytes (non-zero and below <see cref="IOpCodec.WireSize"/>).</summary>
    byte ShortWireSize { get; }
}
