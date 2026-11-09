using Godot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Players.Client;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Ticks a booted loop on a <see cref="FakeClock"/> and plays keys and mouse through <see cref="SmokeInput"/> (the production adapter),
/// for the scenarios that walk the local carrier around the street (WP-G). Steering assumes look yaw 0 (W = −Z, D = +X); <see cref="Turn"/>
/// sets other yaws. Main thread only.
/// </summary>
/// <example><code>var drive = new SmokeDriver(clock, loop, session.Carrier.View); drive.MoveTo(x, z); drive.Hold(Key.W, true);</code></example>
internal sealed class SmokeDriver(FakeClock clock, GameLoop loop, CarrierView carrier)
{
    /// <summary>How close <see cref="MoveTo"/> gets, mm.</summary>
    public const int ToleranceMm = 150;

    private readonly HashSet<Key> _down = [];

    /// <summary>The owned carrier's view.</summary>
    public CarrierView Carrier => carrier;

    /// <summary>One frame of one tick.</summary>
    public void Tick()
    {
        clock.Advance(FakeClock.TickCounts);
        loop._Process(0);
    }

    /// <summary><paramref name="n"/> ticks.</summary>
    /// <param name="n">Ticks.</param>
    public void TickN(int n)
    {
        for (int i = 0; i < n; i++)
        {
            Tick();
        }
    }

    /// <summary>Ticks until <paramref name="done"/> or <paramref name="ticks"/> have run.</summary>
    /// <param name="ticks">The most ticks.</param>
    /// <param name="done">The condition.</param>
    /// <returns>The condition's last value.</returns>
    public bool TickUntil(int ticks, Func<bool> done)
    {
        for (int i = 0; i < ticks; i++)
        {
            if (done())
            {
                return true;
            }

            Tick();
        }

        return done();
    }

    /// <summary>Presses or releases a key once (no repeat).</summary>
    /// <param name="key">The key.</param>
    /// <param name="down">True to press.</param>
    public void Hold(Key key, bool down)
    {
        if (down == _down.Contains(key))
        {
            return;
        }

        SmokeInput.Key(key, down);
        _ = down ? _down.Add(key) : _down.Remove(key);
    }

    /// <summary>Presses a key for <paramref name="ticks"/> ticks, then releases it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="ticks">Ticks held.</param>
    public void Press(Key key, int ticks)
    {
        Hold(key, true);
        TickN(ticks);
        Hold(key, false);
        Tick();
    }

    /// <summary>Clicks a mouse button (two ticks down, two up).</summary>
    /// <param name="button">The button.</param>
    public void Click(MouseButton button)
    {
        SmokeInput.Mouse(button, true);
        TickN(2);
        SmokeInput.Mouse(button, false);
        TickN(2);
    }

    /// <summary>Steers with W/A/S/D (look yaw 0) until the feet are within <see cref="ToleranceMm"/>, then releases them and settles.</summary>
    /// <param name="x">Target X, mm.</param>
    /// <param name="z">Target Z, mm.</param>
    public void MoveTo(int x, int z)
    {
        for (int i = 0; i < 900; i++)
        {
            int dx = x - carrier.Current.X;
            int dz = z - carrier.Current.Z;
            Hold(Key.D, dx > ToleranceMm);
            Hold(Key.A, dx < -ToleranceMm);
            Hold(Key.S, dz > ToleranceMm);
            Hold(Key.W, dz < -ToleranceMm);
            if (Math.Abs(dx) <= ToleranceMm && Math.Abs(dz) <= ToleranceMm)
            {
                break;
            }

            Tick();
        }

        foreach (Key k in new[] { Key.W, Key.A, Key.S, Key.D })
        {
            Hold(k, false);
        }

        TickN(6);
    }

    /// <summary>Turns the look with mouse motion until it is within 1° of <paramref name="yaw"/>.</summary>
    /// <param name="yaw">Target yaw, BAM.</param>
    public void Turn(ushort yaw)
    {
        for (int i = 0; i < 60; i++)
        {
            int diff = unchecked((short)(ushort)(yaw - carrier.LookYaw));
            if (Math.Abs(diff) < 182)
            {
                return;
            }

            SmokeInput.MouseMotion(-Math.Clamp(diff / 20f, -400f, 400f), 0f);
            Tick();
        }
    }
}
