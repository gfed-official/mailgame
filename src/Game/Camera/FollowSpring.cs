using Godot;

namespace PerformativeMail.Game.Camera;

/// <summary>
/// The camera pivot's follow spring (player-character story 023, GDD P9): a critically damped spring on position only, smooth time
/// <see cref="SmoothTimeS"/> (angular frequency <c>2 / SmoothTimeS</c>), stepped by the closed-form solution so a long frame never
/// overshoots or goes unstable. <c>dt</c> is a difference of <c>IRenderClock.RenderSimTimeMs</c>, never Godot's <c>delta</c>. The first
/// <see cref="Step"/> without a <see cref="Reset"/> seeds at the target. Float, Presentation only. Main thread only.
/// </summary>
/// <example><code>Vector3 feet = spring.Step(renderedFeetMm, (now - last) / 1000f);</code></example>
public sealed class FollowSpring
{
    /// <summary>The smooth time, seconds.</summary>
    public const float SmoothTimeS = 0.08f;

    private const float Omega = 2f / SmoothTimeS;

    private Vector3 _velocity;
    private bool _seeded;

    /// <summary>The followed position (any unit; the rig uses mm).</summary>
    public Vector3 Position { get; private set; }

    /// <summary>Puts the spring at rest on <paramref name="at"/>.</summary>
    /// <param name="at">The position.</param>
    public void Reset(Vector3 at)
    {
        Position = at;
        _velocity = Vector3.Zero;
        _seeded = true;
    }

    /// <summary>Moves toward <paramref name="target"/> for <paramref name="dtS"/>; zero or negative changes nothing.</summary>
    /// <param name="target">The followed target.</param>
    /// <param name="dtS">Render time since the last step, seconds.</param>
    /// <returns>The new position.</returns>
    public Vector3 Step(Vector3 target, float dtS)
    {
        if (!_seeded)
        {
            Reset(target);
            return Position;
        }

        if (dtS <= 0f)
        {
            return Position;
        }

        // x(t) = (x0 + (v0 + ω·x0)·t)·e^(−ωt), v(t) = (v0 − ω·(v0 + ω·x0)·t)·e^(−ωt), with x measured from the target.
        Vector3 offset = Position - target;
        Vector3 c = (_velocity + (offset * Omega)) * dtS;
        float decay = MathF.Exp(-Omega * dtS);
        _velocity = (_velocity - (c * Omega)) * decay;
        Position = target + ((offset + c) * decay);
        return Position;
    }
}
