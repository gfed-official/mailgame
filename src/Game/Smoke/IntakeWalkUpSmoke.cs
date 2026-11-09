using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// A no-flag boot's walk to the Intake (WP-I; playtest of b1a3e8d: "~2 min near the orange Intake, no [E] Take mail"), over the default
/// profile and the production input adapter, in Prep: the drawn Intake console (<c>Terrain/Pad/Intake</c>) is the only orange pad marker,
/// and walking from the spawn to it and on into the Post Office wall, as a player does, keeps the Intake focused with the [E] prompt.
/// The orange marker the playtest walked to was the door marker, 2 m west of the Intake's anchor, where nothing is in focus.
/// </summary>
[SmokeScenario("intake-walkup")]
public sealed class IntakeWalkUpSmoke : SmokeScenario
{
    private const int StandOffMm = 1500;

    private readonly FakeClock _clock = new();

    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override double TimeoutSeconds => 60;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop) => loop.UseBootOptionsForSmoke(new BootOptions { Clock = _clock });

    /// <inheritdoc />
    public override void Start(SmokeContext ctx) =>
        Expect(ctx, "client loop composed at boot step 5", ctx.Loop.Session?.Loop is not null, true);

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (loop.InputBoot?.Adapter is not { } adapter || !adapter.IsInsideTree())
        {
            return SmokeStatus.Running;
        }

        if (loop.Session is not { LocalPlayer: { } local, Mirrors: { } mirrors } session || loop.InputBoot is not { } input)
        {
            ctx.Fail("no booted session");
            return SmokeStatus.Done;
        }

        var drive = new SmokeDriver(_clock, loop, session.Carrier.View);
        loop._Process(0);
        drive.TickUntil(20, () => local.IsKnown);
        Expect(ctx, "in Prep (no playtest flags)", mirrors.Run.Phase, ShiftPhase.Prep);

        Node? pad = loop.FindChild("Pad", true, false);
        if (!Expect(ctx, "the Intake console is drawn", pad?.GetNodeOrNull<MeshInstance3D>("Intake") is not null, true))
        {
            return SmokeStatus.Done;
        }

        MeshInstance3D console = pad!.GetNode<MeshInstance3D>("Intake");
        Color orange = ((StandardMaterial3D)console.MaterialOverride).AlbedoColor;
        int alike = pad.GetChildren().OfType<MeshInstance3D>().Count(m => m.MaterialOverride is StandardMaterial3D { AlbedoColor: var c } && c == orange);
        Expect(ctx, "the Intake console is the only orange pad marker", alike, 1);

        // Walk to the console as a player would: in front of it, then straight on into the Post Office wall.
        int x = (int)Math.Round(console.Position.X * 1000f);
        int z = (int)Math.Round(console.Position.Z * 1000f);
        drive.MoveTo(x, z + StandOffMm);
        IInteractionQuery focus = input.Ports.Interaction;
        Expect(ctx, $"in front of the console the Intake is focused with [E] Take mail ('{Prompt(loop)}')", Focused(drive, focus) && Prompt(loop) == "[E] Take mail", true);
        drive.Hold(Key.W, true);
        drive.TickN(45);
        drive.Hold(Key.W, false);
        drive.TickN(6);
        Expect(ctx, $"walked into the wall at the console (feet {drive.Carrier.Current.X},{drive.Carrier.Current.Z}) the prompt stays ('{Prompt(loop)}')", Focused(drive, focus) && Prompt(loop) == "[E] Take mail", true);
        return SmokeStatus.Done;
    }

    private static bool Focused(SmokeDriver drive, IInteractionQuery q) =>
        drive.TickUntil(30, () => !q.Current.Focused.IsNone && q.Current.Verb == Verb.Open && q.Current.Available != 0);

    private static string Prompt(GameLoop loop) =>
        loop.GetNodeOrNull<Control>("Hud/Persistent/DeliverPromptView") is { Visible: true } view
            ? string.Join(" ", view.FindChildren("*", nameof(Label), true, false).OfType<Label>().Where(l => l.Visible && l.Text.Length > 0).Select(l => l.Text))
            : string.Empty;
}
