using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Game.Views;
using PerformativeMail.Sim.Content;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// godot-host-shell story 008 AC-3 over the real boot: step 8 resolves the three editor-saved catalogs against the M-1a content with
/// no art, so it logs one <c>VIEW warn missing view</c> line per def (4 items + 5 mail kinds + 1 destination type = 10), no error, the
/// per-table <c>resolved/total</c> counts, and reaches <c>ok</c>; <c>PresenterDeps.Views</c> holds, per table, the def count of scenes,
/// each the catalog's placeholder. A <c>release = true</c> resolve of the same catalogs reports 10 errors (a release boot would fail)
/// and leaves the booted deps untouched.
/// </summary>
[SmokeScenario("view-catalog")]
public sealed class ViewCatalogSmoke : SmokeScenario
{
    private PresenterProbeView _probe = null!;

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        _probe = new PresenterProbeView { Tag = "views" };
        loop.GetNode("World").AddChild(_probe);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        IReadOnlyList<string> lines = ctx.Loop.BootLines;
        foreach (string line in lines.Where(l => l.StartsWith("BOOT 8 ", StringComparison.Ordinal) || l.StartsWith("VIEW ", StringComparison.Ordinal)))
        {
            ctx.Pass($"boot log: {line}");
        }

        Expect(ctx, "step 8 ok", BootSmokeFaults.Logged(lines, "BOOT 8 views ok"), true);
        Expect(ctx, "VIEW warn missing view lines", lines.Count(l => l.StartsWith("VIEW warn missing view for ", StringComparison.Ordinal)), 10);
        Expect(ctx, "VIEW error lines", lines.Count(l => l.StartsWith("VIEW error", StringComparison.Ordinal)), 0);
        Expect(ctx, "step 8 per-table counts", BootSmokeFaults.Logged(lines, "BOOT 8 views: resolved items 0/4, mail_kinds 0/5, destination_types 0/1 release=False errors=0"), true);
        if (_probe.Deps is not { } deps || !Expect(ctx, "deps hold the resolved catalogs", deps.Views.IsResolved, true))
        {
            return;
        }

        ContentSet set = BuiltInContent.CreateM1a();
        int releaseErrors = 0;
        foreach (DefTableId table in Enum.GetValues<DefTableId>())
        {
            ResolvedViews views = deps.Views[table];
            ViewCatalog catalog = ViewCatalogResolver.Load(table)!;
            Expect(ctx, $"{table} byDefIndex length = def count", views.ByDefIndex.Count, ViewMapping.DefIds(set, table).Length);
            Expect(ctx, $"{table} every def shows the placeholder", views.ByDefIndex.All(s => ReferenceEquals(s, catalog.Placeholder)), true);
            releaseErrors += ViewCatalogResolver.Resolve(catalog, set, table, release: true).Errors;
        }

        Expect(ctx, "release = true resolve errors", releaseErrors, 10);
        Expect(ctx, "booted deps unchanged (warnings, errors)", Enum.GetValues<DefTableId>().Sum(t => deps.Views[t].Warnings) + "," + Enum.GetValues<DefTableId>().Sum(t => deps.Views[t].Errors), "10,0");
    }
}
