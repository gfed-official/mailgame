using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Inventory
{
    public enum StackKind : byte { Mail = 0, Item = 1 }

    /// <summary>
    /// The contents of one grid entry: either a stack of mail sharing kind + address (doc 03 §1.2),
    /// or a counted stack of one non-mail item type.
    /// </summary>
    public sealed class ItemStack
    {
        public StackKind Kind;
        /// <summary>MailKindDef.Index for mail, ItemDef.Index for items.</summary>
        public ushort DefIndex;
        /// <summary>Mail only: the shared address of every item in the stack.</summary>
        public AddressId Address;
        /// <summary>Mail only.</summary>
        public List<MailItem> Mail = EmptyMail;
        /// <summary>Items only.</summary>
        public int ItemCount;

        private static readonly List<MailItem> EmptyMail = new List<MailItem>(0);

        public int Count => Kind == StackKind.Mail ? Mail.Count : ItemCount;

        public static ItemStack OfMail(MailItem item) => new ItemStack
        {
            Kind = StackKind.Mail,
            DefIndex = item.Kind,
            Address = item.Address,
            Mail = new List<MailItem> { item },
        };

        public static ItemStack OfItem(ItemDef def, int count) => new ItemStack
        {
            Kind = StackKind.Item,
            DefIndex = def.Index,
            ItemCount = count,
        };

        /// <summary>Mail stacks only with the same kind and address; other stackables merge by item type.</summary>
        public bool CanStackWith(ItemStack other) =>
            Kind == other.Kind && DefIndex == other.DefIndex && (Kind != StackKind.Mail || Address == other.Address);

        public ItemStack Clone() => new ItemStack
        {
            Kind = Kind,
            DefIndex = DefIndex,
            Address = Address,
            Mail = Kind == StackKind.Mail ? new List<MailItem>(Mail) : EmptyMail,
            ItemCount = ItemCount,
        };

        /// <summary>Removes <paramref name="count"/> units from the top of this stack into a new stack.</summary>
        public ItemStack Take(int count)
        {
            var taken = new ItemStack { Kind = Kind, DefIndex = DefIndex, Address = Address };
            if (Kind == StackKind.Mail)
            {
                int start = Mail.Count - count;
                taken.Mail = Mail.GetRange(start, count);
                Mail.RemoveRange(start, count);
            }
            else
            {
                taken.ItemCount = count;
                ItemCount -= count;
            }
            return taken;
        }

        /// <summary>Moves up to <paramref name="count"/> units from <paramref name="source"/> onto this stack.</summary>
        public void Absorb(ItemStack source, int count)
        {
            if (Kind == StackKind.Mail)
            {
                int start = source.Mail.Count - count;
                Mail.AddRange(source.Mail.GetRange(start, count));
                source.Mail.RemoveRange(start, count);
            }
            else
            {
                ItemCount += count;
                source.ItemCount -= count;
            }
        }
    }

    /// <summary>Grid footprint and stacking limits of a stack, resolved from content defs.</summary>
    public readonly struct StackShape
    {
        public readonly int W, H, MaxStack;
        public readonly string Category;
        public readonly WeightClass Weight;
        public readonly bool CarryByHand;

        public StackShape(int w, int h, int maxStack, string category, WeightClass weight, bool carryByHand)
        {
            W = w; H = h; MaxStack = maxStack; Category = category; Weight = weight; CarryByHand = carryByHand;
        }

        public int Area => W * H;

        public static StackShape Of(ContentDb content, ItemStack stack) => Of(content, stack.Kind, stack.DefIndex);

        public static StackShape Of(ContentDb content, StackKind kind, ushort defIndex)
        {
            if (kind == StackKind.Mail)
            {
                var k = content.MailKinds[defIndex];
                return new StackShape(k.GridW, k.GridH, k.MaxStack, "mail", k.WeightClass, k.CarryByHand);
            }
            var d = content.Items[defIndex];
            return new StackShape(d.GridW, d.GridH, d.MaxStack, d.Category, d.WeightClass, true);
        }
    }
}
