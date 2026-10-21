namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// Grid Inventory's own reply reason codes (ADR-0009 section 2; GDD <c>grid-inventory.md</c>; TR-inventory-006; story grid-inventory/004).
/// Values are u16, keyed by <c>(OpKind, Reason)</c> in each inventory <see cref="Core.OpDescriptor"/>, append-only and never renumbered
/// or reused (a snapshot test pins every name and value). <c>bad_request</c> is not here: it is Sim Core's global code
/// <see cref="Core.CoreReason.BadRequest"/> (8), exposed as <see cref="InvReasons.BadRequest"/>. 64 to 85 is exact: 83/84 stay
/// declared although <c>DeliverMatching</c> moved to Delivery's block (ADR-0009 erratum 2026-10-16).
/// </summary>
/// <example><code>return Reply.Reject(in op, (ushort)InvReason.StaleEntry);   // 67</code></example>
public enum InvReason : ushort
{
    /// <summary><c>not_open</c>: the actor has not opened (is not subscribed to) the container.</summary>
    NotOpen = 64,

    /// <summary><c>out_of_range</c>: the claimed position is too far from the target (P13).</summary>
    OutOfRange = 65,

    /// <summary><c>position_stale</c>: the claimed position disagrees with the Sim's (P13).</summary>
    PositionStale = 66,

    /// <summary><c>stale_entry</c>: the entry id is not live (removed, consumed, or a temp id never mapped).</summary>
    StaleEntry = 67,

    /// <summary><c>stale_layout</c>: <c>baseVersion</c> is older than the destination's last replace.</summary>
    StaleLayout = 68,

    /// <summary><c>cursor_busy</c>: the cursor already holds an entry.</summary>
    CursorBusy = 69,

    /// <summary><c>cursor_empty</c>: the cursor holds nothing.</summary>
    CursorEmpty = 70,

    /// <summary><c>blocked</c>: the target cells are occupied.</summary>
    Blocked = 71,

    /// <summary><c>out_of_bounds</c>: the placement leaves the container's grid.</summary>
    OutOfBounds = 72,

    /// <summary><c>not_allowed</c>: the container's category filter refuses the item.</summary>
    NotAllowed = 73,

    /// <summary><c>not_carryable</c>: the item may not be hand-carried.</summary>
    NotCarryable = 74,

    /// <summary><c>swimming</c>: the action is refused while swimming.</summary>
    Swimming = 75,

    /// <summary><c>stack_full</c>: the target stack has no room.</summary>
    StackFull = 76,

    /// <summary><c>no_room</c>: no container in the chain has room.</summary>
    NoRoom = 77,

    /// <summary><c>bad_count</c>: the requested count is not valid for the entry.</summary>
    BadCount = 78,

    /// <summary><c>unsplittable</c>: the entry cannot be split.</summary>
    Unsplittable = 79,

    /// <summary><c>reserved_slot</c>: the target slot is reserved.</summary>
    ReservedSlot = 80,

    /// <summary><c>sort_failed</c>: the sort could not place every entry.</summary>
    SortFailed = 81,

    /// <summary><c>container_gone</c>: the container id is tombstoned.</summary>
    ContainerGone = 82,

    /// <summary><c>no_target</c>: there is no interactable target (P13).</summary>
    NoTarget = 83,

    /// <summary><c>wallet_floor</c>: the wallet would go below its floor.</summary>
    WalletFloor = 84,

    /// <summary><c>capacity_exhausted</c>: a preallocated store (e.g. the mailIds arena) has no block left.</summary>
    CapacityExhausted = 85,
}

/// <summary>The snake_case catalogue names of <see cref="InvReason"/> and the <see cref="Core.ReasonDef"/>s built from them.</summary>
/// <example><code>ReasonDef d = InvReasons.Def(InvReason.NoRoom);   // (77, "no_room")</code></example>
public static class InvReasons
{
    /// <summary><c>bad_request</c>: Sim Core's global code 8 (ADR-0007 section 5), used by inventory for structurally invalid requests.</summary>
    public const ushort BadRequest = Core.CoreReason.BadRequest;

    /// <summary>The stable snake_case name of <paramref name="reason"/> (ADR-0009 section 2), or <c>"unknown"</c> for an undefined value.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The catalogue name (a literal; no allocation).</returns>
    public static string Name(InvReason reason) => reason switch
    {
        InvReason.NotOpen => "not_open",
        InvReason.OutOfRange => "out_of_range",
        InvReason.PositionStale => "position_stale",
        InvReason.StaleEntry => "stale_entry",
        InvReason.StaleLayout => "stale_layout",
        InvReason.CursorBusy => "cursor_busy",
        InvReason.CursorEmpty => "cursor_empty",
        InvReason.Blocked => "blocked",
        InvReason.OutOfBounds => "out_of_bounds",
        InvReason.NotAllowed => "not_allowed",
        InvReason.NotCarryable => "not_carryable",
        InvReason.Swimming => "swimming",
        InvReason.StackFull => "stack_full",
        InvReason.NoRoom => "no_room",
        InvReason.BadCount => "bad_count",
        InvReason.Unsplittable => "unsplittable",
        InvReason.ReservedSlot => "reserved_slot",
        InvReason.SortFailed => "sort_failed",
        InvReason.ContainerGone => "container_gone",
        InvReason.NoTarget => "no_target",
        InvReason.WalletFloor => "wallet_floor",
        InvReason.CapacityExhausted => "capacity_exhausted",
        _ => "unknown",
    };

    /// <summary>The descriptor declaration of <paramref name="reason"/>: <c>(code, snake_case name)</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The reason definition.</returns>
    public static Core.ReasonDef Def(InvReason reason) => new((ushort)reason, Name(reason));
}
