using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// Translates one tick's <see cref="InputFrame"/> into the solver's <see cref="CarrierInput"/> (ADR-0013 section 1, ADR-0016 section 2;
/// player-character story 020). Pure, static and allocation-free; it takes facts, never a port.
/// <list type="bullet">
/// <item><description><c>MoveX</c>/<c>MoveY</c> (Q15 on the unit disc) pass through unchanged; <c>LookYaw</c> is the frame's absolute BAM.</description></item>
/// <item><description><c>SprintHeld</c> and <c>Aiming</c> are the Sprint and Aim held bits (Hold / Toggle / Auto modes are the router's).</description></item>
/// <item><description><c>JumpPressed</c> is true only when the frame carries a Jump press edge. Edges are on the first tick of a catch-up
/// batch only, so later ticks of the batch never jump, and several presses in one frame still jump once.</description></item>
/// <item><description><c>ContextFlags</c> come from <paramref name="status"/> (<see cref="CarrierStatus.ToContextFlags"/>); <c>HostAuthored</c> is false.</description></item>
/// <item><description>F1 stat clamps: <c>BuffAddPm = min(max(addPm, 0), buffCapPm)</c>; <c>ExtraJumps = max(extraJumps, 0)</c>. The
/// <c>longLegsStacks</c> ≤ 3 clamp is applied by the stat sheet that resolved <c>addPm</c>.</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// CarrierInput input = CarrierInputMapper.Map(in ctx.Input, in status, load.SpeedPermille, addPm, extraJumps, settings.BuffCapPm);
/// </code>
/// </example>
public static class CarrierInputMapper
{
    /// <summary>Maps one frame.</summary>
    /// <param name="frame">This tick's latched frame.</param>
    /// <param name="status">This tick's carrier status (Panel from the frame's <see cref="InputFrameFlags.PanelOpen"/>, Dead/Mounted from life/body).</param>
    /// <param name="speedPermille">The load source's speed multiplier (Grid D1).</param>
    /// <param name="moveSpeedAddPm">The stat sheet's <c>MoveSpeedAddPm</c> for the local slot.</param>
    /// <param name="extraJumps">The stat sheet's <c>ExtraJumps</c> for the local slot.</param>
    /// <param name="buffCapPm">The <c>player.buffCapPm</c> cap.</param>
    /// <returns>The solver input for this tick.</returns>
    public static CarrierInput Map(in InputFrame frame, in CarrierStatus status, int speedPermille, int moveSpeedAddPm, int extraJumps, int buffCapPm)
    {
        ulong held = frame.HeldBits;
        return new CarrierInput
        {
            MoveX = frame.MoveX,
            MoveY = frame.MoveY,
            LookYaw = frame.LookYaw,
            SprintHeld = (held & FrameActions.Bit(FrameAction.Sprint)) != 0,
            JumpPressed = HasJumpPress(in frame),
            Aiming = (held & FrameActions.Bit(FrameAction.Aim)) != 0,
            ContextFlags = status.ToContextFlags(),
            HostAuthored = false,
            SpeedPermille = speedPermille,
            BuffAddPm = Math.Min(Math.Max(moveSpeedAddPm, 0), buffCapPm),
            ExtraJumps = Math.Max(extraJumps, 0),
        };
    }

    private static bool HasJumpPress(in InputFrame frame)
    {
        ReadOnlySpan<InputEdge> edges = frame.Edges;
        for (int i = 0; i < edges.Length; i++)
        {
            if (edges[i].Action == FrameAction.Jump && edges[i].Kind == EdgeKind.Press)
            {
                return true;
            }
        }

        return false;
    }
}
