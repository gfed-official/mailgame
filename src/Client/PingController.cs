using System;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client
{
    /// <summary>
    /// The ping key (doc 09 §4: middle mouse, "hold for wheel"; §2.6; design choice 117). A tap
    /// pings "Look here" where the crosshair meets the ground; held a moment, the ping wheel comes
    /// up over the crosshair and the mouse, which no longer turns the view, picks one of its four
    /// words — up "Deliver here", right "Build here", down "Need materials", left "Danger" — or,
    /// left in the middle, "Look here"; letting go pings it where the crosshair was when the key
    /// went down.
    /// </summary>
    public sealed class PingController
    {
        /// <summary>How long the key is held before the wheel comes up.</summary>
        public const double HoldSeconds = 0.25;
        /// <summary>How far the crosshair reaches along the ground for a ping.</summary>
        public const float Reach = 120f;
        /// <summary>How far (in pixels of mouse motion) the wheel's pointer goes from its middle, and how far it must go to leave "Look here".</summary>
        public const float WheelRadius = 110f, WheelDead = 28f;

        private readonly ClientRuntime _client;
        private bool _down, _hasPoint;
        private double _held;
        private float _x, _z;

        public PingController(ClientRuntime client) => _client = client;

        /// <summary>True while the wheel is up: the mouse moves its pointer, not the view.</summary>
        public bool WheelOpen { get; private set; }
        /// <summary>The wheel's pointer, in pixels from its middle (y down).</summary>
        public float PointerX { get; private set; }
        public float PointerY { get; private set; }
        /// <summary>The request of the last ping sent (0 for none yet).</summary>
        public uint LastSent { get; private set; }

        /// <summary>What the wheel's pointer is on.</summary>
        public PingKind Choice => Pick(PointerX, PointerY);

        public static PingKind Pick(float x, float y)
        {
            if (x * x + y * y < WheelDead * WheelDead) return PingKind.Here;
            if (MathF.Abs(x) > MathF.Abs(y)) return x > 0f ? PingKind.Build : PingKind.Danger;
            return y < 0f ? PingKind.Deliver : PingKind.Materials;
        }

        /// <summary>The mouse moved while the wheel is up.</summary>
        public void Steer(float dx, float dy)
        {
            PointerX += dx;
            PointerY += dy;
            float len = MathF.Sqrt(PointerX * PointerX + PointerY * PointerY);
            if (len > WheelRadius) { PointerX *= WheelRadius / len; PointerY *= WheelRadius / len; }
        }

        /// <summary>Once a frame, with whether the ping key is held.</summary>
        public void Update(double dt, bool held)
        {
            var world = _client.World;
            if (held && !_down)
            {
                _down = true;
                _held = 0;
                PointerX = PointerY = 0f;
                _hasPoint = world != null && world.HasLocal && Aim(world, _client.Input.YawRadians, _client.Input.PitchRadians, out _x, out _z);
            }
            if (held)
            {
                _held += dt;
                if (_held >= HoldSeconds && _hasPoint) WheelOpen = true;
                return;
            }
            if (!_down) return;
            _down = false;
            var kind = WheelOpen ? Choice : PingKind.Here;
            WheelOpen = false;
            if (_hasPoint) LastSent = _client.Ping(_x, _z, kind);
        }

        /// <summary>Where the view from the local player's eyes meets the ground within <see cref="Reach"/>; false looking up.</summary>
        public static bool Aim(ClientWorld world, float yaw, float pitch, out float x, out float z)
        {
            var ray = ViewRay.FromPlayer(world.Local, yaw, pitch);
            x = z = 0f;
            float ground = world.Map.HeightAt(ray.X, ray.Z);
            if (ray.DirY > -1e-3f) return false;
            float t = (ray.Y - ground) / -ray.DirY;
            if (t > Reach) return false;
            x = Math.Clamp(ray.X + ray.DirX * t, 0f, world.Map.WidthM);
            z = Math.Clamp(ray.Z + ray.DirZ * t, 0f, world.Map.DepthM);
            return true;
        }
    }
}
