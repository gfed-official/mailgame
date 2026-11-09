using PerformativeMail.Client.Core.Presentation;

namespace PerformativeMail.Game.Views;

/// <summary>
/// The view-catalog handle in <c>PresenterDeps</c> (godot-host-shell story 008): created empty with the deps at boot step 7 and filled
/// once by boot step 8 with the three resolved catalogs, before the first frame. Read-only for views. Main thread only.
/// </summary>
/// <example><code>PackedScene? scene = deps.Views[DefTableId.Items].Scene(def);</code></example>
public sealed class ResolvedViewTables
{
    private ResolvedViews? _items;
    private ResolvedViews? _mailKinds;
    private ResolvedViews? _destinationTypes;

    /// <summary>True once boot step 8 has stored the catalogs.</summary>
    public bool IsResolved => _items is not null;

    /// <summary>One table's resolved catalog.</summary>
    /// <param name="table">The table.</param>
    /// <exception cref="InvalidOperationException">Read before boot step 8.</exception>
    public ResolvedViews this[DefTableId table] => (table switch
    {
        DefTableId.Items => _items,
        DefTableId.MailKinds => _mailKinds,
        DefTableId.DestinationTypes => _destinationTypes,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unknown def table."),
    }) ?? throw new InvalidOperationException("View catalogs are resolved at boot step 8; read them after boot.");

    internal void Set(ResolvedViews items, ResolvedViews mailKinds, ResolvedViews destinationTypes)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException("View catalogs are resolved once per boot.");
        }

        _items = items;
        _mailKinds = mailKinds;
        _destinationTypes = destinationTypes;
    }
}
