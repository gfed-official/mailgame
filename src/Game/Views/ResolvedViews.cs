using Godot;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Views;

/// <summary>
/// One resolved view catalog (godot-host-shell story 008): the scene of every def by <see cref="DefIndex"/> (the placeholder where the
/// catalog has no entry) and what resolution found. Immutable; produced by <see cref="ViewCatalogResolver.Resolve"/>.
/// </summary>
/// <example><code>PackedScene scene = deps.Views[DefTableId.DestinationTypes].SceneFor("house");</code></example>
public sealed class ResolvedViews
{
    private readonly string[] _defIds;
    private readonly ViewMap<PackedScene> _map;

    internal ResolvedViews(DefTableId table, string[] defIds, ViewMap<PackedScene> map)
    {
        Table = table;
        _defIds = defIds;
        _map = map;
    }

    /// <summary>The def table.</summary>
    public DefTableId Table { get; }

    /// <summary>Element <c>i</c> is the scene of the def with ordinal <c>i</c>; the table's def count long.</summary>
    public IReadOnlyList<PackedScene?> ByDefIndex => _map.ByDefIndex;

    /// <summary>Every finding, in catalog then def order.</summary>
    public IReadOnlyList<ViewFinding> Findings => _map.Findings;

    /// <summary>Defs resolved to a catalog entry (the rest hold the placeholder).</summary>
    public int Resolved => _map.Mapped;

    /// <summary>Findings that fail boot.</summary>
    public int Errors => _map.Errors;

    /// <summary>Findings that only warn.</summary>
    public int Warnings => _map.Warnings;

    /// <summary>The scene of a def.</summary>
    /// <param name="def">The def's ordinal in this table.</param>
    /// <returns>Its scene or the placeholder.</returns>
    public PackedScene? Scene(DefIndex def) => _map.ByDefIndex[def.Value];

    /// <summary>The scene of a def by id; null when the table has no such def.</summary>
    /// <param name="defId">The def id.</param>
    /// <returns>Its scene or the placeholder; null for an unknown id.</returns>
    public PackedScene? SceneFor(string defId)
    {
        int i = Array.IndexOf(_defIds, defId);
        return i < 0 ? null : _map.ByDefIndex[i];
    }
}
