using System;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// What a client may ask for in its Hello so that a live check is reliable (playtest nits 10
    /// and 13). The host's own client asks the same way a guest's does; next to these flags the
    /// Hello carries the shift the run is to start at (<see cref="Players.Player.PlaytestShift"/>)
    /// and the money it is to start with (<see cref="Players.Player.PlaytestWallet"/>).
    /// </summary>
    [Flags]
    public enum PlaytestFlags : byte
    {
        None = 0,
        /// <summary>The shift clock stands for as long as this player is in the game.</summary>
        NoTimer = 1,
        /// <summary>The player is put before a mailbox, and has mail for it whenever mailboxes are open.</summary>
        AtMailbox = 2,
        /// <summary>A missed quota does not end the run while this player is in the game.</summary>
        NoQuota = 4,
        /// <summary>Every shift's night raid comes right after the shift starts, also in a shift that has none.</summary>
        RaidNow = 8,
        /// <summary>
        /// The player has Small Packages for the mail cannon: when a shift starts, when they join
        /// one under way, and whenever they are back on their feet.
        /// </summary>
        Packages = 16,
    }

    public static class PlaytestMask
    {
        /// <summary>Every flag there is. Not a member of the enum, so a log names the flags one by one.</summary>
        public const PlaytestFlags All = PlaytestFlags.NoTimer | PlaytestFlags.AtMailbox | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow | PlaytestFlags.Packages;
        /// <summary>What a player with <see cref="PlaytestFlags.Packages"/> is topped up to: a few shots, and the pockets stay mostly free.</summary>
        public const int PackageCount = 4;
        /// <summary>From the start of a shift to the first raiders under <see cref="PlaytestFlags.RaidNow"/>: time to read the warning.</summary>
        public const double RaidNowSeconds = 5;
        /// <summary>The most a playtest may ask the wallet to start with: $1000.00, in cents.</summary>
        public const int MaxWallet = 100000;
    }

    /// <summary>Where a player who asked for <see cref="PlaytestFlags.AtMailbox"/> stands; both ends work it out from the map.</summary>
    public static class PlaytestSpot
    {
        /// <summary>From the mailbox to the carrier's feet: well inside the reach of a hand.</summary>
        public const float StandOff = 1.3f;
        /// <summary>Enough for a delivery, a second one and a split.</summary>
        public const int MailCount = 3;
        /// <summary>
        /// In a shift with a raid, a parked carrier is let be for as long as they stay this near
        /// the spot (playtest df77576: the raid holds its line a few metres past the mailboxes
        /// next to the Post Office, and downed the carriers it found there before they could move).
        /// </summary>
        public const float GuardRadius = 4f;

        /// <summary>Each seat has a mailbox of its own, so two carriers do not stand in each other.</summary>
        public static DestinationDef Mailbox(WorldMap map, int slot) => map.Houses[slot % map.Houses.Count];

        /// <summary>The spot on the street side of a mailbox, clear of anything solid.</summary>
        public static void Stand(WorldMap map, DestinationDef dest, float radius, out float x, out float z)
        {
            const float tile = SimConstants.TileSize;
            var street = dest.Street.Rect;
            float dx = Math.Clamp(dest.MailboxX, street.X * tile, (street.X + street.W) * tile) - dest.MailboxX;
            float dz = Math.Clamp(dest.MailboxZ, street.Y * tile, (street.Y + street.H) * tile) - dest.MailboxZ;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            // A mailbox on the street itself has no street side.
            if (length < 1e-3f) { dx = 0f; dz = 1f; length = 1f; }
            x = dest.MailboxX + dx / length * StandOff;
            z = dest.MailboxZ + dz / length * StandOff;
            PlayerMovement.ResolveCollisions(ref x, ref z, map.HeightAt(x, z), map, radius);
        }
    }
}
