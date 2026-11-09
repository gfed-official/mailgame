using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Client.Core.Ui.Throwing;

/// <summary>How the aim arc ends, which picks the line pattern and marker (throwing-catching 018, greybox: three of the M-1a classes).</summary>
public enum AimEnd : byte
{
    /// <summary>A mailbox accepts the parcel: solid green, ring marker.</summary>
    Accept = 0,

    /// <summary>A wrong mailbox: dashed red, X marker.</summary>
    Misdeliver = 1,

    /// <summary>Anything else (ground, reject, water, timeout, blocked): dotted white, plain marker.</summary>
    Neutral = 2,
}

/// <summary>
/// The aim preview as the ribbon view reads it. Wire after PR 87: adapt <c>ThrowPreviewState</c> (tc-004/017) to this port; the view
/// never recomputes a class or a contact.
/// </summary>
public interface IAimArcSource
{
    /// <summary>True while the thrower is aiming.</summary>
    bool Aiming { get; }

    /// <summary>The published end class.</summary>
    AimEnd End { get; }

    /// <summary>Number of polyline points (0 when none).</summary>
    int PointCount { get; }

    /// <summary>Polyline point <paramref name="i"/> in mm.</summary>
    /// <param name="i">The index.</param>
    /// <returns>The point.</returns>
    Vec3Mm Point(int i);
}

/// <summary>One in-flight parcel for this frame: the integer segment endpoints and the render alpha (throwing-catching 017/020).</summary>
/// <param name="FlightId">The flight id.</param>
/// <param name="From">Segment start <c>p(k)</c> in mm.</param>
/// <param name="To">Segment end <c>p(k+1)</c> in mm.</param>
/// <param name="AlphaPermille">Interpolation between them, 0 to 1000.</param>
public readonly record struct FlightRender(uint FlightId, Vec3Mm From, Vec3Mm To, int AlphaPermille);

/// <summary>The flight mirror as the flight view reads it. Wire after PR 87: adapt the Client.Core flight mirror to this port.</summary>
public interface IFlightSource
{
    /// <summary>The render tick the flights are placed at (drives the spin).</summary>
    int RenderTick { get; }

    /// <summary>Number of flights visible this frame.</summary>
    int Count { get; }

    /// <summary>Flight <paramref name="i"/>.</summary>
    /// <param name="i">The index.</param>
    /// <returns>The flight.</returns>
    FlightRender Flight(int i);
}

/// <summary>Samples a launch with the Sim's F2 integer ballistic, for adapters and doubles that hold a <see cref="Launch"/> rather than points.</summary>
public static class ArcSampler
{
    /// <summary>Fills <paramref name="points"/> with <c>Ballistic.PositionAt(launch, k)</c> for k = 0..n-1.</summary>
    /// <param name="launch">The launch.</param>
    /// <param name="points">Destination; its length is the sample count (at most <c>Ballistic.MaxAgeTicks + 1</c>).</param>
    public static void Fill(in Launch launch, Span<Vec3Mm> points)
    {
        for (int k = 0; k < points.Length; k++)
        {
            points[k] = Ballistic.PositionAt(in launch, k);
        }
    }
}
