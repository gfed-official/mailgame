namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The verb owner's handler for one immediate action (ADR-0012 section 6): Grid Inventory's panel ops, Throwing's release
/// builder, or the host shell and camera rig for the local verbs (Pause, Shoulder swap, Hotbar slot). Registered at boot with
/// <see cref="InputRouter.RegisterSink"/> and called from <see cref="InputRouter.DispatchImmediates"/>, before
/// <c>FrameRunner.RunFrame</c>, in the frame the event arrived. The sink builds and sends its own request; the router never does.
/// </summary>
/// <example>
/// <code>
/// sealed class DropSink(ClientAdapter adapter) : IImmediateActionSink
/// {
///     public void OnImmediate(in ImmediateAction a) { /* build the Drop op from published state and send it */ }
/// }
/// </code>
/// </example>
public interface IImmediateActionSink
{
    /// <summary>Handles one dispatched immediate. Must not throw and should allocate 0 B.</summary>
    /// <param name="a">The immediate action.</param>
    void OnImmediate(in ImmediateAction a);
}
