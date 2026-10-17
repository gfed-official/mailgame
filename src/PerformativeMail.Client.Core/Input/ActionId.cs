namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The stable numeric ids of the M-1a actions (ADR-0012 section 2). A <c>ushort</c> that indexes <see cref="ActionMap"/>
/// directly. Append-only: never renumber, reuse or delete an id (renames keep the id and add an alias); <c>0</c> is "no
/// action". A golden test pins every value.
/// </summary>
/// <example>
/// <code>
/// ActionDef? jump = map.Get(ActionId.Jump);
/// ushort slot3 = ActionId.HotbarSlot3;
/// </code>
/// </example>
public static class ActionId
{
    /// <summary>No action. Never valid.</summary>
    public const ushort None = 0;

    /// <summary>Move (axis).</summary>
    public const ushort Move = 1;

    /// <summary>Look (axis).</summary>
    public const ushort Look = 2;

    /// <summary>Sprint (held state).</summary>
    public const ushort Sprint = 3;

    /// <summary>Jump.</summary>
    public const ushort Jump = 4;

    /// <summary>Interact (per target).</summary>
    public const ushort Interact = 5;

    /// <summary>Throw (aim-release).</summary>
    public const ushort Throw = 6;

    /// <summary>Tool / attack (stub).</summary>
    public const ushort Tool = 7;

    /// <summary>Hotbar step (wheel, D-pad left / right).</summary>
    public const ushort HotbarStep = 8;

    /// <summary>Hotbar slot 1.</summary>
    public const ushort HotbarSlot1 = 9;

    /// <summary>Hotbar slot 2.</summary>
    public const ushort HotbarSlot2 = 10;

    /// <summary>Hotbar slot 3.</summary>
    public const ushort HotbarSlot3 = 11;

    /// <summary>Hotbar slot 4.</summary>
    public const ushort HotbarSlot4 = 12;

    /// <summary>Hotbar slot 5.</summary>
    public const ushort HotbarSlot5 = 13;

    /// <summary>Hotbar slot 6.</summary>
    public const ushort HotbarSlot6 = 14;

    /// <summary>Hotbar slot 7.</summary>
    public const ushort HotbarSlot7 = 15;

    /// <summary>Hotbar slot 8.</summary>
    public const ushort HotbarSlot8 = 16;

    /// <summary>Drop (the whole entry).</summary>
    public const ushort Drop = 17;

    /// <summary>Throw pressed while holding: the "Put it away first" message action.</summary>
    public const ushort ThrowWhileHolding = 18;

    /// <summary>Tool pressed while holding: the "Put it away first" message action.</summary>
    public const ushort ToolWhileHolding = 19;

    /// <summary>Open panel.</summary>
    public const ushort OpenPanel = 20;

    /// <summary>Shoulder swap (camera).</summary>
    public const ushort ShoulderSwap = 21;

    /// <summary>Pause.</summary>
    public const ushort Pause = 22;

    /// <summary>Back (the R5 ladder).</summary>
    public const ushort Back = 23;

    /// <summary>Grid cursor.</summary>
    public const ushort GridCursor = 24;

    /// <summary>Pick up / place.</summary>
    public const ushort PickPlace = 25;

    /// <summary>Rotate the held entry.</summary>
    public const ushort Rotate = 26;

    /// <summary>Cancel / return (Holding-open).</summary>
    public const ushort CancelReturn = 27;

    /// <summary>Quick-move.</summary>
    public const ushort QuickMove = 28;

    /// <summary>Close panel (keep the cursor entry).</summary>
    public const ushort ClosePanel = 29;
}
