using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Hud;

/// <summary>
/// Adapters from the Sim mirrors to the HUD's narrow ports (stories hud-001/003). Boot composition (WP-A part 2) builds
/// <c>new HudState(HudSources.Wallet(walletMirror), HudSources.Run(runMirror), interactionQuery, renderClock)</c> and puts it in
/// <c>PresenterDeps.HudState</c>.
/// </summary>
public static class HudSources
{
    /// <summary>Adapts the wallet mirror.</summary>
    /// <param name="mirror">The mirror.</param>
    /// <returns>The port.</returns>
    public static IHudWallet Wallet(WalletMirror mirror) => new WalletPort(mirror);

    /// <summary>Adapts the run mirror.</summary>
    /// <param name="mirror">The mirror.</param>
    /// <returns>The port.</returns>
    public static IHudRun Run(RunMirror mirror) => new RunPort(mirror);

    /// <summary>Adapts the world tables' display strings; with any other <see cref="IWorldQuery"/> every lookup misses.</summary>
    /// <param name="world">The world query.</param>
    /// <returns>The port.</returns>
    public static IHudText Text(IWorldQuery world) => new TextPort(world as WorldTables);

    private sealed class WalletPort(WalletMirror mirror) : IHudWallet
    {
        public Cents Balance => mirror.Balance;
    }

    private sealed class RunPort(RunMirror mirror) : IHudRun
    {
        public byte Phase => (byte)mirror.Phase;

        public uint DeadlineTick => mirror.DeadlineTick;
    }

    private sealed class TextPort(WorldTables? tables) : IHudText
    {
        public bool TryGetDisplayText(StringId id, out string text)
        {
            if (tables is null)
            {
                text = string.Empty;
                return false;
            }

            return tables.TryGetDisplayText(id, out text);
        }
    }
}
