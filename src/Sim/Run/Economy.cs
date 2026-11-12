using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Mail;

namespace PerformativeMail.Sim.Run
{
    /// <summary>Team wallet, shift earnings and complaint meter (doc 03 §4.1, doc 01 §3.3). Integer ¢ throughout.</summary>
    public sealed class Economy
    {
        private readonly int _complaintMax;
        private readonly uint _decayIntervalTicks;
        private readonly int _bonusPermille;
        private uint _decayTimer;

        public int Wallet { get; private set; }
        /// <summary>Delivery income this shift with penalties already subtracted (doc 01 §2.4).</summary>
        public int ShiftEarnings { get; private set; }
        public int Complaint { get; private set; }
        public int ShiftDeliveries { get; private set; }
        public int ShiftMisdeliveries { get; private set; }
        public int ShiftLateDeliveries { get; private set; }
        /// <summary>Correct deliveries this shift per mail kind index.</summary>
        public int[] ShiftDeliveriesByKind { get; }
        public int TotalDeliveries { get; private set; }
        public int TotalMisdeliveries { get; private set; }
        /// <summary>Shift earnings plus shift bonuses since the run began (the score base, doc 01 §6).</summary>
        public int TotalEarned { get; private set; }

        public Economy(Balance balance, int mailKinds = 0)
        {
            _complaintMax = balance.GetInt("complaintMax");
            double perSecond = balance.GetDouble("complaintDecayPerSecond");
            _decayIntervalTicks = perSecond > 0 ? SimConstants.SecondsToTicks(1.0 / perSecond) : 0;
            _bonusPermille = (int)Math.Round(balance.GetDouble("zeroMisdeliveryBonus") * 1000.0);
            ShiftDeliveriesByKind = new int[mailKinds];
        }

        public void Apply(in DeliveryResult result, int kind = -1)
        {
            switch (result.Outcome)
            {
                case DeliveryOutcome.Delivered:
                    Wallet += result.Amount;
                    ShiftEarnings += result.Amount;
                    TotalEarned += result.Amount;
                    ShiftDeliveries++;
                    TotalDeliveries++;
                    if (result.Late) ShiftLateDeliveries++;
                    if (kind >= 0 && kind < ShiftDeliveriesByKind.Length) ShiftDeliveriesByKind[kind]++;
                    break;
                case DeliveryOutcome.Misdelivered:
                    Wallet -= result.Amount;
                    ShiftEarnings -= result.Amount;
                    TotalEarned -= result.Amount;
                    ShiftMisdeliveries++;
                    TotalMisdeliveries++;
                    break;
                default:
                    return;
            }
            AddComplaint(result.Complaint);
        }

        public void AddComplaint(int points) => Complaint = Math.Clamp(Complaint + points, 0, _complaintMax);

        /// <summary>One Delivery-phase tick: the meter decays 1 point every 1 / complaintDecayPerSecond seconds.</summary>
        public void StepDelivery()
        {
            if (_decayIntervalTicks == 0) return;
            if (++_decayTimer < _decayIntervalTicks) return;
            _decayTimer = 0;
            if (Complaint > 0) Complaint--;
        }

        public void BeginShift()
        {
            ShiftEarnings = 0;
            ShiftDeliveries = 0;
            ShiftMisdeliveries = 0;
            ShiftLateDeliveries = 0;
            Array.Clear(ShiftDeliveriesByKind, 0, ShiftDeliveriesByKind.Length);
        }

        /// <summary>
        /// Shift bonus (doc 03 §4.2): +10 % of the shift's earnings when it had no misdelivery.
        /// Paid into the wallet at Payday; it is not part of the earnings the quota is checked against.
        /// </summary>
        public int PayShiftBonus()
        {
            if (ShiftMisdeliveries > 0 || ShiftEarnings <= 0) return 0;
            int bonus = (int)((long)ShiftEarnings * _bonusPermille / 1000);
            Wallet += bonus;
            TotalEarned += bonus;
            return bonus;
        }

        /// <summary>A new run starts from nothing.</summary>
        public void Reset()
        {
            BeginShift();
            Wallet = 0;
            Complaint = 0;
            TotalDeliveries = 0;
            TotalMisdeliveries = 0;
            TotalEarned = 0;
            _decayTimer = 0;
        }
    }
}
