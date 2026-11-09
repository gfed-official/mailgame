namespace PerformativeMail.Client.Core.Ui.Throwing;

/// <summary>A screen point in whole pixels (Client.Core is float-free; the view converts).</summary>
/// <param name="X">X px.</param>
/// <param name="Y">Y px.</param>
public readonly record struct Px(int X, int Y);

/// <summary>One screen-space quad, corners in winding order, drawn with one <c>DrawPrimitive</c> call.</summary>
/// <param name="A">Corner A.</param>
/// <param name="B">Corner B.</param>
/// <param name="C">Corner C.</param>
/// <param name="D">Corner D.</param>
public readonly record struct Quad(Px A, Px B, Px C, Px D);

/// <summary>Pure screen-space ribbon geometry (throwing-catching 018): solid, dashed and dotted strokes as quads, plus the end markers.</summary>
public static class RibbonGeometry
{
    /// <summary>Core line width in px.</summary>
    public const int LineWidth = 6;

    /// <summary>Marker size in px.</summary>
    public const int MarkerSize = 24;

    /// <summary>
    /// Emits the stroke over a projected polyline. A point behind the near plane (<paramref name="valid"/> false) breaks the line: no
    /// segment touching it is drawn. Dash phase is anchored at the first point in screen arc length.
    /// </summary>
    /// <param name="points">Projected screen points.</param>
    /// <param name="valid">Per point: false when behind the near plane.</param>
    /// <param name="end">The end class, which selects the pattern.</param>
    /// <param name="quads">Destination; extra quads are dropped.</param>
    /// <returns>Number of quads written.</returns>
    public static int BuildStroke(ReadOnlySpan<Px> points, ReadOnlySpan<bool> valid, AimEnd end, Span<Quad> quads)
    {
        (int on, int off) = end switch
        {
            AimEnd.Accept => (int.MaxValue / 4, 0),
            AimEnd.Misdeliver => (18, 10),
            _ => (6, 8),
        };
        int pitch = on + off;
        long run = 0;
        int n = 0;
        for (int i = 0; i + 1 < points.Length && n < quads.Length; i++)
        {
            if (!valid[i] || !valid[i + 1])
            {
                continue;
            }

            Px a = points[i];
            long dx = points[i + 1].X - a.X;
            long dy = points[i + 1].Y - a.Y;
            int len = (int)ISqrt((dx * dx) + (dy * dy));
            if (len == 0)
            {
                continue;
            }

            int t = 0;
            while (t < len && n < quads.Length)
            {
                int phase = (int)((run + t) % pitch);
                if (phase < on)
                {
                    int t1 = Math.Min(len, t + (on - phase));
                    quads[n++] = Bar(At(a, dx, dy, t, len), At(a, dx, dy, t1, len), dx, dy, len, LineWidth / 2);
                    t = t1;
                }
                else
                {
                    t += pitch - phase;
                }
            }

            run += len;
        }

        return n;
    }

    /// <summary>Emits the end marker: Accept a hollow square, Misdeliver an X, Neutral a filled square.</summary>
    /// <param name="end">The end class.</param>
    /// <param name="c">Screen centre.</param>
    /// <param name="quads">Destination, at least 4 long.</param>
    /// <returns>Number of quads written.</returns>
    public static int BuildMarker(AimEnd end, Px c, Span<Quad> quads)
    {
        int h = MarkerSize / 2;
        const int W = 2;
        switch (end)
        {
            case AimEnd.Accept:
                quads[0] = Bar(new Px(c.X - h, c.Y - h), new Px(c.X + h, c.Y - h), 1, 0, 1, W);
                quads[1] = Bar(new Px(c.X - h, c.Y + h), new Px(c.X + h, c.Y + h), 1, 0, 1, W);
                quads[2] = Bar(new Px(c.X - h, c.Y - h), new Px(c.X - h, c.Y + h), 0, 1, 1, W);
                quads[3] = Bar(new Px(c.X + h, c.Y - h), new Px(c.X + h, c.Y + h), 0, 1, 1, W);
                return 4;
            case AimEnd.Misdeliver:
                quads[0] = Bar(new Px(c.X - h, c.Y - h), new Px(c.X + h, c.Y + h), 1, 1, 1, W);
                quads[1] = Bar(new Px(c.X - h, c.Y + h), new Px(c.X + h, c.Y - h), 1, -1, 1, W);
                return 2;
            default:
                quads[0] = new Quad(new Px(c.X - h, c.Y - h), new Px(c.X + h, c.Y - h), new Px(c.X + h, c.Y + h), new Px(c.X - h, c.Y + h));
                return 1;
        }
    }

    private static Px At(Px a, long dx, long dy, int t, int len) => new(a.X + (int)(dx * t / len), a.Y + (int)(dy * t / len));

    // The half-width offset is the segment normal scaled by halfWidth / len (len is the direction's length, or 1 for unit axes).
    private static Quad Bar(Px from, Px to, long dx, long dy, int len, int halfWidth)
    {
        if (dx != 0 && dy != 0 && len == 1)
        {
            len = (int)ISqrt((dx * dx) + (dy * dy));
        }

        int nx = (int)(-dy * halfWidth / len);
        int ny = (int)(dx * halfWidth / len);
        return new Quad(new Px(from.X + nx, from.Y + ny), new Px(to.X + nx, to.Y + ny), new Px(to.X - nx, to.Y - ny), new Px(from.X - nx, from.Y - ny));
    }

    private static long ISqrt(long v)
    {
        if (v <= 0)
        {
            return 0;
        }

        long x = v;
        long y = (x + 1) / 2;
        while (y < x)
        {
            x = y;
            y = (x + (v / x)) / 2;
        }

        return x;
    }
}
