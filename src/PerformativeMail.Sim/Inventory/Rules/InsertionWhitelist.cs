using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory.Ops;

namespace PerformativeMail.Sim.Inventory.Rules;

/// <summary>
/// The one shared insertion predicate (GDD R5 and R1 <c>acceptsInsert</c>; TR-inventory-006; story grid-inventory/006). Every
/// insertion path (move, quick_move, AutoPlace, inserter, belt, spawn, return) calls <see cref="Check(ContainerDef, in PlacementSubject)"/>,
/// so the whitelist cannot drift between paths. It answers the <c>not_carryable → not_allowed</c> steps of the placement chain:
/// a <see cref="ContainerDef.HandCarried"/> container rejects <c>carryByHand: false</c> or <see cref="WeightClass.Bulk"/> with
/// <c>not_carryable</c>; a container with <see cref="ContainerDef.AcceptsInsert"/> false, or whose
/// <see cref="ContainerDef.AllowedCategories"/> omit the category, rejects with <c>not_allowed</c>. Checked on insertion only,
/// never retroactively. Pure and allocation-free.
/// </summary>
/// <example>
/// <code>
/// InvReason r = InsertionWhitelist.Check(intakeDef, in subject);   // InvReason.NotAllowed for a Plank
/// if (r != 0) { return Reply.Reject(in op, (ushort)r); }
/// </code>
/// </example>
public static class InsertionWhitelist
{
    /// <summary>The whitelist verdict for <paramref name="subject"/> entering <paramref name="container"/>: 0 when admitted, else the reason.</summary>
    /// <param name="container">The destination container's def.</param>
    /// <param name="subject">The entry being inserted.</param>
    /// <returns>0, <see cref="InvReason.NotCarryable"/> or <see cref="InvReason.NotAllowed"/>.</returns>
    public static InvReason Check(ContainerDef container, in PlacementSubject subject) =>
        Check(container, subject.Category, subject.CarryByHand, subject.WeightClass);

    /// <summary>The whitelist verdict from the def traits: 0 when admitted, else the reason (<c>not_carryable</c> before <c>not_allowed</c>).</summary>
    /// <param name="container">The destination container's def.</param>
    /// <param name="category">The entry's category (<see cref="ItemCategory.Mail"/> for mail).</param>
    /// <param name="carryByHand">The def's <c>carryByHand</c>.</param>
    /// <param name="weightClass">The def's weight class.</param>
    /// <returns>0, <see cref="InvReason.NotCarryable"/> or <see cref="InvReason.NotAllowed"/>.</returns>
    public static InvReason Check(ContainerDef container, ItemCategory category, bool carryByHand, WeightClass weightClass)
    {
        if (container.HandCarried && (!carryByHand || weightClass == WeightClass.Bulk))
        {
            return InvReason.NotCarryable;
        }

        if (!container.AcceptsInsert || !Admits(container, category))
        {
            return InvReason.NotAllowed;
        }

        return 0;
    }

    /// <summary>
    /// True when the container's <c>allowedCategories</c> admit <paramref name="category"/> (omitted = any; declared but default or
    /// empty = none). The category half of R3's mergeable test.
    /// </summary>
    /// <param name="container">The container's def.</param>
    /// <param name="category">The entry's category.</param>
    /// <returns>True when admitted.</returns>
    public static bool Admits(ContainerDef container, ItemCategory category)
    {
        if (!container.HasAllowedCategories)
        {
            return true;
        }

        if (container.AllowedCategories.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (ItemCategory allowed in container.AllowedCategories)
        {
            if (allowed == category)
            {
                return true;
            }
        }

        return false;
    }
}
