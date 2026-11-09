using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Client.Core.Ui.Throwing;

/// <summary>Where one parcel is drawn: world position in mm and the cosmetic spin as parts per 100 000 of a turn.</summary>
/// <param name="XMm">X in mm.</param>
/// <param name="YMm">Y in mm.</param>
/// <param name="ZMm">Z in mm.</param>
/// <param name="SpinTurnsE5">Spin about the up axis, 0 to 99 999.</param>
public readonly record struct FlightPlacement(int XMm, int YMm, int ZMm, int SpinTurnsE5);

/// <summary>Pure placement and slot filling for the one preallocated MultiMesh tier (throwing-catching 017/020, greybox).</summary>
public static class FlightPlacements
{
    /// <summary>The MultiMesh instance cap, preallocated once.</summary>
    public const int Cap = 160;

    /// <summary>The place of a flight: the lerp of its integer segment endpoints, then a spin that is a pure function of id and render tick (2 rev/s).</summary>
    /// <param name="f">The flight.</param>
    /// <param name="renderTick">The render tick.</param>
    /// <returns>The placement.</returns>
    public static FlightPlacement Place(in FlightRender f, int renderTick)
    {
        long a = f.AlphaPermille;
        long x = f.From.X + ((f.To.X - f.From.X) * a / 1000);
        long y = f.From.Y + ((f.To.Y - f.From.Y) * a / 1000);
        long z = f.From.Z + ((f.To.Z - f.From.Z) * a / 1000);
        long ticksE3 = (renderTick * 1000L) + a;
        long turnsE5 = ((f.FlightId * 7919L % 100_000) + (ticksE3 * 2 * 100_000 / 30_000)) % 100_000;
        return new FlightPlacement((int)x, (int)y, (int)z, (int)turnsE5);
    }

    /// <summary>Fills slots 0..n-1 from the source, clamped to <paramref name="slots"/>.Length (the cap); the view sets VisibleInstanceCount to the result.</summary>
    /// <param name="source">The flights.</param>
    /// <param name="slots">The preallocated slots.</param>
    /// <returns>Visible count.</returns>
    public static int Fill(IFlightSource source, Span<FlightPlacement> slots)
    {
        int n = Math.Min(source.Count, slots.Length);
        for (int i = 0; i < n; i++)
        {
            FlightRender f = source.Flight(i);
            slots[i] = Place(in f, source.RenderTick);
        }

        return n;
    }
}
