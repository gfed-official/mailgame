using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Mail
{
    public enum DeliveryOutcome : byte
    {
        /// <summary>Rule 3: consumed, team paid.</summary>
        Delivered,
        /// <summary>Rule 4: kind not accepted by this destination type; nothing happens.</summary>
        WontFitHere,
        /// <summary>Rule 5: consumed, penalty deducted, complaint raised.</summary>
        Misdelivered,
        /// <summary>Rule 5 exception: the penalty would take the wallet below the floor; nothing happens.</summary>
        WalletFloor,
    }

    public readonly struct DeliveryResult
    {
        public readonly DeliveryOutcome Outcome;
        /// <summary>¢ paid (Delivered) or deducted (Misdelivered); 0 otherwise.</summary>
        public readonly int Amount;
        /// <summary>Complaint points added.</summary>
        public readonly int Complaint;
        public readonly bool Late;

        public DeliveryResult(DeliveryOutcome outcome, int amount = 0, int complaint = 0, bool late = false)
        {
            Outcome = outcome; Amount = amount; Complaint = complaint; Late = late;
        }

        public bool Consumed => Outcome == DeliveryOutcome.Delivered || Outcome == DeliveryOutcome.Misdelivered;
    }

    /// <summary>
    /// Destination acceptance rules (doc 03 §2.2). Pure: the same function judges a hand delivery
    /// and, later, automation output.
    /// </summary>
    public static class DeliveryRules
    {
        public static bool KindAccepted(MailKindDef kind, DestinationTypeDef type)
        {
            for (int i = 0; i < kind.AcceptedBy.Count; i++)
                if (kind.AcceptedBy[i] == type.Id) return true;
            return false;
        }

        /// <summary>1.0 on time, lateValueRatio one shift late, 0 after that. Returned in permille.</summary>
        public static int TimelinessPermille(int currentShift, int deadlineShift, double lateValueRatio)
        {
            if (currentShift <= deadlineShift) return 1000;
            if (currentShift == deadlineShift + 1) return (int)System.Math.Round(lateValueRatio * 1000.0);
            return 0;
        }

        /// <summary>What a Priority piece pays once its time is up: its value without the priority's multiplier.</summary>
        public static int PriorityLapsed(int value, ContentDb content)
        {
            float mult = content.Balance.GetFloat("priorityValueMult");
            return mult > 0f ? (int)System.Math.Round(value / (double)mult) : value;
        }

        /// <summary>
        /// The verdict on one piece of mail at one mailbox. <paramref name="team"/> is what the
        /// team's perks do to the numbers (doc 01 §2.5); without it the content's own apply.
        /// </summary>
        public static DeliveryResult Evaluate(in MailItem item, MailKindDef kind, DestinationDef dest,
            int currentShift, int wallet, ContentDb content, StatSheet? team = null, uint tick = 0)
        {
            if (!KindAccepted(kind, dest.Type)) return new DeliveryResult(DeliveryOutcome.WontFitHere);

            if (dest.Takes(item.Address))
            {
                double lateRatio = content.MailMix.LateValueRatio;
                if (team != null) lateRatio = System.Math.Clamp(team.Resolve(Stat.LateValueRatio, (float)lateRatio), 0f, 1f);
                int permille = TimelinessPermille(currentShift, item.DeadlineShift, lateRatio);
                // Late is late whatever it pays: the complaint is for the day, not for the money.
                bool late = currentShift > item.DeadlineShift;
                int value = item.Value;
                // Doc 10 §4.3 Priority Post: past its time it pays what it would have without the priority.
                if (item.IsPriority && tick > item.DueTick) value = PriorityLapsed(item.Value, content);
                int paid = value * permille / 1000;
                if (team != null && kind.MaxStack > 1) paid = Whole(team.Resolve(Stat.PaperMailValue, paid));
                // Doc 10 §6 Postal Inspector: the shift after, every delivery pays less.
                if (team != null) paid = Whole(team.Resolve(Stat.DeliveryEarnings, paid));
                return new DeliveryResult(DeliveryOutcome.Delivered, paid,
                    late ? content.Balance.GetInt("complaintLateDelivery") : 0, late);
            }

            int penalty = (int)(item.Value * (long)System.Math.Round(content.MailMix.MisdeliveryPenaltyRatio * 1000.0) / 1000);
            int complaint = kind.ComplaintOnMisdelivery;
            if (team != null)
            {
                penalty = Whole(team.Resolve(Stat.MisdeliveryPenalty, penalty));
                complaint = Whole(team.Resolve(Stat.MisdeliveryComplaint, complaint));
            }
            if (wallet - penalty < content.Balance.GetInt("walletFloor"))
                return new DeliveryResult(DeliveryOutcome.WalletFloor);
            return new DeliveryResult(DeliveryOutcome.Misdelivered, penalty, complaint);
        }

        /// <summary>A resolved stat as whole ¢ or points: halves round up, nothing goes below 0.</summary>
        private static int Whole(float value) =>
            System.Math.Max(0, (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero));
    }
}
