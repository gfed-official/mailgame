using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's <see cref="OpKind"/> values (ADR-0007 section 4 Throwing block <c>0x0140-0x017F</c>; ADR-0015 section 1; story
/// throwing-catching/006). Kept in the owning module (as <c>InvOpKinds</c> keeps Inventory's) so Sim Core's enum never names a
/// system's ops. Append-only, never reused. <c>CatchArm</c> (<c>0x0141</c>) is story 022's.
/// </summary>
/// <example><code>var op = new Op { Kind = ThrowOpKinds.ThrowRelease, ReqId = seq };</code></example>
public static class ThrowOpKinds
{
    /// <summary>Release a throw: the hotbar entry, its units, the claimed feet and the aim direction (ADR-0015 section 1).</summary>
    public const OpKind ThrowRelease = (OpKind)0x0140;
}
