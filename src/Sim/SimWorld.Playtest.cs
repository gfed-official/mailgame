using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Playtest nit 10: a stopped shift clock and a carrier put before a mailbox with mail for it,
    /// asked for by a player's Hello (<see cref="PlaytestFlags"/>).
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Far above anything a run's spawner hands out.</summary>
        private const uint PlaytestMailIdBase = 0xF0000000;
        private uint _playtestMail;

        /// <summary>True while a player who asked for <see cref="PlaytestFlags.NoTimer"/> is in the game.</summary>
        public bool ClockStopped
        {
            get
            {
                foreach (var p in _players)
                    if ((p.Playtest & PlaytestFlags.NoTimer) != 0) return true;
                return false;
            }
        }

        /// <summary>What a joining player asked for. Unknown bits are ignored.</summary>
        public void SetPlaytest(Player player, PlaytestFlags flags)
        {
            player.Playtest = flags & PlaytestMask.All;
            if ((player.Playtest & PlaytestFlags.AtMailbox) != 0) ParkAtMailbox(player);
        }

        /// <summary>
        /// A stopped Delivery clock keeps all the time it has: the end of the shift moves away a
        /// tick at a time, and with it a raid that has not been announced yet.
        /// </summary>
        private void HoldShiftClock()
        {
            Run.PhaseEndTick++;
            if (_raidStartTick != 0 && Raid == RaidState.None)
            {
                _raidWarnTick++;
                _raidStartTick++;
            }
        }

        private void ParkPlaytesters()
        {
            foreach (var p in _players)
                if ((p.Playtest & PlaytestFlags.AtMailbox) != 0) ParkAtMailbox(p);
        }

        /// <summary>
        /// Puts the player on the street side of their seat's mailbox, facing it. While mailboxes
        /// take mail the player also gets a stack addressed to it, unless they still carry one.
        /// </summary>
        private void ParkAtMailbox(Player p)
        {
            if (p.Dead) return;
            var dest = PlaytestSpot.Mailbox(Map, p.Slot);
            PlaytestSpot.Stand(Map, dest, _move.Radius, out float x, out float z);
            float yaw = MathF.Atan2(-(dest.MailboxX - x), -(dest.MailboxZ - z));
            p.Move = new PlayerMoveState { X = x, Z = z, Y = Map.HeightAt(x, z), Grounded = true, Yaw = PlayerInput.QuantizeYaw(yaw) };
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
