using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>What pushed a modifier (CD13 <c>sourceKind</c>). Closed; append-only; values are never reused.</summary>
/// <example>
/// <code>
/// SourceKind kind = SourceKind.Perk;
/// </code>
/// </example>
public enum SourceKind : byte
{
    /// <summary>A perk (Perks and Perk Draft).</summary>
    Perk = 1,

    /// <summary>A stamp (Stamps and Run Events).</summary>
    Stamp = 2,

    /// <summary>A run event (Stamps and Run Events).</summary>
    RunEvent = 3,
}

/// <summary>The op of one modifier (CD13). Closed; append-only; values are never reused.</summary>
/// <example>
/// <code>
/// ModifierOp op = ModifierOp.Mul;   // value in permille
/// </code>
/// </example>
public enum ModifierOp : byte
{
    /// <summary>Adds <see cref="Modifier.Value"/> in the stat's unit.</summary>
    Add = 1,

    /// <summary>Multiplies by <see cref="Modifier.Value"/> permille.</summary>
    Mul = 2,

    /// <summary>Replaces the base-derived value (the stat's <see cref="SetResolve"/> picks between several).</summary>
    Set = 3,
}

/// <summary>
/// The lifetime tag of a modifier (CD16). The sheet never acts on it: the pushing system removes <see cref="Shift"/>
/// sources at Payday (the sheet has no timers). Closed; append-only; values are never reused.
/// </summary>
/// <example>
/// <code>
/// ModifierLifetime life = ModifierLifetime.Shift;
/// </code>
/// </example>
public enum ModifierLifetime : byte
{
    /// <summary>Until the run ends (perks, stamps).</summary>
    Run = 1,

    /// <summary>Until the next Payday settlement after it was pushed.</summary>
    Shift = 2,
}

/// <summary>
/// One stat modifier (CD13): <c>{sourceKind, sourceId, stackIndex, ownerSlot|Team, stat, qualifier|0xFFFF, op, value, lifetime}</c>.
/// Immutable with structural equality. Push order never changes a result (F1). A team modifier has
/// <see cref="OwnerSlot"/> = <see cref="PlayerSlot.None"/>.
/// </summary>
/// <param name="SourceKind">What pushed it.</param>
/// <param name="SourceId">The source def's index (a <c>PerkDef</c> from M1; any index at M-1a).</param>
/// <param name="StackIndex">Which stack of the source this is (0-based).</param>
/// <param name="OwnerSlot">The owning player's slot, or <see cref="PlayerSlot.None"/> for the team.</param>
/// <param name="Stat">The stat it changes.</param>
/// <param name="Qualifier">The qualifier def's ordinal, or <see cref="StatSheet.NoQualifier"/> (0xFFFF) for "every qualifier" / an unqualified stat.</param>
/// <param name="Op">Add, Mul or Set.</param>
/// <param name="Value">In the stat's unit; Mul in permille.</param>
/// <param name="Lifetime">The lifetime tag (not acted on by the sheet).</param>
/// <example>
/// <code>
/// // One stack of Long Legs: personal Add 120 on MoveSpeedAddPm for slot 2.
/// var m = new Modifier(SourceKind.Perk, DefIndex.FromValue(0), 0, PlayerSlot.FromValue(2),
///     Stat.MoveSpeedAddPm, StatSheet.NoQualifier, ModifierOp.Add, 120, ModifierLifetime.Run);
/// </code>
/// </example>
public readonly record struct Modifier(
    SourceKind SourceKind,
    DefIndex SourceId,
    byte StackIndex,
    PlayerSlot OwnerSlot,
    Stat Stat,
    ushort Qualifier,
    ModifierOp Op,
    int Value,
    ModifierLifetime Lifetime);
