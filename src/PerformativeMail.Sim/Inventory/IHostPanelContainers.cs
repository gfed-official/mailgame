using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The container behind each section of a host panel view (<see cref="IInventoryApi.HostPanelView"/>), so the host
/// <c>deliver_matching</c> handler can name a scanned entry as an <see cref="EntryRef"/> for <c>IMailApi.ConsumeHand</c>. Implemented
/// by <see cref="HostPanelView"/> next to the ADR-0016 <see cref="IPanelView"/> shape, which it leaves unchanged; internal to the Sim.
/// Read-only, allocation-free.
/// </summary>
/// <example><code>if (inventory.HostPanelView(slot) is IHostPanelContainers c) { ContainerId hotbar = c.ContainerOf(PanelSection.Hotbar); }</code></example>
internal interface IHostPanelContainers
{
    /// <summary>The container of section <paramref name="s"/> of this view's slot; <c>default</c> for a section with none (the backpack at M-1a).</summary>
    /// <param name="s">The panel section.</param>
    /// <returns>The container id.</returns>
    ContainerId ContainerOf(PanelSection s);
}
