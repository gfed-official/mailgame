using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// Registers the Deliver anchors (Manual Delivery GDD MD1; TR-delivery-001; ADR-0014 section 1, ADR-0017 section 3): one per
/// destination whose type has a <see cref="DestinationTypeDef.ManualInsertHoldMs"/> above 0, at <see cref="AnchorRef"/> slot 0 of the
/// destination entity and at the registry's interaction anchor point. Mode, priority (Pick-up + 1) and the default reach come from
/// Player Character's <see cref="VerbCatalogue"/> through <see cref="AnchorDef.For"/>; only the hold base is the destination type's.
/// Called once per run while the world is <c>Constructed</c>. Delivery never unregisters or moves these anchors.
/// </summary>
/// <example>
/// <code>
/// RegisterResult r = DeliverAnchors.RegisterAll(players, registry, content, new DeliverInteractable(sheet));
/// if (r != RegisterResult.Ok) { /* the composition case reports it */ }
/// </code>
/// </example>
internal static class DeliverAnchors
{
    /// <summary>
    /// Registers one Deliver anchor per eligible destination, in ordinal order, all sharing <paramref name="impl"/>. Stops at the
    /// first result other than <see cref="RegisterResult.Ok"/> and returns it (anchors registered before it stay registered).
    /// </summary>
    /// <param name="players">The Players API that owns the anchor registry.</param>
    /// <param name="registry">The destination registry (ids, anchor points, types).</param>
    /// <param name="content">The content set whose destination types give the hold base.</param>
    /// <param name="impl">The shared Deliver hooks.</param>
    /// <returns><see cref="RegisterResult.Ok"/> when every eligible destination registered (including when none is eligible); else the first failure.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <example><code>RegisterResult r = DeliverAnchors.RegisterAll(players, registry, content, impl);   // Ok, 10 anchors on the M-1a street</code></example>
    public static RegisterResult RegisterAll(IPlayersApi players, DestinationRegistry registry, ContentSet content, DeliverInteractable impl)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(impl);

        for (int i = 0; i < registry.Count; i++)
        {
            var o = new DestinationOrdinal((ushort)i);
            DestinationTypeDef type = content.DestinationTypes[registry.TypeOf(o)];
            if (type.ManualInsertHoldMs == 0)
            {
                continue;
            }

            AnchorDef def = AnchorDef.For(new AnchorRef(registry.EntityIdOf(o), 0), registry.AnchorOf(o), Verb.Deliver);
            def.HoldBaseMs = type.ManualInsertHoldMs;
            RegisterResult r = players.RegisterAnchor(in def, impl);
            if (r != RegisterResult.Ok)
            {
                return r;
            }
        }

        return RegisterResult.Ok;
    }
}
