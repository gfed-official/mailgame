using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim.Tests.Unit.Ui;

/// <summary>HUD read model, F3 timer, money formatter and prompt rows (hud-notifications stories 001 to 003, Godot-free halves).</summary>
public class HudUiTests
{
    private static readonly StringId _mailbox = new(StringNamespace.Run, 1);
    private static readonly StringId _piece = new(StringNamespace.Run, 2);
    private static readonly FrameworkReasons _reasons = new(new StringId(StringNamespace.Reason, 1), new StringId(StringNamespace.Reason, 2));

    private sealed class Wallet : IHudWallet
    {
        public long Cents { get; set; }

        public Cents Balance
        {
            get
            {
                var sink = default(SimCounterSink);
                return Primitives.Cents.FromCents(Cents, ref sink);
            }
        }
    }

    private sealed class Run : IHudRun
    {
        public byte Phase { get; set; } = (byte)ShiftPhase.Delivery;

        public uint DeadlineTick { get; set; }
    }

    private sealed class Query : IInteractionQuery
    {
        private InteractionSnapshot _s;

        public ref readonly InteractionSnapshot Current => ref _s;

        public void Set(InteractionSnapshot s) => _s = s;
    }

    private sealed class Clock : IRenderClock
    {
        public uint HostTick => 0;

        public int AlphaPermille => 0;

        public long RenderSimTimeMs { get; set; }

        public bool IsValid { get; set; } = true;
    }

    private sealed class Text : IHudText
    {
        public bool TryGetDisplayText(StringId id, out string text)
        {
            text = id == _mailbox ? "17 Elm St" : id == _piece ? "14 Larch Lane" : string.Empty;
            return text.Length > 0;
        }
    }

    private static InteractionSnapshot Deliver(DeliverPromptArgs a, ushort seq = 1, InteractState state = InteractState.Idle)
    {
        var s = new InteractionSnapshot { Seq = seq, Verb = Verb.Deliver, State = state, Focused = new AnchorRef(new EntityId(0x0200_0001u), 1) };
        PromptArgs args = default;
        DeliverPromptArgs.Write(in a, ref args);
        s.PromptArgs = args;
        return s;
    }

    private static PromptRows Rows(DeliverPromptArgs a, InteractState state = InteractState.Idle) =>
        DeliverPromptRows.Build(DeliverPrompt.From(Deliver(a, 1, state)), new EnMoneyFormatter(), new Text(), "[E]", _reasons);

    [Fact]
    public void Refresh_sets_dirty_flags_per_changed_source()
    {
        var wallet = new Wallet();
        var run = new Run { DeadlineTick = 4020 };
        var query = new Query();
        var clock = new Clock { RenderSimTimeMs = 120_300 };
        var hud = new HudState(wallet, run, query, clock);

        hud.Refresh();
        Assert.Equal(HudDirty.Timer, hud.Dirty);
        Assert.Equal(14, hud.Timer.DisplaySeconds);

        hud.Refresh();
        Assert.Equal(HudDirty.None, hud.Dirty);

        wallet.Cents = 9;
        hud.Refresh();
        Assert.Equal(HudDirty.Wallet, hud.Dirty);
        Assert.Equal(9, hud.WalletCents.Value);

        query.Set(Deliver(new DeliverPromptArgs(DeliverPromptBranch.Nothing, DeliverPromptFlags.None, DeliverPromptKeys.Nothing, _mailbox, 0, 0, 0)));
        hud.Refresh();
        Assert.Equal(HudDirty.Prompt, hud.Dirty);
        Assert.True(hud.Prompt.Shown);
        Assert.Equal(DeliverPromptBranch.Nothing, hud.Prompt.Branch);
    }

    [Fact]
    public void Timer_rounds_up_never_rises_within_a_key_and_hides_without_deadline()
    {
        ShiftTimer t = ShiftTimerF3.Step(default, 1, 4020, 120_300);
        Assert.Equal(14, t.DisplaySeconds);
        Assert.Equal("02:14", HudStringsEn.TimerText(134));
        Assert.Equal(1, ShiftTimerF3.Step(default, 1, 30, 999).DisplaySeconds);
        ShiftTimer end = ShiftTimerF3.Step(default, 1, 30, 1000);
        Assert.Equal("00:00", HudStringsEn.TimerText(end.DisplaySeconds));
        Assert.True(end.Visible);

        Assert.Equal(14, ShiftTimerF3.Step(t, 1, 4020, 100_000).DisplaySeconds);
        Assert.False(ShiftTimerF3.Step(t, 1, 0, 120_300).Visible);
    }

    [Fact]
    public void Timer_text_changes_once_per_second_over_ten_seconds()
    {
        ShiftTimer t = default;
        int changes = 0;
        for (long ms = 0; ms <= 10_000; ms++)
        {
            ShiftTimer next = ShiftTimerF3.Step(t, 1, 600, ms);
            if (next.DisplaySeconds != t.DisplaySeconds)
            {
                changes++;
            }

            t = next;
        }

        Assert.Equal(11, changes); // 0 -> 20 on the first frame, then 19 down to 10
    }

