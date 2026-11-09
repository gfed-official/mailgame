using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The production <see cref="IHotbarSelection"/> until the <c>HotbarSelect</c> op and its host-side store land (a later WP-D story;
/// throwing-catching story 010 wiring): no slot is ever reported selected (<see cref="None"/> is outside 0 to 7), so check 2 answers
/// every composed-world release <c>stale_entry</c> rather than launching an entry the player never selected. Stateless.
/// </summary>
/// <example><code>IHotbarSelection hotbar = UnsetHotbarSelection.Instance;   // SelectedIndex(slot) == 0xFF</code></example>
internal sealed class UnsetHotbarSelection : IHotbarSelection
{
    /// <summary>The index reported for every slot: no hotbar cell has it.</summary>
    public const byte None = 0xFF;

    private UnsetHotbarSelection()
    {
    }

    /// <summary>The one instance.</summary>
    public static UnsetHotbarSelection Instance { get; } = new();

    /// <inheritdoc />
    public byte SelectedIndex(PlayerSlot slot) => None;
}
