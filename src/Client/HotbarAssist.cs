using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Client
{
    /// <summary>
    /// Keeps a mail stack selected without the player hunting for number keys: mail picked up
    /// while nothing is held gets selected, and when the held stack is delivered the next one is
    /// (the hands when there is none: an emptied slot never stays selected).
    /// It only reacts to those two changes, so choosing "hands" by key is respected until then.
    /// </summary>
    public sealed class HotbarAssist
    {
        private uint _containerId;
        private int _mailStacks;
        private int _slot;
        private bool _heldMail;

        /// <summary>Call once per frame for a human player, before the interaction update.</summary>
        public void Update(ClientRuntime client)
        {
            var world = client.World;
            if (world == null || !world.HasLocal) return;
            var hotbar = world.Hotbar;

            int mailStacks = 0;
            foreach (var e in hotbar.Entries)
                if (e.Stack.Kind == StackKind.Mail) mailStacks++;
            int slot = client.Input.HeldSlot;
            bool heldMail = IsMail(hotbar, slot);

            bool sameHotbar = hotbar.Id == _containerId;
            bool gained = sameHotbar && mailStacks > _mailStacks;
            bool lostHeld = sameHotbar && slot == _slot && _heldMail && !heldMail;
            if (!heldMail && (gained || lostHeld))
            {
                int next = NextMailSlot(hotbar, slot);
                if (next >= 0)
                {
                    client.Input.HeldSlot = (byte)next;
                    slot = next;
                    heldMail = true;
                }
                else if (lostHeld)
                {
                    // Nothing left to deliver: back to the hands rather than an empty slot.
                    client.Input.HeldSlot = 0;
                    slot = 0;
                }
            }

            _containerId = hotbar.Id;
            _mailStacks = mailStacks;
            _slot = slot;
            _heldMail = heldMail;
        }

        private static bool IsMail(GridContainer hotbar, int slot)
        {
            var entry = slot > 0 && slot < hotbar.Cols ? hotbar.EntryAt(slot, 0) : null;
            return entry != null && entry.Stack.Kind == StackKind.Mail;
        }

        /// <summary>The first slot holding mail to the right of <paramref name="from"/>, wrapping; −1 if there is none.</summary>
        private static int NextMailSlot(GridContainer hotbar, int from)
        {
            for (int i = 1; i <= hotbar.Cols; i++)
            {
                int slot = (from + i) % hotbar.Cols;
                if (IsMail(hotbar, slot)) return slot;
            }
            return -1;
        }
    }
}
