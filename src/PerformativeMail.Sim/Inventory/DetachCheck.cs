namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The decision of <see cref="IInventoryApi.CheckDetachForThrow"/>: the refusal <see cref="IInventoryApi.DetachForThrow"/> would return,
/// computed without mutating (throwing-catching story 009; ADR-0009 section 2 validate/commit split). Throwing maps it to checks 1 and 2
/// of its <c>ThrowRelease</c> chain (ADR-0015 section 1).
/// </summary>
/// <example><code>if (inventory.CheckDetachForThrow(slot, payload.EntryId, payload.Units, out byte index) == DetachCheck.Stale) { /* stale_entry */ }</code></example>
public enum DetachCheck : byte
{
    /// <summary>The entry is a whole placed entry of the slot's hotbar with exactly the request's units: the detach would succeed.</summary>
    Ok = 0,

    /// <summary><c>bad_request</c>: no hotbar, a malformed id, or a live entry not in the slot's hotbar.</summary>
    BadRequest = 1,

    /// <summary><c>stale_entry</c>: an id that is no longer (or never was) live, or units other than the entry's count.</summary>
    Stale = 2,
}
