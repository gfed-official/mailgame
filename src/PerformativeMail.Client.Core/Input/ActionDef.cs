using System.Collections.Immutable;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// One player verb (GDD R1, ADR-0012 section 2): a stable numeric id and string id, exactly one <see cref="Grammar"/>, the
/// states it is live in as a union of <see cref="LivePredicate"/>s, its <see cref="Delivery"/> and (for per-tick frame
/// actions) its <see cref="FrameAction"/> bit, the four default bindings (two keyboard-and-mouse, two gamepad), the reserved
/// and required flags, the owning GDD and the alias ids kept after a rename. Every field is validated at load
/// (<see cref="ActionMapValidator"/>); a field left at its default is a validation failure. Immutable; thread-safe.
/// <para>
/// <b>Throw</b> is one def: <see cref="Grammar.AimRelease"/>, <see cref="Delivery.Frame"/> with the
/// <see cref="FrameAction.Aim"/> bit (the held bit Player Character reads as "aiming"), and
/// <see cref="CommitOnRelease"/> = true, which tells the router that release also sends one <see cref="Delivery.Immediate"/>
/// commit request (input-controls story 014). The def never gets a second id for the commit.
/// </para>
/// <para>
/// <b>Interact</b> is <see cref="Grammar.Hold"/> with <see cref="TargetDriven"/> = true: the presented target's interact mode
/// decides whether a press fires at once (a Tap target) or must be held (story 012).
/// </para>
/// </summary>
/// <example>
/// <code>
/// ActionDef jump = map.Get(ActionId.Jump)!;
/// bool live = jump.IsLive(frame.RouterStateId);
/// ulong bit = jump.FrameBit is { } b ? FrameActions.Bit(b) : 0UL;
/// </code>
/// </example>
public sealed record ActionDef
{
    /// <summary>The stable numeric id (non-zero, unique, never renumbered). It indexes <see cref="ActionMap"/>.</summary>
    public ushort Id { get; init; }

    /// <summary>The stable string id (non-empty, unique, ordinal, for example <c>jump</c>).</summary>
    public string StringId { get; init; } = string.Empty;

    /// <summary>Exactly one grammar bit.</summary>
    public Grammar Grammar { get; init; }

    /// <summary>The union of predicates the action is live in: live when any matches. Must not be empty.</summary>
    public ImmutableArray<LivePredicate> LiveIn { get; init; }

    /// <summary>Frame (through the per-tick input frame) or Immediate (a reliable request).</summary>
    public Delivery Delivery { get; init; }

    /// <summary>
    /// The frame bit. Required for <see cref="Delivery.Frame"/> actions unless the grammar is <see cref="Grammar.Axis"/> (axes
    /// travel in dedicated <c>InputFrame</c> fields); absent for <see cref="Delivery.Immediate"/> and for axes.
    /// </summary>
    public FrameAction? FrameBit { get; init; }

    /// <summary>True when the interaction target decides what a press does (Interact). Only valid with <see cref="Grammar.Hold"/>.</summary>
    public bool TargetDriven { get; init; }

    /// <summary>True when releasing also sends an Immediate commit request (Throw). Only valid with <see cref="Grammar.AimRelease"/> and <see cref="Delivery.Frame"/>.</summary>
    public bool CommitOnRelease { get; init; }

    /// <summary>The first default keyboard-and-mouse binding. <c>default</c> is a validation failure; use <see cref="InputBinding.Unbound"/>.</summary>
    public InputBinding KbmPrimary { get; init; }

    /// <summary>The second default keyboard-and-mouse binding. NumpadEnter is not stored here; it is an alias flag on the Enter binding.</summary>
    public InputBinding KbmSecondary { get; init; }

    /// <summary>The first default gamepad binding.</summary>
    public InputBinding PadPrimary { get; init; }

    /// <summary>The second default gamepad binding.</summary>
    public InputBinding PadSecondary { get; init; }

    /// <summary>The inputs that must stay bound to this action (R10), for example Esc on Back. Each must appear in a default binding. Empty or default means not reserved.</summary>
    public ImmutableArray<PhysicalInput> Reserved { get; init; }

    /// <summary>True when the action must stay bound on each device (R10): the primary slots must not be unbound.</summary>
    public bool Required { get; init; }

    /// <summary>The verb-declaring GDD path, for example <c>design/gdd/grid-inventory.md</c>. Non-empty.</summary>
    public string OwnerGdd { get; init; } = string.Empty;

    /// <summary>Former ids of this action, kept after a rename (R10). Must not collide with another action's id or alias.</summary>
    public ImmutableArray<string> Aliases { get; init; }

    /// <summary>True when <see cref="Reserved"/> names at least one input.</summary>
    public bool IsReserved => !Reserved.IsDefaultOrEmpty;

    /// <summary>True when any predicate of <see cref="LiveIn"/> matches the state. 0 B; false for an invalid state id or an empty <see cref="LiveIn"/>.</summary>
    /// <param name="stateId">The packed router state id.</param>
    /// <returns>Whether the action is live in that state.</returns>
    /// <example><code>if (def.IsLive(frame.RouterStateId)) { /* dispatch */ }</code></example>
    public bool IsLive(ushort stateId)
    {
        ImmutableArray<LivePredicate> live = LiveIn;
        if (live.IsDefault)
        {
            return false;
        }

        for (int i = 0; i < live.Length; i++)
        {
            if (live[i].IsLive(stateId))
            {
                return true;
            }
        }

        return false;
    }
}
