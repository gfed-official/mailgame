using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Views;
using PerformativeMail.Game.Views.Destinations;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Boot steps 7 and 8's Godot half (godot-host-shell stories 006, 008, 009). Step 7 (<see cref="Initialize"/>) builds the one
/// <see cref="PresenterDeps"/> from the step's inputs (with an empty <see cref="ResolvedViewTables"/> and the
/// <see cref="EntityViewRegistry"/>), registers the <see cref="DestinationsHost"/> over <c>World/Destinations</c> as a world-build step,
/// calls <c>Initialize(deps)</c> on the World, Hud and Audio presenters in that order (ADR-0019 section 2), then runs the World presenter's
/// world-build steps. Step 8 (<see cref="ResolveViews"/>) loads and resolves the three editor-saved view catalogs, logs per-table counts
/// and one <c>VIEW</c> line per finding, stores them in the deps and attaches the per-lot views. <c>GameLoop</c> creates it over its own
/// children <c>World</c>, <c>Hud</c> and <c>Audio</c>. Main thread only.
/// </summary>
/// <param name="world">The <c>Main/World</c> presenter.</param>
/// <param name="hud">The <c>Main/Hud</c> presenter.</param>
/// <param name="audio">The <c>Main/Audio</c> presenter.</param>
/// <example><code>var presenters = new PresentersBoot(world, hud, audio); ctx.Presenters = presenters; ctx.Views = presenters;</code></example>
public sealed class PresentersBoot(WorldPresenter world, HudPresenter hud, AudioPresenter audio) : IPresentersBoot, IViewsBoot
{
    private static readonly DefTableId[] _tables = [DefTableId.Items, DefTableId.MailKinds, DefTableId.DestinationTypes];
    private PresenterDeps? _deps;
    private DestinationsHost? _destinations;

    /// <summary>The deps step 7 built; null before step 7.</summary>
    public PresenterDeps? Deps => _deps;

    /// <inheritdoc />
    public void Initialize(PresentationInputs inputs, BootLog log)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(log);
        var deps = new PresenterDeps
        {
            World = inputs.World,
            Clock = inputs.Clock,
            Events = inputs.Events,
            Published = inputs.Published,
            Audio = audio,
            Views = new ResolvedViewTables(),
            Entities = new EntityViewRegistry(PresentationCaps.EntityViews),
        };
        _deps = deps;
        if (world.GetNodeOrNull<Node3D>("Destinations") is { } destinations)
        {
            _destinations = new DestinationsHost(destinations, deps, world.Register);
            world.RegisterBuildStep(_destinations);
        }

        world.Initialize(deps);
        hud.Initialize(deps);
        audio.Initialize(deps);
        log.Write($"BOOT 7 world: presenters World,Hud,Audio initialized views={world.ViewCount},{hud.ViewCount},{audio.ViewCount}");
        int built = world.BuildWorld(inputs.World);
        log.Write(built == 0 ? "BOOT 7 world: no world-build steps" : $"BOOT 7 world: world-build steps ran={built}");
        if (_destinations is not null)
        {
            log.Write($"BOOT 7 world: destinations lots={_destinations.LotCount}");
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Step 7 has not run.</exception>
    public int ResolveViews(ContentSet content, bool release, BootLog log)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(log);
        PresenterDeps deps = _deps ?? throw new InvalidOperationException("Boot step 8 needs step 7's presenter deps.");
        var resolved = new ResolvedViews?[_tables.Length];
        int errors = 0;
        var counts = new List<string>(_tables.Length);
        for (int t = 0; t < _tables.Length; t++)
        {
            DefTableId table = _tables[t];
            if (ViewCatalogResolver.Load(table) is not { } catalog)
            {
                log.Write($"VIEW error no view catalog at {ViewCatalogResolver.CatalogPath(table)}");
                errors++;
                continue;
            }

            ResolvedViews views = ViewCatalogResolver.Resolve(catalog, content, table, release);
            foreach (ViewFinding finding in views.Findings)
            {
                log.Write(ViewMapping.Describe(table, in finding));
            }

            errors += views.Errors;
            counts.Add($"{ViewMapping.TableName(table)} {views.Resolved}/{views.ByDefIndex.Count}");
            resolved[t] = views;
        }

        log.Write($"BOOT 8 views: resolved {string.Join(", ", counts)} release={release} errors={errors}");
        if (errors > 0)
        {
            return errors;
        }

        deps.Views.Set(resolved[0]!, resolved[1]!, resolved[2]!);
        if (_destinations is not null)
        {
            log.Write($"BOOT 8 views: destination views attached={_destinations.AttachViews(deps.Views[DefTableId.DestinationTypes])}");
        }

        return 0;
    }
}
