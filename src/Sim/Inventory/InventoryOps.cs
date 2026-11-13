using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Inventory
{
    /// <summary>Outcome of a request. Anything but Ok means nothing changed.</summary>
    public enum OpResult : byte
    {
        Ok = 0,
        UnknownContainer,
        UnknownEntry,
        NotOpen,
        OutOfRange,
        NotAllowed,
        DoesNotFit,
        BadRequest,
        UnknownDestination,
        NotMail,
        WontFitHere,
        WalletFloor,
        TooFast,
        RateLimited,
        /// <summary>The run phase does not allow this (mailboxes are shut outside Delivery, Start only in the Lobby, ...).</summary>
        WrongPhase,
        NotHost,
        /// <summary>Only a solo run can be paused (doc 01 §1).</summary>
        NotSolo,
    }

    public enum SortKey : byte { Address = 0, Size = 1 }

    /// <summary>Source of grid entry ids. The server owns the real one; a predicting client uses a private range.</summary>
    public sealed class EntryIdAllocator
    {
        private uint _next;
        public EntryIdAllocator(uint first = 1) { _next = first; }
        public uint Next() => _next++;
    }

    /// <summary>
    /// The inventory mutations of doc 03 §3.2. Pure functions over containers so the server and a
    /// predicting client run the same code. Callers commit the touched containers afterwards.
    /// </summary>
    public static class InventoryOps
    {
        /// <summary>True if this kind of stack may ever be placed in the container (category and hand-carry rules).</summary>
        public static bool Accepts(GridContainer container, in StackShape shape) =>
            container.Def.Allows(shape.Category) && (!container.Def.HandCarried || shape.CarryByHand);

        /// <summary>
        /// Moves an entry (or <paramref name="count"/> units split off it; 0 = all) to cell (x, y) of
        /// <paramref name="to"/>. Dropping onto a compatible stack merges up to maxStack and leaves the
        /// excess at the source.
        /// </summary>
        public static OpResult Move(ContentDb content, GridContainer from, uint entryId, GridContainer to,
            int x, int y, bool rotated, int count, EntryIdAllocator ids)
        {
            var entry = from.Get(entryId);
            if (entry == null) return OpResult.UnknownEntry;
            int have = entry.Stack.Count;
            if (count < 0 || count > have) return OpResult.BadRequest;
            if (count == 0) count = have;
            var shape = StackShape.Of(content, entry.Stack);
            if (!Accepts(to, shape)) return OpResult.NotAllowed;
            if (!to.InBounds(x, y)) return OpResult.DoesNotFit;

            var target = to.EntryAt(x, y);
            if (target != null && target != entry)
            {
                if (!target.Stack.CanStackWith(entry.Stack)) return OpResult.DoesNotFit;
                int n = Math.Min(count, shape.MaxStack - target.Stack.Count);
                if (n <= 0) return OpResult.DoesNotFit;
                target.Stack.Absorb(entry.Stack, n);
                to.MarkDirty(target.EntryId);
                if (entry.Stack.Count == 0) from.Remove(entryId);
                else from.MarkDirty(entryId);
                return OpResult.Ok;
            }

            if (shape.W == shape.H) rotated = false;
            int w = rotated ? shape.H : shape.W;
            int h = rotated ? shape.W : shape.H;

            if (count < have)
            {
                if (!to.CanPlace(x, y, w, h)) return OpResult.DoesNotFit;
                var split = entry.Stack.Take(count);
                from.MarkDirty(entryId);
                to.Add(new GridEntry { EntryId = ids.Next(), X = x, Y = y, Rotated = rotated, W = w, H = h, Stack = split });
                return OpResult.Ok;
            }

            if (ReferenceEquals(from, to))
            {
                if (!to.CanPlace(x, y, w, h, entryId)) return OpResult.DoesNotFit;
                if (entry.X == x && entry.Y == y && entry.Rotated == rotated) return OpResult.Ok;
                to.Reposition(entry, x, y, rotated, w, h);
                return OpResult.Ok;
            }

            if (!to.CanPlace(x, y, w, h)) return OpResult.DoesNotFit;
            from.Remove(entryId);
            entry.X = x; entry.Y = y; entry.Rotated = rotated; entry.W = w; entry.H = h;
            to.Add(entry);
            return OpResult.Ok;
        }

        /// <summary>
        /// Shift-click: moves as much of the entry as fits into the target containers — stack merges
        /// first (in any target), then the first target with free space, trying it unrotated and
        /// then rotated before the next target (doc 03 §3.2 auto-place).
        /// </summary>
        public static OpResult QuickMove(ContentDb content, GridContainer from, uint entryId,
            IReadOnlyList<GridContainer> targets)
        {
            var entry = from.Get(entryId);
            if (entry == null) return OpResult.UnknownEntry;
            var shape = StackShape.Of(content, entry.Stack);
            bool anyAccepts = false;
            bool moved = false;

            foreach (var target in targets)
            {
                if (ReferenceEquals(target, from) || !Accepts(target, shape)) continue;
                anyAccepts = true;
                if (MergeInto(target, entry.Stack, shape))
                {
                    moved = true;
                    if (entry.Stack.Count == 0) { from.Remove(entryId); return OpResult.Ok; }
                    from.MarkDirty(entryId);
                }
            }
            if (!anyAccepts) return OpResult.NotAllowed;

            // The order of the targets wins over the orientation: a 1×2 package goes into the
            // one-row hotbar on its side before the inventory is tried upright.
            foreach (var target in targets)
            {
                if (ReferenceEquals(target, from) || !Accepts(target, shape)) continue;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool rotated = pass == 1;
                    if (rotated && shape.W == shape.H) break;
                    int w = rotated ? shape.H : shape.W;
                    int h = rotated ? shape.W : shape.H;
                    if (!target.FindFreeSpot(w, h, out int x, out int y)) continue;
                    from.Remove(entryId);
                    entry.X = x; entry.Y = y; entry.Rotated = rotated; entry.W = w; entry.H = h;
                    target.Add(entry);
                    return OpResult.Ok;
                }
            }
            return moved ? OpResult.Ok : OpResult.DoesNotFit;
        }

        /// <summary>
        /// Auto-places a loose stack into a container: merges first, then unrotated, then rotated.
        /// Returns false (and places nothing more) if the remainder has no free spot; partial merges stay.
        /// </summary>
        public static bool AutoPlace(ContentDb content, GridContainer target, ItemStack stack, EntryIdAllocator ids)
        {
            var shape = StackShape.Of(content, stack);
            if (!Accepts(target, shape)) return false;
            MergeInto(target, stack, shape);
            if (stack.Count == 0) return true;
            for (int pass = 0; pass < 2; pass++)
            {
                bool rotated = pass == 1;
                if (rotated && shape.W == shape.H) break;
                int w = rotated ? shape.H : shape.W;
                int h = rotated ? shape.W : shape.H;
                if (!target.FindFreeSpot(w, h, out int x, out int y)) continue;
                target.Add(new GridEntry { EntryId = ids.Next(), X = x, Y = y, Rotated = rotated, W = w, H = h, Stack = stack });
                return true;
            }
            return false;
        }

        /// <summary>True if <see cref="AutoPlace"/> would place the whole of a single new unit of this kind.</summary>
        public static bool HasRoomFor(ContentDb content, GridContainer target, ItemStack stack)
        {
            var shape = StackShape.Of(content, stack);
            if (!Accepts(target, shape)) return false;
            if (shape.MaxStack > 1)
            {
                int room = 0;
                foreach (var e in target.Entries)
                    if (e.Stack.CanStackWith(stack)) room += shape.MaxStack - e.Stack.Count;
                if (room >= stack.Count) return true;
            }
            return target.FindFreeSpot(shape.W, shape.H, out _, out _)
                || (shape.W != shape.H && target.FindFreeSpot(shape.H, shape.W, out _, out _));
        }

        /// <summary>
        /// Repacks the container: compatible stacks are merged, then entries are placed first-fit in
        /// key order (doc 03 §3.2). Returns DoesNotFit and changes nothing if the packing fails.
        /// </summary>
        public static OpResult Sort(ContentDb content, GridContainer container, SortKey key)
        {
            var entries = new List<GridEntry>(container.Entries);
            if (entries.Count == 0) return OpResult.Ok;
            entries.Sort((a, b) => Compare(content, a, b, key));

            var backup = new List<GridEntry>(entries.Count);
            foreach (var e in entries) backup.Add(e.Clone());
            foreach (var e in entries) container.Remove(e.EntryId);

            bool ok = true;
            foreach (var e in entries)
            {
                var shape = StackShape.Of(content, e.Stack);
                MergeInto(container, e.Stack, shape);
                if (e.Stack.Count == 0) continue;
                if (container.FindFreeSpot(shape.W, shape.H, out int x, out int y))
                {
                    e.X = x; e.Y = y; e.Rotated = false; e.W = shape.W; e.H = shape.H;
                }
                else if (shape.W != shape.H && container.FindFreeSpot(shape.H, shape.W, out x, out y))
                {
                    e.X = x; e.Y = y; e.Rotated = true; e.W = shape.H; e.H = shape.W;
                }
                else { ok = false; break; }
                container.Add(e);
            }
            if (ok) return OpResult.Ok;

            foreach (var e in new List<GridEntry>(container.Entries)) container.Remove(e.EntryId);
            foreach (var e in backup) container.Add(e);
            return OpResult.DoesNotFit;
        }

        /// <summary>Weight points of everything in a container (doc 03 §3.2), counted per grid entry.</summary>
        public static int WeightPoints(ContentDb content, GridContainer container, int light, int medium, int heavy)
        {
            int points = 0;
            foreach (var e in container.Entries)
            {
                switch (StackShape.Of(content, e.Stack).Weight)
                {
                    case WeightClass.Light: points += light; break;
                    case WeightClass.Medium: points += medium; break;
                    default: points += heavy; break;
                }
            }
            return points;
        }

        /// <summary>Merges units of <paramref name="stack"/> into compatible entries, scanning row-major.</summary>
        private static bool MergeInto(GridContainer target, ItemStack stack, in StackShape shape)
        {
            if (shape.MaxStack <= 1 || target.EntryCount == 0) return false;
            bool moved = false;
            for (int y = 0; y < target.Rows && stack.Count > 0; y++)
            {
                for (int x = 0; x < target.Cols && stack.Count > 0; x++)
                {
                    var e = target.EntryAt(x, y);
                    if (e == null || e.X != x || e.Y != y || ReferenceEquals(e.Stack, stack)) continue;
                    if (!e.Stack.CanStackWith(stack)) continue;
                    int n = Math.Min(stack.Count, shape.MaxStack - e.Stack.Count);
                    if (n <= 0) continue;
                    e.Stack.Absorb(stack, n);
                    target.MarkDirty(e.EntryId);
                    moved = true;
                }
            }
            return moved;
        }

        private static int Compare(ContentDb content, GridEntry a, GridEntry b, SortKey key)
        {
            int areaA = a.W * a.H, areaB = b.W * b.H;
            int c;
            if (key == SortKey.Size)
            {
                c = areaB.CompareTo(areaA);
                if (c != 0) return c;
            }
            // Mail before items; mail by address, then kind; items by def.
            c = a.Stack.Kind.CompareTo(b.Stack.Kind);
            if (c != 0) return c;
            c = a.Stack.Address.CompareTo(b.Stack.Address);
            if (c != 0) return c;
            c = areaB.CompareTo(areaA);
            if (c != 0) return c;
            c = a.Stack.DefIndex.CompareTo(b.Stack.DefIndex);
            if (c != 0) return c;
            // Fuller stacks first so merging drains the small ones.
            c = b.Stack.Count.CompareTo(a.Stack.Count);
            if (c != 0) return c;
            return a.EntryId.CompareTo(b.EntryId);
        }
    }
}
