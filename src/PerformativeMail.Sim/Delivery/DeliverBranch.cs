using System.Runtime.CompilerServices;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>The deliver preview's branch (Manual Delivery MD3), checked in this order: first match wins.</summary>
public enum DeliverBranchKind : byte
{
    /// <summary>The destination is CLOSED or LOCKED; no panel scan.</summary>
    Reject = 0,

    /// <summary>At least one scanned entry evaluates to Accept.</summary>
    Match = 1,

    /// <summary>No match, and the selected hotbar slot holds mail evaluating to Misdeliver (or a <c>wont_fit</c> one, see <see cref="DeliverBranch.WontFit"/>).</summary>
    MisdeliverOne = 2,

    /// <summary>Otherwise: hands, a tool or a non-mail item selected, or no mail at all.</summary>
    Nothing = 3,
}

/// <summary>
/// The result of <see cref="DeliverPreview.Preview"/> (Manual Delivery MD3 and F1): the <see cref="Kind"/> plus its payload. For
/// <see cref="DeliverBranchKind.Match"/>: the matching entries in scan order, <see cref="K"/>, <see cref="N"/>, the kinds present and
/// the F1 figures. For <see cref="DeliverBranchKind.MisdeliverOne"/>: the selected entry, its address, the oldest unit's penalty,
/// <see cref="FloorRefusal"/> and <see cref="Cursed"/>. Money is a prediction in integer cents; the host decides. A value type with
/// inline storage: building one allocates nothing.
/// </summary>
/// <example>
/// <code>
/// DeliverBranch b = DeliverPreview.Preview(in dest, panel, selectedSlot, shift, balance, stats, wallet, floor, ref scratch);
/// if (b.Kind == DeliverBranchKind.Match) { long cents = b.PredictedPay.Value; }
/// </code>
/// </example>
public struct DeliverBranch
{
    private DeliverEntryIds _entries;

    /// <summary>The branch.</summary>
    public DeliverBranchKind Kind { get; internal set; }

    /// <summary>For <see cref="DeliverBranchKind.Reject"/>: <c>Closed</c> or <c>NotOnRoute</c> (the prompt's <c>mail.reject.*</c> key).</summary>
    public MailRejectReason RejectReason { get; internal set; }

    /// <summary>MATCH: the number of matching entries.</summary>
    public int K { get; internal set; }

    /// <summary>MATCH: the number of matching units.</summary>
    public int N { get; internal set; }

    /// <summary>MATCH: bit k set when a matching entry has kind key k (keys of 64 or more are not recorded).</summary>
    public ulong KindsPresent { get; internal set; }

    /// <summary>MATCH: the predicted pay, the sum of one F6 rounding per unit (F1).</summary>
    public Cents PredictedPay { get; internal set; }

    /// <summary>MATCH: units exactly one shift late.</summary>
    public int LateUnits { get; internal set; }

    /// <summary>MATCH: dead-letter units.</summary>
    public int DeadUnits { get; internal set; }

    /// <summary>MATCH: every matching unit is a dead letter (still a ✓, labelled "clears").</summary>
    public bool DeadOnly { get; internal set; }

    /// <summary>MISDELIVER_ONE: the selected entry.</summary>
    public EntryId SelectedEntry { get; internal set; }

    /// <summary>MISDELIVER_ONE: the selected entry's address.</summary>
    public AddressId SelectedAddress { get; internal set; }

    /// <summary>MISDELIVER_ONE: the F7 penalty of the selected entry's oldest unit (run 0).</summary>
    public Cents PredictedPenalty { get; internal set; }

    /// <summary>MISDELIVER_ONE: the wallet would land below the floor (<c>wallet − penalty &lt; walletFloor</c>).</summary>
    public bool FloorRefusal { get; internal set; }

    /// <summary>MISDELIVER_ONE: the selected entry carries the Cursed flag.</summary>
    public bool Cursed { get; internal set; }

    /// <summary>MISDELIVER_ONE: the destination type does not accept the selected mail's kind (<c>wont_fit</c>, dormant for houses); the hold is blocked.</summary>
    public bool WontFit { get; internal set; }

    /// <summary>MATCH: the matching entry at <paramref name="i"/> in scan order (the first <see cref="DeliverScan.MaxEntries"/> are kept).</summary>
    /// <param name="i">The index, 0 to <c>Math.Min(K, DeliverScan.MaxEntries)</c> - 1.</param>
    /// <returns>The entry id; none outside the kept list.</returns>
    public readonly EntryId Entry(int i) => (uint)i < (uint)Math.Min(K, DeliverScan.MaxEntries) ? _entries[i] : EntryId.None;

    /// <summary>Records one more matching entry (K + 1), keeping its id while there is room.</summary>
    /// <param name="id">The entry id.</param>
    internal void AddMatch(EntryId id)
    {
        if (K < DeliverScan.MaxEntries)
        {
            _entries[K] = id;
        }

        K++;
    }
}

/// <summary>Fixed-capacity inline storage for the MATCH entry list (no heap).</summary>
[InlineArray(DeliverScan.MaxEntries)]
internal struct DeliverEntryIds
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
#pragma warning disable IDE0051 // Remove unused private members
    private EntryId _element;
#pragma warning restore IDE0051
#pragma warning restore CS0169
}
