namespace PerformativeMail.Sim.Players;

/// <summary>
/// The bits of <see cref="Core.TransformSample.Flags"/> (player-character story 019; shared with story 020, which writes them). This
/// story reads <see cref="Swimming"/> into the host position; ADR-0020 (M0) owns the final layout.
/// </summary>
/// <example><code>bool swimming = ((SampleFlags)sample.Flags &amp; SampleFlags.Swimming) != 0;</code></example>
[Flags]
public enum SampleFlags : byte
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The owner's carrier was swimming when it sent the sample.</summary>
    Swimming = 1,

    /// <summary>The owner's carrier was sprinting when it sent the sample.</summary>
    Sprinting = 2,
}
