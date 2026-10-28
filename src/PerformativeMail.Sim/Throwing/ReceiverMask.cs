namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Which receivers <see cref="FlightSweep.FirstContact"/> tests (ADR-0015 section 4 and the throw preview; TR-throwing-003):
/// the host asks for <see cref="All"/>, the aim preview for <see cref="StaticOnly"/>. One code path, one flag.
/// </summary>
/// <example>
/// <code>
/// var view = new ReceiverView(world, world.Bounds, ranks, ReceiverMask.StaticOnly) { Mailboxes = volumes };
/// </code>
/// </example>
public enum ReceiverMask : byte
{
    /// <summary>Every span in the view (the host's <c>Throwing.Step</c>).</summary>
    All = 0,

    /// <summary>
    /// Mailbox volumes, world solids, the map sides, terrain and water only: <see cref="ReceiverView.Catchers"/>,
    /// <see cref="ReceiverView.Cylinders"/> and <see cref="ReceiverView.Belts"/> are skipped (the story 002 AC).
    /// </summary>
    StaticOnly = 1,
}