    [Theory]
    [InlineData(86, MoneySign.Delta, "+86¢")]
    [InlineData(-4, MoneySign.Delta, "−4¢")]
    [InlineData(0, MoneySign.Delta, "0¢")]
    [InlineData(-500, MoneySign.Balance, "−500¢")]
    [InlineData(9, MoneySign.Balance, "9¢")]
    public void Money_formats(long cents, MoneySign sign, string expected) => Assert.Equal(expected, new EnMoneyFormatter().Format(cents, sign));

    [Fact]
    public void Match_rows_show_tick_pay_and_dead_only_has_no_plus()
    {
        PromptRows r = Rows(new DeliverPromptArgs(DeliverPromptBranch.Match, DeliverPromptFlags.None, DeliverPromptKeys.MatchPieces, _mailbox, 12, 86, 0));
        Assert.Equal(PromptMark.Match, r.Mark);
        Assert.Equal("✓ Hold [E] Deliver · 17 Elm St", r.Line1);
        Assert.Equal("12 pieces · +86¢", r.Line2);

        PromptRows dead = Rows(new DeliverPromptArgs(DeliverPromptBranch.Match, DeliverPromptFlags.None, DeliverPromptKeys.MatchPieces, _mailbox, 2, 0, 0));
        Assert.Equal("Clears 2 dead letters · 0¢", dead.Line2);
    }

    [Fact]
    public void Mismatch_rows_show_cross_penalty_and_floor_refusal_keeps_the_verb()
    {
        var a = new DeliverPromptArgs(DeliverPromptBranch.MisdeliverOne, DeliverPromptFlags.None, DeliverPromptKeys.MismatchPieceFor, _piece, 1, 0, 4);
        PromptRows r = Rows(a);
        Assert.Equal(PromptMark.Mismatch, r.Mark);
        Assert.StartsWith("✗ Hold [E] Deliver", r.Line1);
        Assert.Equal("Wrong house — piece is for 14 Larch Lane", r.Line2);
        Assert.Equal("Posts 1 · −4¢", r.Line3);

        PromptRows floor = Rows(a with { Flags = DeliverPromptFlags.FloorRefusal });
        Assert.Equal("Can't afford a wrong house (−4¢)", floor.Line3);
        Assert.Equal(PromptMark.Mismatch, floor.Mark);
    }

    [Fact]
    public void Nothing_reject_and_unavailable_rows_have_no_mark_and_non_deliver_verb_decodes_reason()
    {
        PromptRows nothing = Rows(new DeliverPromptArgs(DeliverPromptBranch.Nothing, DeliverPromptFlags.None, DeliverPromptKeys.Nothing, _mailbox, 0, 0, 0));
        Assert.Equal((PromptMark.None, true, "Nothing for 17 Elm St"), (nothing.Mark, nothing.Dim, nothing.Line1));
        Assert.Equal("Closed — opens at Delivery", Rows(new DeliverPromptArgs(DeliverPromptBranch.Reject, DeliverPromptFlags.None, DeliverPromptKeys.MailRejectClosed, _mailbox, 0, 0, 0)).Line1);
        Assert.Equal("Close panel to deliver", Rows(new DeliverPromptArgs(DeliverPromptBranch.Unavailable, DeliverPromptFlags.None, DeliverPromptKeys.ClosePanel, _mailbox, 0, 0, 0)).Line1);

        var pick = new InteractionSnapshot { Seq = 3, Verb = Verb.PickUp, Available = 0, Reason = _reasons.DryLand, Focused = new AnchorRef(new EntityId(0x0200_0001u), 1) };
        DeliverPrompt p = DeliverPrompt.From(pick);
        Assert.Equal(DeliverPromptBranch.None, p.Branch);
        Assert.Equal(_reasons.DryLand, p.Key);
        Assert.Equal("Dry land required to deliver", DeliverPromptRows.Build(p, new EnMoneyFormatter(), new Text(), "[E]", _reasons).Line1);
        Assert.False(DeliverPrompt.From(new InteractionSnapshot { Seq = 4 }).Shown);
    }

    [Fact]
    public void String_table_has_every_listed_key()
    {
        string[] keys =
        [
            DeliverPromptKeys.VerbHoldKey, DeliverPromptKeys.MatchPiecesKey, DeliverPromptKeys.MatchStacksKey, DeliverPromptKeys.MatchPayKey,
            DeliverPromptKeys.MatchDeadOnlyKey, DeliverPromptKeys.MismatchPieceForKey, DeliverPromptKeys.MismatchPostOneKey,
            DeliverPromptKeys.FloorRefusalKey, DeliverPromptKeys.NothingKey, DeliverPromptKeys.ClosePanelKey, DeliverPromptKeys.DryLandKey,
            DeliverPromptKeys.PendingKey, DeliverPromptKeys.MailRejectClosedKey, DeliverPromptKeys.MailRejectNotOnRouteKey,
        ];
        foreach (string key in keys)
        {
            DeliverPromptKey k = DeliverPromptKeys.All.Single(x => x.Spelling == key);
            Assert.NotNull(HudStringsEn.Format(k.Id));
        }
    }
}
