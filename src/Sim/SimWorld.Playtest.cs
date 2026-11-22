using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Playtest nits 10, 13 and 18: a stopped shift clock, a carrier put before a mailbox with mail
    /// for it, a run that starts at a later shift, a quota that ends nothing, a raid right after the
    /// shift starts and packages for the cannon, each asked for by a player's Hello
    /// (<see cref="PlaytestFlags"/>).
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Far above anything a run's spawner hands out.</summary>
        private const uint PlaytestMailIdBase = 0xF0000000;
        private uint _playtestMail;

        /// <summary>True while a player who asked for <see cref="PlaytestFlags.NoTimer"/> is in the game.</summary>
        public bool ClockStopped => AnyPlaytest(PlaytestFlags.NoTimer);
        /// <summary>True while a player who asked for <see cref="PlaytestFlags.NoQuota"/> is in the game.</summary>
        public bool QuotaWaived => AnyPlaytest(PlaytestFlags.NoQuota);
        /// <summary>True while a player who asked for <see cref="PlaytestFlags.RaidNow"/> is in the game.</summary>
        public bool RaidsAtShiftStart => AnyPlaytest(PlaytestFlags.RaidNow);

        /// <summary>Who has lost their connection asks for nothing until they are back (their Hello says it again).</summary>
        private bool AnyPlaytest(PlaytestFlags flag)
        {
            foreach (var p in _players)
                if ((p.Playtest & flag) != 0 && !p.Away) return true;
            return false;
        }

        /// <summary>The most money any player in the game asked the run to start with (cents).</summary>
        public int StartWallet
        {
            get
            {
                int coins = 0;
                foreach (var p in _players)
                    if (!p.Away) coins = Math.Max(coins, p.PlaytestWallet);
                return coins;
            }
        }

        /// <summary>
        /// The shift a run starts at: the latest any player in the game asked for, the first if
        /// nobody did. Never past the last shift.
        /// </summary>
        public int StartShift
        {
            get
            {
                int shift = 1;
                foreach (var p in _players)
                    if (!p.Away) shift = Math.Max(shift, p.PlaytestShift);
                return Math.Min(shift, ShiftCount);
            }
        }

        /// <summary>
        /// What a joining player asked for. Unknown bits are ignored. A start shift counts when the
        /// next run starts, or at once for a run still being generated or in the Prep of an earlier shift; a raid asked
        /// for in a Delivery whose raid has not been announced comes now.
        /// </summary>
        public void SetPlaytest(Player player, PlaytestFlags flags, int startShift = 0, int startWallet = 0)
        {
            player.Playtest = flags & PlaytestMask.All;
            player.PlaytestShift = (byte)Math.Clamp(startShift, 0, ShiftCount);
            player.PlaytestWallet = Math.Clamp(startWallet, 0, PlaytestMask.MaxWallet);
            // Asked for in a run under way, the money is there at once.
            if (Run.Phase != RunPhase.Lobby && Run.Phase != RunPhase.Results) Economy.TopUp(player.PlaytestWallet);
            if (Run.Phase == RunPhase.Prep && player.PlaytestShift > Run.Shift)
            {
                Run.Shift = player.PlaytestShift;
                BeginPrep();
            }
            // While the run is being set up, its first shift is still to come.
            if (Run.Phase == RunPhase.Generating && player.PlaytestShift > Run.Shift) Run.Shift = player.PlaytestShift;
            if ((player.Playtest & PlaytestFlags.RaidNow) != 0 && Run.Phase == RunPhase.Delivery && Raid == RaidState.None) ScheduleRaid();
            if ((player.Playtest & PlaytestFlags.AtMailbox) != 0) ParkAtMailbox(player);
            GivePlaytestPackages(player);
        }

        /// <summary>
        /// A stopped Delivery clock keeps all the time it has: the end of the shift moves away a
        /// tick at a time, and with it a raid that has not been announced yet (a raid asked for at
        /// the shift's start hangs on the start, not on the end, and comes all the same).
        /// </summary>
        private void HoldShiftClock()
        {
            Run.PhaseEndTick++;
            if (_raidStartTick != 0 && Raid == RaidState.None && !_raidAtStart)
            {
                _raidWarnTick++;
                _raidStartTick++;
            }
        }

        private void ParkPlaytesters()
        {
            foreach (var p in _players)
                if ((p.Playtest & PlaytestFlags.AtMailbox) != 0 && !p.Away) ParkAtMailbox(p);
        }

        private void StockPlaytesters()
        {
            foreach (var p in _players)
                if (!p.Away) GivePlaytestPackages(p);
        }

        /// <summary>
        /// Nit 18: tops a player who asked for <see cref="PlaytestFlags.Packages"/> up to
        /// <see cref="PlaytestMask.PackageCount"/> packages the cannon fires, while mailboxes take
        /// mail. They are real mail, addressed to the mailbox of the player's seat if it takes
        /// packages, else to the first one that does.
        /// </summary>
        private void GivePlaytestPackages(Player p)
        {
            if ((p.Playtest & PlaytestFlags.Packages) == 0 || Run.Phase != RunPhase.Delivery || p.Dead || p.Away) return;
            MailKindDef? kind = null;
            foreach (var k in Content.MailKinds)
                if (k.CannonAmmo) { kind = k; break; }
            if (kind == null) return;
            DestinationDef? dest = PlaytestSpot.Mailbox(Map, p.Slot);
            if (!DeliveryRules.KindAccepted(kind, dest.Type))
            {
                dest = null;
                foreach (var d in Map.Destinations)
                    if (DeliveryRules.KindAccepted(kind, d.Type)) { dest = d; break; }
            }
            if (dest == null) return;
            for (int have = CountAmmo(p.Hotbar) + CountAmmo(p.Inventory); have < PlaytestMask.PackageCount; have++)
            {
                var item = new MailItem
                {
                    Id = PlaytestMailIdBase + _playtestMail++,
                    Kind = kind.Index,
                    Address = dest.Address,
                    Value = (ushort)kind.BaseValue,
                    SpawnShift = (byte)Run.Shift,
                    DeadlineShift = (byte)(Run.Shift + kind.DeadlineOffsetShifts),
                };
                if (!Give(p.Hotbar, ItemStack.OfMail(item)) && !Give(p.Inventory, ItemStack.OfMail(item))) break;
            }
        }

        /// <summary>
        /// Puts the player on the street side of their seat's mailbox, facing it. While mailboxes
        /// take mail the player also gets a stack addressed to it, unless they still carry one.
        /// </summary>
        private void ParkAtMailbox(Player p)
        {
            if (p.Dead) return;
            var dest = PlaytestSpot.Mailbox(Map, p.Slot);
            // A rider is parked in the saddle (playtest c59d497): the bike comes along, standing, facing the box.
            var ride = RiddenBy(p.Id);
            float radius = ride != null ? Math.Max(_move.Radius, ride.Def.Radius) : _move.Radius;
            PlaytestSpot.Stand(Map, dest, radius, out float x, out float z);
            float yaw = MathF.Atan2(-(dest.MailboxX - x), -(dest.MailboxZ - z));
            var move = new PlayerMoveState { X = x, Z = z, Y = Map.HeightAt(x, z), Grounded = true, Yaw = PlayerInput.QuantizeYaw(yaw) };
            if (ride != null && p.Move.Riding)
            {
                move.Ride = p.Move.Ride;
                Vehicle.Forward(yaw, out move.HeadX, out move.HeadZ);
                ride.X = x;
                ride.Z = z;
                ride.Heading = Vehicle.YawOf(move.HeadX, move.HeadZ, ride.Heading);
                MoveCargo(ride);
            }
            p.Move = move;
            CloseExternal(p, notify: true);
            GivePlaytestMail(p, dest);
            // After the mail, so the client that hears of it can take the stack in hand.
            Events.Add(new PlayerParkedEvent { Audience = p.Id, Destination = dest.Id });
        }

        private void GivePlaytestMail(Player p, DestinationDef dest)
        {
            if (Run.Phase != RunPhase.Delivery || CarriesMailFor(p.Hotbar, dest.Address) || CarriesMailFor(p.Inventory, dest.Address)) return;
            MailKindDef? kind = null;
            foreach (var k in Content.MailKinds)
                if (DeliveryRules.KindAccepted(k, dest.Type)) { kind = k; break; }
            if (kind == null) return;
            for (int i = 0; i < PlaytestSpot.MailCount; i++)
            {
                var item = new MailItem
                {
                    Id = PlaytestMailIdBase + _playtestMail++,
                    Kind = kind.Index,
                    Address = dest.Address,
                    Value = (ushort)kind.BaseValue,
                    SpawnShift = (byte)Run.Shift,
                    DeadlineShift = (byte)(Run.Shift + kind.DeadlineOffsetShifts),
                };
                if (!Give(p.Hotbar, ItemStack.OfMail(item)) && !Give(p.Inventory, ItemStack.OfMail(item))) break;
            }
        }

        private static bool CarriesMailFor(GridContainer container, AddressId address)
        {
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Mail && e.Stack.Address == address) return true;
            return false;
        }
    }
}
