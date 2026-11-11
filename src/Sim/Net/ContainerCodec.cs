using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Net
{
    /// <summary>Wire format of container states and deltas (doc 08 §3.4).</summary>
    public static class ContainerCodec
    {
        public static void WriteState(NetWriter w, ContainerState s)
        {
            w.U32(s.ContainerId);
            w.U16(s.DefIndex);
            w.U32(s.Version);
            w.U32(s.Hash);
            w.U16((ushort)s.Entries.Count);
            foreach (var e in s.Entries) WriteEntry(w, e);
        }

        public static ContainerState ReadState(NetReader r)
        {
            var s = new ContainerState { ContainerId = r.U32(), DefIndex = r.U16(), Version = r.U32(), Hash = r.U32() };
            int n = r.Count16(MinEntryBytes);
            for (int i = 0; i < n; i++) s.Entries.Add(ReadEntry(r));
            return s;
        }

        public static void WriteDelta(NetWriter w, ContainerDelta d)
        {
            w.U32(d.ContainerId);
            w.U32(d.Version);
            w.U32(d.Hash);
            w.U16((ushort)d.Removed.Count);
            foreach (uint id in d.Removed) w.U32(id);
            w.U16((ushort)d.Upserts.Count);
            foreach (var e in d.Upserts) WriteEntry(w, e);
        }

        public static ContainerDelta ReadDelta(NetReader r)
        {
            var d = new ContainerDelta { ContainerId = r.U32(), Version = r.U32(), Hash = r.U32() };
            int removed = r.Count16(4);
            for (int i = 0; i < removed; i++) d.Removed.Add(r.U32());
            int upserts = r.Count16(MinEntryBytes);
            for (int i = 0; i < upserts; i++) d.Upserts.Add(ReadEntry(r));
            return d;
        }

        private const int MinEntryBytes = 16;

        private static void WriteEntry(NetWriter w, GridEntry e)
        {
            w.U32(e.EntryId);
            w.U8((byte)e.X);
            w.U8((byte)e.Y);
            w.U8((byte)e.W);
            w.U8((byte)e.H);
            w.Bool(e.Rotated);
            var s = e.Stack;
            w.U8((byte)s.Kind);
            w.U16(s.DefIndex);
            if (s.Kind == StackKind.Mail)
            {
                w.U32(s.Address.Key);
                w.U8(s.Address.Unit);
                w.U16((ushort)s.Mail.Count);
                foreach (var m in s.Mail)
                {
                    w.U32(m.Id);
                    w.U16(m.Value);
                    w.U8(m.SpawnShift);
                    w.U8(m.DeadlineShift);
                    w.U8(m.Flags);
                }
            }
            else w.I32(s.ItemCount);
        }

        private static GridEntry ReadEntry(NetReader r)
        {
            var e = new GridEntry
            {
                EntryId = r.U32(), X = r.U8(), Y = r.U8(), W = r.U8(), H = r.U8(), Rotated = r.Bool(),
            };
            byte kind = r.U8();
            if (kind > (byte)StackKind.Item) throw new NetFormatException("unknown stack kind");
            var stack = new ItemStack { Kind = (StackKind)kind, DefIndex = r.U16() };
            if (stack.Kind == StackKind.Mail)
            {
                stack.Address = new AddressId(r.U32(), r.U8());
                int n = r.Count16(9);
                stack.Mail = new System.Collections.Generic.List<MailItem>(n);
                for (int i = 0; i < n; i++)
                {
                    stack.Mail.Add(new MailItem
                    {
                        Id = r.U32(), Value = r.U16(), SpawnShift = r.U8(), DeadlineShift = r.U8(), Flags = r.U8(),
                        Kind = stack.DefIndex, Address = stack.Address,
                    });
                }
            }
            else stack.ItemCount = r.I32();
            e.Stack = stack;
            return e;
        }
    }
}
