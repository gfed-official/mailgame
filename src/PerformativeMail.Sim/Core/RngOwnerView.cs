using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The <see cref="IRngStreams"/> view of one owner: <see cref="Create"/> succeeds only for names whose
/// <see cref="StreamName.Owner"/> equals the view's owner. Obtain through <see cref="RngRegistry.ViewFor"/>.
/// </summary>
/// <example>
/// <code>
/// IRngStreams view = registry.ViewFor(SystemId.Economy);
/// RngStream shop = view.Create(in RngNames.Shop, 0);
/// </code>
/// </example>
public sealed class RngOwnerView : IRngStreams
{
    private readonly RngRegistry _registry;
    private readonly SystemId _owner;

    internal RngOwnerView(RngRegistry registry, SystemId owner)
    {
        _registry = registry;
        _owner = owner;
    }

    /// <summary>The system this view belongs to.</summary>
    public SystemId Owner => _owner;

    /// <inheritdoc />
    public RngStream Create(in StreamName name, uint index)
    {
        if (!Exposes(in name))
        {
            _registry.Counters.Add(CounterId.RngRefused);
            SimAssert.Fail(SimAssertCode.RngOwnerMismatch);
            return _registry.Refuse(in name, index);
        }

        return _registry.Create(in name, index);
    }

    /// <summary>
    /// The registered stream names this view lets its holder create, in ordinal order: exactly the names for which
    /// <see cref="Create"/> does not refuse. Inspection only (the SC-ARCH-1 owner-only test, ADR-0005 section 3); allocates,
    /// so never call it on the tick path.
    /// </summary>
    /// <example>
    /// <code>
    /// var view = (RngOwnerView)registry.ViewFor(SystemId.Throwing);
    /// int none = view.ExposedNames().Length; // 0: Throwing owns no stream
    /// </code>
    /// </example>
    /// <returns>A new array of the exposed names; empty for an owner of no stream and for <see cref="SystemId.None"/>.</returns>
    internal StreamName[] ExposedNames()
    {
        var names = new List<StreamName>();
        for (int i = 0; i < RngNames.Count; i++)
        {
            StreamName name = RngNames.ByOrdinal(i);
            if (Exposes(in name))
            {
                names.Add(name);
            }
        }

        return names.ToArray();
    }

    // The one ownership predicate, shared by Create and ExposedNames so the inspection reports what the view grants.
    private bool Exposes(in StreamName name)
        => _owner != SystemId.None && name.Owner == _owner && RngNames.IsRegistered(in name);
}
