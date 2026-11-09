using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery;

/// <summary>MD2's client-only unavailable rows 1 to 3 (manual-delivery story 003 evaluates them), as the prompt shows them.</summary>
public enum DeliverUnavailable : byte
{
    /// <summary>Available as far as rows 1 to 3 go.</summary>
    None = 0,

    /// <summary>Row 1: Panel context (<c>deliver.close_panel</c>).</summary>
    ClosePanel = 1,

    /// <summary>Row 2: swim depth (<c>deliver.dry_land</c>).</summary>
    DryLand = 2,

    /// <summary>Row 3: an own non-optimistic op is pending (<c>deliver.pending</c>).</summary>
    Pending = 3,

    /// <summary>Row 3 while the pending op is a sort (<c>deliver.sorting</c>).</summary>
    Sorting = 4,
}

/// <summary>
/// What <see cref="DeliverInteractable.WritePrompt(in DeliverPromptInput, ref Players.PromptArgs)"/> renders into the prompt bytes: this
/// tick's <see cref="DeliverPreview"/> branch (story 002), MD2's rows 1 to 3 and the settle state (story 003's), and the two addresses as
/// world display ids (the destination's, and the selected piece's for MISDELIVER_ONE). A plain value.
/// </summary>
/// <example><code>var input = new DeliverPromptInput { Branch = branch, DestinationAddress = lotDisplay };</code></example>
public readonly struct DeliverPromptInput
{
    /// <summary>The preview's branch and payload.</summary>
    public DeliverBranch Branch { get; init; }

    /// <summary>MD2 rows 1 to 3; any value but <see cref="DeliverUnavailable.None"/> wins over the branch.</summary>
    public DeliverUnavailable Unavailable { get; init; }

    /// <summary>The branch has not settled yet (the tracker's <c>CanStartHoldNext</c> is false).</summary>
    public bool Settling { get; init; }

    /// <summary>The destination's address as a world display id.</summary>
    public StringId DestinationAddress { get; init; }

    /// <summary>MISDELIVER_ONE: the selected piece's address as a world display id.</summary>
    public StringId SelectedAddress { get; init; }
}
