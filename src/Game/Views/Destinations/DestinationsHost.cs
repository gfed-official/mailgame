using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Destinations;

/// <summary>
/// The static per-lot views under <c>World/Destinations</c> (ADR-0019 section 2: one per lot, not pooled; godot-host-shell story 009).
/// Boot step 7 (<see cref="Build"/>, a world-build step) creates one <c>Lot_&lt;Number&gt;</c> <see cref="Node3D"/> per
/// <see cref="IWorldQuery.Lots"/> entry at its anchor in metres, yawed by <see cref="FaceYaw.ToBam"/>, and registers it in the
/// <see cref="EntityViewRegistry"/> under the lot's destination. Boot step 8 (<see cref="AttachViews"/>), once the catalogs are resolved,
/// instantiates the destination type's scene (the placeholder at M-1a) under each lot, injects it and binds it when it is an
/// <see cref="IDestinationView"/>. Reads only <see cref="IWorldQuery"/>; no colliders. Main thread only.
/// </summary>
/// <example><code>worldPresenter.RegisterBuildStep(new DestinationsHost(destinationsNode, deps, worldPresenter.Register));</code></example>
/// <param name="parent">The parent, <c>World/Destinations</c>.</param>
/// <param name="deps">The presenters' deps (registry and resolved catalogs).</param>
/// <param name="present">Registers a presented view with the World presenter; null to skip.</param>
public sealed class DestinationsHost(Node3D parent, PresenterDeps deps, Action<IPresentedView>? present = null) : IWorldBuildStep
{
    /// <summary>The destination type every lot shows until a per-lot type reaches the client tables (every M-1a lot is a house).</summary>
    public const string DefaultTypeId = "house";

    private readonly Node3D _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    private readonly PresenterDeps _deps = deps ?? throw new ArgumentNullException(nameof(deps));
    private readonly List<(Node3D Lot, LotBinding Binding)> _lots = [];

    /// <summary>Lots built by <see cref="Build"/>.</summary>
    public int LotCount => _lots.Count;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Called twice, or a lot's destination is already registered.</exception>
    public void Build(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_lots.Count > 0)
        {
            throw new InvalidOperationException("The destinations are built once per boot.");
        }

        ReadOnlySpan<LotRecord> lots = world.Lots;
        for (int i = 0; i < lots.Length; i++)
        {
            LotRecord lot = lots[i];
            var anchor = new Vector3(lot.Anchor.X / 1000f, lot.Anchor.Y / 1000f, lot.Anchor.Z / 1000f);
            var node = new Node3D
            {
                Name = $"Lot_{lot.Number}",
                Position = anchor,
                Rotation = new Vector3(0f, FaceYaw.ToBam(lot.Face) * Mathf.Tau / 65536f, 0f),
            };
            if (!_deps.Entities.Add(lot.Destination, node))
            {
                node.Free();
                throw new InvalidOperationException($"Destination {lot.Destination.Value:X8} of lot {lot.Number} could not be registered.");
            }

            _parent.AddChild(node);
            _lots.Add((node, new LotBinding(lot.Destination, i, anchor, lot.Face, lot.Number)));
        }
    }

    /// <summary>Instantiates the destination type's scene under every lot, injects and binds it.</summary>
    /// <param name="destinationTypes">The resolved <c>destination_types</c> catalog.</param>
    /// <returns>How many views were attached.</returns>
    /// <exception cref="InvalidOperationException">The content has no <see cref="DefaultTypeId"/> destination type.</exception>
    public int AttachViews(ResolvedViews destinationTypes)
    {
        ArgumentNullException.ThrowIfNull(destinationTypes);
        PackedScene scene = destinationTypes.SceneFor(DefaultTypeId)
            ?? throw new InvalidOperationException($"No scene for destination type '{DefaultTypeId}'.");
        int attached = 0;
        foreach ((Node3D lot, LotBinding binding) in _lots)
        {
            if (!GodotObject.IsInstanceValid(lot))
            {
                continue;
            }

            Node view = scene.Instantiate();
            lot.AddChild(view);
            if (view is IInjectable<PresenterDeps> { IsInitialized: false } injectable)
            {
                injectable.Initialize(_deps);
            }

            if (view is IDestinationView destination)
            {
                destination.Bind(in binding);
            }

            if (view is IPresentedView presented)
            {
                present?.Invoke(presented);
            }

            attached++;
        }

        return attached;
    }
}
