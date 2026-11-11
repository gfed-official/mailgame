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
        private uint _decayTimer;

        public int Wallet { get; private set; }
        /// <summary>Delivery income this shift with penalties already subtracted (doc 01 §2.4).</summary>
        public int ShiftEarnings { get; private set; }
        public int Complaint { get; private set; }
        public int ShiftDeliveries { get; private set; }
        public int ShiftMisdeliveries { get; private set; }
        public int TotalDeliveries { get; private set; }
        public int TotalMisdeliveries { get; private set; }

        public Economy(Balance balance)
        {
            _complaintMax = balance.GetInt("complaintMax");
            double perSecond = balance.GetDouble("complaintDecayPerSecond");
            _decayIntervalTicks = perSecond > 0 ? SimConstants.SecondsToTicks(1.0 / perSecond) : 0;
        }

        public void Apply(in DeliveryResult result)
        {
            switch (result.Outcome)
            {
                case DeliveryOutcome.Delivered:
                    Wallet += result.Amount;
                    ShiftEarnings += result.Amount;
                    ShiftDeliveries++;
                    TotalDeliveries++;
                    break;
                case DeliveryOutcome.Misdelivered:
                    Wallet -= result.Amount;
                    ShiftEarnings -= result.Amount;
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
        }
    }
}
