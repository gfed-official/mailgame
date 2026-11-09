using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>The focused prompt as the HUD reads it: <see cref="DeliverPromptArgs"/> decoded, plus the snapshot's state. Values only.</summary>
public readonly record struct DeliverPrompt(
    bool Shown, Verb Verb, DeliverPromptBranch Branch, StringId Key, StringId Address, ushort Units, int PredictedPay, int PredictedPenalty,
    DeliverPromptFlags Flags, InteractState State, ushort ProgressPm, ushort Seq)
{
    /// <summary>Decodes a snapshot. Only <see cref="DeliverPromptArgs.TryRead"/> touches the argument bytes.</summary>
    /// <param name="s">The snapshot.</param>
    /// <returns>The prompt.</returns>
    public static DeliverPrompt From(in InteractionSnapshot s)
    {
        if (s.Focused.IsNone)
        {
            return new DeliverPrompt { Seq = s.Seq };
        }

        PromptArgs args = s.PromptArgs;
        if (s.Verb == Verb.Deliver && DeliverPromptArgs.TryRead(in args, out DeliverPromptArgs a))
        {
            return new DeliverPrompt(true, s.Verb, a.Branch, a.Key, a.Address, a.Units, a.PayCents, a.PenaltyCents, a.Flags, s.State, s.ProgressPm, s.Seq);
        }

        return new DeliverPrompt(true, s.Verb, DeliverPromptBranch.None, s.Available == 0 ? s.Reason : StringId.None, StringId.None, 0, 0, 0,
            DeliverPromptFlags.None, s.State, s.ProgressPm, s.Seq);
    }
}
