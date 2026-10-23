namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The fixed sub-order of <see cref="ClientStepId.Previews"/> (ADR-0016 section 2 and Key Interfaces). The numeric values are the
/// run order. Bind with <see cref="ClientLayerBuilder.BindPreview"/>; steps bound with plain
/// <c>Bind(ClientStepId.Previews, …)</c> run after <see cref="HoldAdvance"/> (D4, Approved by Evan 2026-10-22 8:24 PM PT
/// (Mail Lead chat), TD-164).
/// </summary>
/// <example><code>builder.BindPreview(PreviewSubStep.Focus, focusStep);</code></example>
public enum PreviewSubStep : byte
{
    /// <summary>4a: interaction focus and the 4-tick dwell (ADR-0014 section 2).</summary>
    Focus = 0,

    /// <summary>4b: Manual Delivery's preview and tracker.</summary>
    DeliverPreview = 1,

    /// <summary>4c: Throwing's preview.</summary>
    ThrowPreview = 2,

    /// <summary>4d: the tick's latched edges, resolved in arrival order on this tick's 4a–4c state.</summary>
    EdgeResolve = 3,

    /// <summary>4e: hold accrual and cancels, then the interaction snapshot.</summary>
    HoldAdvance = 4,
}
