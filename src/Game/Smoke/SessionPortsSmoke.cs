using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// godot-host-shell story 005 AC-2 and AC-3. Ports: the production plan with a recording <see cref="IPortContributor"/> appended to
/// step 6; it is called once, before <c>Attach</c>, and the <c>BOOT 6 sim: ports: …</c> line names every port, with the panel as the
/// client's own-panel mirror (<c>OwnPanelMirror</c>, fed by <c>MirrorDrain</c> container records; H-MIRROR's <c>ContainerMirror</c>,
/// TD-140, replaces it) and the carrier load as the constant default. End to end: 40 fake-clock frames of 1/30 s with idle input; the
/// local player is known in slot 0, every carrier sample was accepted by the host world (no refusal on either side, one ingested per
/// tick after the join), and player-character 019's <c>CheckInteraction</c> at the carrier's position does not report
/// <c>PositionStale</c> (without the samples it would: the join-seeded position is 40 ticks old, past the 30-tick rule).
/// </summary>
[SmokeScenario("session-ports")]
public sealed class SessionPortsSmoke : SmokeScenario
{
    private const int Frames = 40;

    private readonly FakeClock _clock = new();
    private readonly RecordingPortContributor _recorder = new();

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop)
    {
        loop.UseBootOptionsForSmoke(new BootOptions { Clock = _clock });
        IBootStep[] steps = [.. loop.BuildBootPlan()];
        steps[BootPlan.SimStep - 1] = new AttachPortsBootStep([.. BootPlan.CreatePortContributors(), _recorder]);
        loop.InstallBootStepsForSmoke(steps);
    }

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (!Expect(ctx, "GameLoop holds the booted session", loop.Session is not null, true) || loop.Session is not { } session)
        {
            return;
        }

        CheckPorts(ctx, loop.BootLines, session);
        CheckSamplesEndToEnd(ctx, loop, session);
    }

    private void CheckPorts(SmokeContext ctx, IReadOnlyList<string> lines, BootSession session)
    {
        Expect(ctx, "recording contributor called once", _recorder.Calls, 1);
        Expect(ctx, "recording contributor ran before Attach", _recorder.AttachedAtCall == false, true);
        Expect(ctx, "ports attached after step 6", session.IsAttached, true);
        int contributed = IndexOf(lines, $"BOOT 6 sim: port contributor {nameof(RecordingPortContributor)}");
        int attached = IndexOf(lines, "BOOT 6 sim: ports: ");
        Expect(ctx, "contributor line precedes the ports line", contributed >= 0 && contributed < attached, true);
        string ports = attached >= 0 ? lines[attached] : string.Empty;
        ctx.Pass($"boot log: {ports}");
        foreach (string part in new[]
        {
            "panel=OwnPanelMirror ", "requests=ClientAdapterRequestSink", "hostClock=", "samples=HostSampleSink",
            "load=ConstantCarrierLoadSource(default)", "localPlayer=HostLocalPlayer",
        })
        {
            Expect(ctx, $"ports line names {part}", ports.Contains(part, StringComparison.Ordinal), true);
        }

        Expect(ctx, "ports line no longer says the panel is undeclared", ports.Contains("not declared", StringComparison.Ordinal), false);
    }

    private void CheckSamplesEndToEnd(SmokeContext ctx, GameLoop loop, BootSession session)
    {
        loop._Process(0);   // starts the driver; no tick
        for (int i = 0; i < Frames; i++)
        {
            _clock.Advance(FakeClock.TickCounts);
            loop._Process(0);
        }

        uint tick = session.CurrentTick;
        Expect(ctx, $"ticks after {Frames} frames", tick, (uint)Frames);
        if (!Expect(ctx, "carrier ports were contributed", session.LocalPlayer is not null && session.Samples is not null, true))
        {
            return;
        }

        Expect(ctx, "ILocalPlayer.IsKnown", session.LocalPlayer!.IsKnown, true);
        Expect(ctx, "ILocalPlayer.Slot", session.LocalPlayer.Slot, PlayerSlot.FromValue(0));
        Expect(ctx, "host sink: samples refused", session.Samples!.Refused, 0L);
        Expect(ctx, "carrier step: samples refused", session.Carrier.SamplesRefused, 0L);
        Expect(ctx, "one sample per tick after the tick-0 join", session.Samples.Written, (long)(tick - 1));
        Expect(ctx, "host ingested every sample", session.WorldCounters[CounterId.SamplesIngested], (long)(tick - 1));
        Expect(ctx, "world refused no sample", session.WorldCounters[CounterId.SamplesRefused], 0L);

        if (!Expect(ctx, "Players host API present", session.Players is not null, true))
        {
            return;
        }

        // A probe anchor at the carrier's chest; the claim is the carrier's own position.
        IPlayersApi players = session.Players!;
        ref readonly CarrierState feet = ref session.Carrier.View.Current;
        int chest = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player).ChestMm;
        var anchor = new AnchorRef(EntityId.Compose(EntityClass.Prop, 0xFF_FF00), 0);
        AnchorDef def = AnchorDef.For(anchor, new Vec3Mm(feet.X, feet.Y + chest, feet.Z), Verb.Open);
        Expect(ctx, "probe anchor registered", players.RegisterAnchor(in def, new ProbeAnchor()), RegisterResult.Ok);
        InteractionCheck check = players.CheckInteraction(ActorId.FromEntity(session.LocalPlayer.Id), anchor, new ClaimedPosition(feet.X, feet.Y, feet.Z));
        Expect(ctx, "CheckInteraction is not PositionStale", check.Kind != InteractionCheckKind.PositionStale, true);
        Expect(ctx, "CheckInteraction at the carrier's own position", check.Kind, InteractionCheckKind.Ok);
        players.UnregisterAnchor(anchor);
    }

    private static int IndexOf(IReadOnlyList<string> lines, string prefix)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Records its calls and whether the ports were already attached when it ran.</summary>
    private sealed class RecordingPortContributor : IPortContributor
    {
        public int Calls { get; private set; }

        public bool? AttachedAtCall { get; private set; }

        public void Contribute(ClientPortsBuilder b, BootContext ctx)
        {
            Calls++;
            AttachedAtCall = ctx.Session?.IsAttached;
        }
    }

    /// <summary>An always-available probe anchor.</summary>
    private sealed class ProbeAnchor : IInteractable
    {
        public bool IsAvailable(in InteractionContext c, out StringId reason)
        {
            reason = StringId.None;
            return true;
        }

        public bool ShouldCancel(in InteractionLatch latch, in InteractionContext c, out StringId reason)
        {
            reason = StringId.None;
            return false;
        }

        public void WritePrompt(in InteractionContext c, ref PromptArgs args)
        {
        }

        public bool TryBuildRequest(in InteractionLatch latch, in InteractionContext c, ref Op op) => false;

        public void OnPress(in InteractionContext c, ref InteractionLatch latch)
        {
        }
    }
}
