using PerformativeMail.Sim.Content;
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

        public static DeliveryResult Evaluate(in MailItem item, MailKindDef kind, DestinationDef dest,
            int currentShift, int wallet, ContentDb content)
        {
            if (!KindAccepted(kind, dest.Type)) return new DeliveryResult(DeliveryOutcome.WontFitHere);

            if (item.Address == dest.Address)
            {
                int permille = TimelinessPermille(currentShift, item.DeadlineShift, content.MailMix.LateValueRatio);
                bool late = permille < 1000;
                int paid = item.Value * permille / 1000;
                return new DeliveryResult(DeliveryOutcome.Delivered, paid,
                    late ? content.Balance.GetInt("complaintLateDelivery") : 0, late);
            }

            int penalty = (int)(item.Value * (long)System.Math.Round(content.MailMix.MisdeliveryPenaltyRatio * 1000.0) / 1000);
            if (wallet - penalty < content.Balance.GetInt("walletFloor"))
                return new DeliveryResult(DeliveryOutcome.WalletFloor);
            return new DeliveryResult(DeliveryOutcome.Misdelivered, penalty, kind.ComplaintOnMisdelivery);
        }
    }
}
