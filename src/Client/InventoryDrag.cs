using System;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Client
{
    /// <summary>
    /// The stack on the cursor while it is dragged across the inventory screen (doc 09 §2.5).
    /// Nothing leaves its cell until the drop: the drop is one Move request, shown at once by the
    /// <see cref="InventoryPrediction"/>. Aiming runs the server's own rules on scratch copies, so
    /// a drop the server would refuse is shown as such and never sent.
    /// </summary>
    public sealed class InventoryDrag
    {
        private readonly ClientRuntime _client;
        private bool _half;

        public bool Active { get; private set; }
        public uint FromContainer { get; private set; }
        public uint EntryId { get; private set; }
        public bool Rotated { get; private set; }
        /// <summary>Footprint of the carried stack in cells, as it is turned now.</summary>
        public int W { get; private set; }
        public int H { get; private set; }
        /// <summary>The cell of the footprint the cursor holds it by.</summary>
        public int GrabX { get; private set; }
        public int GrabY { get; private set; }

        public InventoryDrag(ClientRuntime client)
        {
            _client = client;
        }

        /// <summary>The dragged entry where it still lies; null once it is gone (taken, delivered, container closed).</summary>
        public GridEntry? Entry => Active ? _client.World?.Prediction.View(FromContainer)?.Get(EntryId) : null;

        /// <summary>Units on the cursor: the whole stack, or the bigger half of it for a split.</summary>
        public int Count
        {
            get
            {
                int have = Entry?.Stack.Count ?? 0;
                return _half && have > 1 ? (have + 1) / 2 : have;
            }
        }

        /// <summary>Picks up the entry by the cell under the cursor; <paramref name="half"/> carries half of the stack.</summary>
        public bool Begin(uint containerId, uint entryId, int cellX, int cellY, bool half)
        {
            Active = false;
            var entry = _client.World?.Prediction.View(containerId)?.Get(entryId);
            // A stack the server has not named yet cannot be named in a request either.
            if (entry == null || InventoryPrediction.IsPrivate(entry)) return false;
            Active = true;
            FromContainer = containerId;
            EntryId = entryId;
            Rotated = entry.Rotated;
            W = entry.W;
            H = entry.H;
            GrabX = Math.Clamp(cellX - entry.X, 0, W - 1);
            GrabY = Math.Clamp(cellY - entry.Y, 0, H - 1);
            _half = half && entry.Stack.Count > 1;
            return true;
        }

        /// <summary>Quarter turn around the cell the cursor holds.</summary>
        public void Rotate()
        {
            if (!Active || W == H) return;
            Rotated = !Rotated;
            (W, H) = (H, W);
            (GrabX, GrabY) = (GrabY, GrabX);
        }

        public void Cancel() => Active = false;

        /// <summary>
        /// Where the stack would land with the cursor over a cell, and what the server would say.
        /// Over another stack of the same kind the drop is a merge into that stack.
        /// </summary>
        public OpResult Aim(uint toContainer, int cellX, int cellY, out int x, out int y)
        {
            x = cellX - GrabX;
            y = cellY - GrabY;
            var world = _client.World;
            var entry = Entry;
            if (world == null || entry == null) return OpResult.UnknownEntry;
            var source = world.Prediction.View(FromContainer)!;
            var target = toContainer == FromContainer ? source : world.Prediction.View(toContainer);
            if (target == null) return OpResult.UnknownContainer;

            var under = target.EntryAt(cellX, cellY);
            if (under != null && under != entry && under.Stack.CanStackWith(entry.Stack))
            {
                x = cellX;
                y = cellY;
            }
            var from = source.Clone();
            var to = ReferenceEquals(source, target) ? from : target.Clone();
            return InventoryOps.Move(world.Content, from, EntryId, to, x, y, Rotated, _half ? Count : 0, new EntryIdAllocator(InventoryPrediction.PrivateIdBase));
        }

        /// <summary>
        /// Lets go over a cell. Returns the id of the Move request, or 0 when nothing was sent: the
        /// drop would be refused, or the stack was put back exactly where it lay.
        /// </summary>
        public uint Drop(uint toContainer, int cellX, int cellY)
        {
            if (!Active) return 0;
            var result = Aim(toContainer, cellX, cellY, out int x, out int y);
            var entry = Entry;
            Active = false;
            if (result != OpResult.Ok || entry == null) return 0;
            if (!_half && toContainer == FromContainer && x == entry.X && y == entry.Y && Rotated == entry.Rotated) return 0;
            int count = _half ? (entry.Stack.Count + 1) / 2 : 0;
            return _client.Move(FromContainer, EntryId, toContainer, x, y, Rotated, count);
        }
    }
}
