using System.Runtime.InteropServices;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The interaction snapshot the local client publishes once per tick at the end of <c>PreviewSubStep.HoldAdvance</c> (4e)
/// (ADR-0014 section 7 l.115 with the 2026-11-04 erratum; ADR-0016 section 2; GDD P11, UI Requirements; TR-movement-011). The
/// router and the HUD read it through <see cref="IInteractionQuery.Current"/>. Values only, 84 B, explicit layout, no
/// <see langword="bool"/>; byte offsets per TD-209 item 1 (approved by Evan 2026-11-04 9:56 AM PT: "Approve TD-209 item 1 as
/// listed (usable flag at byte 39)"). The padding inside each <see cref="AnchorRef"/> and <see cref="StringId"/> is zero: the
/// publisher builds every snapshot from <c>default</c>.
/// </summary>
/// <example>
/// <code>
/// ref readonly InteractionSnapshot s = ref query.Current;
/// if (!s.Focused.IsNone &amp;&amp; s.Available == 0) { StringId why = s.Reason; }
/// int ring = s.ProgressPm;   // 5 of 12 held ticks: 416
/// </code>
/// </example>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct InteractionSnapshot
{
    /// <summary>The snapshot's size in bytes.</summary>
    public const int Size = 84;

    /// <summary>The first byte <see cref="Seq"/> change detection compares (everything but <see cref="Tick"/> and <see cref="Seq"/>).</summary>
    internal const int ContentOffset = 6;

    /// <summary>The client tick it was published on.</summary>
    [field: FieldOffset(0)]
    public uint Tick { readonly get; internal set; }

    /// <summary>
    /// The change counter: starts at 0 and goes up by 1 (wrapping as a <see langword="ushort"/>, compare with
    /// <see cref="SerialU16.IsNewer"/>) only on a publish whose bytes 6 to 83 differ from the previous one (TD-209 item 7). A press
    /// edge whose <c>PresentedSeq</c> equals it acted on this prompt.
    /// </summary>
    [field: FieldOffset(4)]
    public ushort Seq { readonly get; internal set; }

    /// <summary>The interaction state after this tick's 4d and 4e.</summary>
    [field: FieldOffset(6)]
    public InteractState State { readonly get; internal set; }

    /// <summary>Why the last interaction ended or was turned down (TD-209 item 8); <see cref="CancelReason.None"/> after a successful start or Tap send.</summary>
    [field: FieldOffset(7)]
    public CancelReason LastCancel { readonly get; internal set; }

    /// <summary>The focused anchor; <c>default</c> when nothing is focused and on every Dead tick (TD-209 item 6).</summary>
    [field: FieldOffset(8)]
    public AnchorRef Focused { readonly get; internal set; }

    /// <summary>The latched anchor while Holding, Channel or Complete (none for a catch); <c>default</c> otherwise (TD-209 item 5).</summary>
    [field: FieldOffset(16)]
    public AnchorRef Latched { readonly get; internal set; }

    /// <summary>The nearest visible out-of-reach anchor within 6000 mm, or <c>default</c> (A5, ADR-0014 erratum 2026-11-04).</summary>
    [field: FieldOffset(24)]
    public AnchorRef NearestBeyond { readonly get; internal set; }

    /// <summary>The chest distance of <see cref="NearestBeyond"/>, mm (at most 6000); 0 when it is empty.</summary>
    [field: FieldOffset(32)]
    public int NearestBeyondRangeMm { readonly get; internal set; }

    /// <summary>The focused anchor's verb; 0 when nothing is focused.</summary>
    [field: FieldOffset(36)]
    public Verb Verb { readonly get; internal set; }

    /// <summary>The focused anchor's mode; 0 when nothing is focused.</summary>
    [field: FieldOffset(37)]
    public InteractMode Mode { readonly get; internal set; }

    /// <summary>1 when this tick's press predicted a catch (<see cref="CatchPrediction.Predicted"/>), else 0 (TD-209 item 3).</summary>
    [field: FieldOffset(38)]
    public byte CatchPredicted { readonly get; internal set; }

    /// <summary>The focused anchor's "usable" flag: 1 usable (<c>Availability = ok</c>), 0 not (or nothing focused).</summary>
    [field: FieldOffset(39)]
    public byte Available { readonly get; internal set; }

    /// <summary>Why the focused anchor is unusable, the one reason key; <see cref="StringId.None"/> when there is none.</summary>
    [field: FieldOffset(40)]
    public StringId Reason { readonly get; internal set; }

    /// <summary>This tick's press prediction's <see cref="CatchPrediction.FlightId"/>; 0 without one.</summary>
    [field: FieldOffset(44)]
    public ushort CatchFlightId { readonly get; internal set; }

    /// <summary><c>tick − PressTick</c> while Holding; <see cref="HoldTicks"/> while Complete; saturates at 32 767; 0 otherwise.</summary>
    [field: FieldOffset(46)]
    public short HeldTicks { readonly get; internal set; }

    /// <summary>The latched hold length while Holding or Complete (saturates at 32 767); 0 otherwise.</summary>
    [field: FieldOffset(48)]
    public short HoldTicks { readonly get; internal set; }

    /// <summary><c>Hold.ProgressPm(HeldTicks, HoldTicks)</c> while Holding; 1000 while Complete; 0 otherwise.</summary>
    [field: FieldOffset(50)]
    public ushort ProgressPm { readonly get; internal set; }

    /// <summary>The focused owner's <c>WritePrompt</c> bytes, copied by value; zero when nothing was written (TD-209 item 2).</summary>
    [field: FieldOffset(52)]
    public PromptArgs PromptArgs { readonly get; internal set; }
}
