using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Game.Views;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// godot-host-shell story 009 AC-1: <see cref="EntityViewRegistry"/> refuses and counts a duplicate add and an add beyond its capacity,
/// and drops a node freed behind its back (stale lookup counted); <see cref="EntityViewHost"/> over the booted deps spawns under
/// <c>World/Entities</c> (instantiate, parent, inject an <see cref="IInjectable{TDeps}"/> root, register), spawns a def's resolved scene
/// (the placeholder), refuses an id already registered without leaving a node, and despawns (unregister, free).
/// </summary>
[SmokeScenario("entity-registry")]
public sealed class EntityRegistrySmoke : SmokeScenario
{
    private PresenterProbeView _probe = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        _probe = new PresenterProbeView { Tag = "registry" };
        loop.GetNode("World").AddChild(_probe);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        CheckRegistry(ctx);
        if (_probe.Deps is not { } deps)
        {
            ctx.Fail("probe has no deps");
            return;
        }

        CheckHost(ctx, ctx.Loop, deps);
    }

    private static void CheckRegistry(SmokeContext ctx)
    {
        var registry = new EntityViewRegistry(2);
        EntityId a = EntityId.Compose(EntityClass.Player, 1);
        EntityId b = EntityId.Compose(EntityClass.Player, 2);
        var nodeA = new Node3D();
        var nodeB = new Node3D();
        var extra = new Node3D();
        Expect(ctx, "add a", registry.Add(a, nodeA), true);
        Expect(ctx, "duplicate add refused", registry.Add(a, extra), false);
        Expect(ctx, "add b", registry.Add(b, nodeB), true);
        Expect(ctx, "add beyond capacity refused", registry.Add(EntityId.Compose(EntityClass.Player, 3), extra), false);
        Expect(ctx, "refusals counted (duplicate, capacity), count", $"{registry.DuplicateAdds},{registry.OverCapacityAdds},{registry.Count}", "1,1,2");
        nodeA.Free();
        Expect(ctx, "lookup of a freed node fails", registry.TryGet(a, out _), false);
        Expect(ctx, "stale lookup counted and dropped (stale, count)", $"{registry.StaleLookups},{registry.Count}", "1,1");
        Expect(ctx, "live lookup", registry.TryGet(b, out Node3D found) && ReferenceEquals(found, nodeB), true);
        nodeB.Free();
        extra.Free();
    }

    private static void CheckHost(SmokeContext ctx, GameLoop loop, PresenterDeps deps)
    {
        var entities = loop.GetNode<Node3D>("World/Entities");
        var host = new EntityViewHost(entities, deps);
        var probeScene = new PackedScene();
        var template = new RegistryProbeView { Name = "Probe" };
        probeScene.Pack(template);
        template.Free();

        EntityId id = EntityId.Compose(EntityClass.Player, 7);
        int before = entities.GetChildCount();
        Node3D? spawned = host.Spawn(id, probeScene);
        Expect(ctx, "spawn parents under World/Entities", spawned?.GetParent() == entities, true);
        Expect(ctx, "spawn injects the deps", spawned is RegistryProbeView { InjectedDeps: { } d } && ReferenceEquals(d, deps), true);
        Expect(ctx, "spawn registers", deps.Entities.TryGet(id, out Node3D got) && ReferenceEquals(got, spawned), true);
        Expect(ctx, "duplicate spawn refused, no node left", host.Spawn(id, probeScene) is null && entities.GetChildCount() == before + 1, true);

        EntityId item = EntityId.Compose(EntityClass.WorldItem, 1);
        Node3D? forDef = host.SpawnForDef(item, deps.Views[DefTableId.Items], DefIndex.FromValue(0));
        Expect(ctx, "spawn for def shows the placeholder", forDef?.Name.ToString() ?? "null", "PlaceholderView");

        Expect(ctx, "despawn", host.Despawn(id) && host.Despawn(item), true);
        Expect(ctx, "despawned ids unregistered", deps.Entities.TryGet(id, out _) || deps.Entities.TryGet(item, out _), false);
        Expect(ctx, "despawned nodes queued for free", spawned!.IsQueuedForDeletion() && forDef!.IsQueuedForDeletion(), true);
    }
}
