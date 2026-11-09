using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>Narrow wallet port of the HUD (ADR-0018 section 2.2). Wire after WP-A part 2: adapt <c>WalletMirror</c> (see <c>Game/Hud/HudSources</c>).</summary>
public interface IHudWallet
{
    /// <summary>The last replicated balance.</summary>
    Cents Balance { get; }
}

/// <summary>Narrow run port of the HUD: the shift phase and the tick the Delivery phase ends. Adapt <c>RunMirror</c>.</summary>
public interface IHudRun
{
    /// <summary>The shift phase byte.</summary>
    byte Phase { get; }

    /// <summary>The tick the Delivery phase ends; 0 before Delivery starts.</summary>
    uint DeadlineTick { get; }
}

/// <summary>Narrow world-string port: address text for a world display id. Adapt <c>WorldTables.TryGetDisplayText</c>.</summary>
public interface IHudText
{
    /// <summary>Looks the text up.</summary>
    /// <param name="id">The world display id.</param>
    /// <param name="text">The text when found.</param>
    /// <returns>True when found.</returns>
    bool TryGetDisplayText(StringId id, out string text);
}
