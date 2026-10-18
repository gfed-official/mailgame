using System.Runtime.InteropServices;
using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// What an owner registers for one anchor (ADR-0014 section 1): identity, point, verb, mode, hold base, reach, priority and
/// flags. Explicit sequential layout and a <c>byte</c> flag set; no reference. <see cref="RangeMm"/> 0 means "default" and the
/// registry stores the resolved <c>player.rangeMm</c> (2500) at registration.
/// </summary>
/// <example>
/// <code>
/// AnchorDef def = AnchorDef.For(new AnchorRef(destination, 0), new Vec3Mm(4000, 0, 9000), Verb.Deliver);
/// RegisterResult r = players.RegisterAnchor(in def, impl);
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential)]
public struct AnchorDef
{
    /// <summary>The anchor's identity.</summary>
    public AnchorRef Ref;

    /// <summary>The anchor point, mm (clamped into the edge cells of the grid when outside the map; stored verbatim).</summary>
    public Vec3Mm Point;

    /// <summary>The anchor's one verb.</summary>
    public Verb Verb;

    /// <summary>The start mode.</summary>
    public InteractMode Mode;

    /// <summary>The base hold length, ms (0 for Tap and Channel).</summary>
    public int HoldBaseMs;

    /// <summary>The reach, mm; 0 = use the default (<c>player.rangeMm</c>); negative is rejected.</summary>
    public int RangeMm;

    /// <summary>The focus priority, 0 to 3.</summary>
    public byte Priority;

    /// <summary>The anchor flags.</summary>
    public AnchorFlags Flags;

    /// <summary>A definition whose mode, hold base and priority come from the <see cref="VerbCatalogue"/>, reach is the default and flags are none.</summary>
    /// <param name="anchor">The anchor identity.</param>
    /// <param name="point">The anchor point, mm.</param>
    /// <param name="verb">The verb.</param>
    /// <returns>The definition.</returns>
    /// <example><code>AnchorDef def = AnchorDef.For(anchor, point, Verb.Open);   // Tap, priority 1</code></example>
    public static AnchorDef For(AnchorRef anchor, Vec3Mm point, Verb verb) => new()
    {
        Ref = anchor,
        Point = point,
        Verb = verb,
        Mode = VerbCatalogue.DefaultMode(verb),
        HoldBaseMs = VerbCatalogue.DefaultHoldBaseMs(verb),
        RangeMm = 0,
        Priority = VerbCatalogue.DefaultPriority(verb),
        Flags = AnchorFlags.None,
    };
}
