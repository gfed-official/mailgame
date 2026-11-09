using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// Manual Delivery's Sim module (Manual Delivery MD1, MD6; ADR-0009 erratum 2026-10-16; ADR-0003 section 4). At construction, while the
/// world is <c>Constructed</c>, it builds its own <see cref="DestinationRegistry"/> from the world's lots and the content, the same way
/// Mail does (so both see the same ids, ordinals, anchor points and types), and the per-destination <c>holdStartTick</c> clamp bound.
/// The composition root registers the Deliver anchors over <see cref="Registry"/> (<see cref="DeliverAnchors.RegisterAll"/>). It binds
/// the <see cref="DeliveryOpKinds.DeliverMatching"/> handler and nothing else: no phase, no event, no dump section, no state between
/// ticks (TR-delivery-016). Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var delivery = new DeliverySystem(players, mail, inventory, world, content, slots);
/// RegisterResult r = DeliverAnchors.RegisterAll(players, delivery.Registry, content, new DeliverInteractable(stats));
/// </code>
/// </example>
[SimSystem(SystemId.ManualDelivery)]
public sealed class DeliverySystem : ISimSystem
{
    private readonly DestinationRegistry _registry;
    private readonly DeliverMatchingHandler _handler;

    /// <summary>Creates the module, its destination registry and its handler.</summary>
    /// <param name="players">Player Character's API (<c>CheckInteraction</c>).</param>
    /// <param name="mail">Mail &amp; Destinations' API (<c>ConsumeHand</c>, destination facts).</param>
    /// <param name="inventory">Grid Inventory's API (the host panel view the scan reads).</param>
    /// <param name="world">The world; only its lots are read, here.</param>
    /// <param name="content">The accepted content set (destination types, the closing grace).</param>
    /// <param name="slots">The player slot view (the actor's slot).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The destination registry rejects the world's lots or the content (Mail, built first from the same inputs, reports it as <c>MailRegistryInvalid</c>).</exception>
    public DeliverySystem(IPlayersApi players, IMailApi mail, IInventoryApi inventory, IWorldQuery world, ContentSet content, ISlotView slots)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(slots);
        _registry = new DestinationRegistry(world, content);
        _handler = new DeliverMatchingHandler(players, mail, inventory, slots, _registry, HoldWindows(_registry, content));
    }

    /// <summary>The destination registry built at construction (the composition root registers the Deliver anchors over it).</summary>
    internal DestinationRegistry Registry => _registry;

    /// <inheritdoc />
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindOp(DeliveryDescriptors.DeliverMatching, _handler);
    }

    /// <summary>
    /// The MD6 clamp bound of each destination, ticks: <c>maxHoldTicks + closeGraceTicks</c>, where
    /// <c>maxHoldTicks = ceil(max(manualInsertHold, holdCeil) × 30 / 1000)</c> for its type (60 at the 2.0 s ceiling, so 150 with the
    /// default 90-tick grace). 0 for a destination whose type has no Deliver anchor (<c>manualInsertHold</c> 0), which the handler reads
    /// as "not a Deliver anchor".
    /// </summary>
    internal static int[] HoldWindows(DestinationRegistry registry, ContentSet content)
    {
        var windows = new int[registry.Count];
        for (int i = 0; i < windows.Length; i++)
        {
            int holdMs = content.DestinationTypes[registry.TypeOf(new DestinationOrdinal((ushort)i))].ManualInsertHoldMs;
            if (holdMs == 0)
            {
                continue;
            }

            long ceilMs = Math.Max(holdMs, HoldSettings.DefaultHoldCeilMs);
            int maxHoldTicks = (int)(((ceilMs * SimSettings.TickRateHz) + 999) / 1000);
            windows[i] = maxHoldTicks + content.Mail.CloseGraceTicks;
        }

        return windows;
    }
}
