using System;
using System.Globalization;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    public enum PromptTone : byte { Neutral, Match, Mismatch }

    /// <summary>Every string the HUD shows, derived from replicated state (doc 09 §2.4). Godot-free so it is testable.</summary>
    public static class HudText
    {
        /// <summary>Integer cents as "$18.20"; negative balances as "-$1.50".</summary>
        public static string Money(int cents)
        {
            int abs = Math.Abs(cents);
            return string.Format(CultureInfo.InvariantCulture, "{0}${1}.{2:00}", cents < 0 ? "-" : "", abs / 100, abs % 100);
        }

        /// <summary>mm:ss, rounded up so the clock reads 00:00 only when the phase is over.</summary>
        public static string Clock(double secondsLeft)
        {
            int s = (int)Math.Ceiling(Math.Max(0.0, secondsLeft));
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", s / 60, s % 60);
        }

        public static string PhaseName(RunPhase phase) => phase.ToString().ToUpperInvariant();

        public static string ShiftAndPhase(ClientWorld world)
        {
            int shifts = world.Content.Balance.GetArray("baseQuota").Length;
            return $"Shift {world.Global.Shift} / {shifts}   {PhaseName(world.Global.Phase)} {Clock(world.PhaseSecondsLeft)}";
        }

        public static string Quota(in GlobalState g) => $"Quota {Money(g.ShiftEarnings)} / {Money(g.Quota)}";

        public static string Wallet(in GlobalState g) => $"Wallet {Money(g.Wallet)}";

        /// <summary>"3 Letters" / "1 Small Package".</summary>
        public static string CountAndKind(ContentDb content, ItemStack stack) => CountAndKind(Name(content, stack), stack.Count);

        public static string CountAndKind(string name, int count) => count == 1 ? $"1 {name}" : $"{count} {name}s";

        public static string Name(ContentDb content, ItemStack stack) =>
            stack.Kind == StackKind.Mail ? content.MailKinds[stack.DefIndex].Name : content.Items[stack.DefIndex].Name;

        /// <summary>Full description of a stack for tooltips and lists: "3 Letters → 13 Larch Lane".</summary>
        public static string Stack(ClientWorld world, ItemStack stack) =>
            stack.Kind == StackKind.Mail
                ? $"{CountAndKind(world.Content, stack)} → {world.Map.FormatAddress(stack.Address)}"
                : CountAndKind(world.Content, stack);

        /// <summary>
        /// The interact prompt line. For a delivery it shows the stack's address and the mailbox's
        /// address side by side with a tick or a cross: the game never hides a mismatch.
        /// </summary>
        public static string Prompt(ClientWorld world, in InteractPrompt p, out PromptTone tone)
        {
            tone = PromptTone.Neutral;
            switch (p.Kind)
            {
                case InteractKind.OpenIntake: return "Open Intake  [E]";
                case InteractKind.CloseIntake: return "Close Intake  [E]";
                case InteractKind.Deliver:
                {
                    var dest = p.Destination!;
                    string all = p.MatchingStacks > 0 ? $"   ·   deliver all matching ({p.MatchingStacks})  [F]" : "";
                    if (p.Stack == null) return $"Mailbox: {dest.Label}   ·   select mail (2–8) to deliver{all}";
                    if (!p.KindAccepted)
                    {
                        tone = PromptTone.Mismatch;
                        return $"{Name(world.Content, p.Stack.Stack)} does not fit the mailbox at {dest.Label}  ✗";
                    }
                    tone = p.AddressMatches ? PromptTone.Match : PromptTone.Mismatch;
                    string mark = p.AddressMatches ? "✓" : "✗";
                    return $"Deliver {Stack(world, p.Stack.Stack)}   |   mailbox: {dest.Label}  {mark}  [hold E]{all}";
                }
                default: return "";
            }
        }
    }
}
