using Godot;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The local player's eyes (doc 00: 3D, first person). Sits at eye height on the local player
    /// view and looks along the client's yaw and pitch: the same <see cref="ViewRay"/> the interact
    /// prompt is aimed with. The body is not drawn; what the hands hold is: the letter on the left,
    /// and on the right the tool of the held slot (an Axe, a Pickaxe, a Shovel) for as long as it
    /// is held, or else the hammer or the mail cannon of the concept video, out while there is a
    /// raid on or one of them was just used. A tool in the hand is what swings, at a node and at
    /// a raider alike.
    /// </summary>
    public partial class FirstPersonCamera : Camera3D
    {
        /// <summary>How long the weapon stays up after the last swing or shot with no raid on.</summary>
        private const float DrawnSeconds = 4f;
        /// <summary>How long the cannon stays in the hand after a shot before the hammer comes back.</summary>
        private const float CannonSeconds = 2.5f;
        private const float SwingSeconds = 0.38f;
        private static readonly Vector3 HammerRest = new Vector3(0.4f, -0.66f, -0.5f);
        private static readonly Vector3 CannonRest = new Vector3(0.3f, -0.4f, -0.42f);

        public Node3D? Target;

        /// <summary>
        /// The debug world's free camera (design choice 119): while set, the camera stands at
        /// <see cref="FreePosition"/> looking along <see cref="FreeYaw"/> and <see cref="FreePitch"/>
        /// instead of from the carrier's eyes, and nothing is in the hands.
        /// </summary>
        public bool FreeLook;
        public Vector3 FreePosition;
        public float FreeYaw, FreePitch;

        /// <summary>Free, at <paramref name="eye"/>, looking at <paramref name="target"/>.</summary>
        public void LookFrom(Vector3 eye, Vector3 target)
        {
            FreeLook = true;
            FreePosition = eye;
            var d = target - eye;
            FreeYaw = Mathf.Atan2(-d.X, -d.Z);
            FreePitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
            Sync();
        }

        /// <summary>True while a weapon is up in the view.</summary>
        public bool WeaponDrawn => _drawn > 0.5f;
        /// <summary>True while the weapon in the view is the cannon.</summary>
        public bool CannonOut => _cannon.Visible && WeaponDrawn;
        /// <summary>The name of the tool drawn in the hand ("Shovel"); empty when none is.</summary>
        /// <summary>True while the letter in the hand shows its cursed label (doc 05 §6).</summary>
        public bool CursedInHand => _curse != null && _curse.Visible;
        private Node3D? _curse;

        public string ToolInHand => _tool != null && _tool.Visible ? _tool.Name : "";

        private ClientRuntime _client = null!;
        private Node3D _held = null!, _hammer = null!, _cannon = null!;
        /// <summary>The tool of the held slot, if it is one; the models are built the first time each is held.</summary>
        private Node3D? _tool;
        private readonly System.Collections.Generic.Dictionary<ushort, Node3D?> _tools = new System.Collections.Generic.Dictionary<ushort, Node3D?>();
        private int _swings, _shots;
        private float _drawn, _drawnLeft, _cannonLeft, _swing = -1f, _recoil;

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Fov = 75f;
            Near = 0.05f;
            Current = true;
            _held = WeaponModels.Letter();
            _held.Name = "HeldMail";
            _held.Position = new Vector3(-0.3f, -0.26f, -0.6f);
            _held.Rotation = new Vector3(-0.5f, -0.25f, 0f);
            _held.Visible = false;
            AddChild(_held);
            _hammer = WeaponModels.Hammer();
            _hammer.Visible = false;
            AddChild(_hammer);
            _cannon = WeaponModels.Cannon();
            _cannon.Visible = false;
            AddChild(_cannon);
            _swings = client.LocalHammerSwings + client.LocalHarvestSwings;
            _shots = client.LocalCannonShots;
            Sync();
        }

        public override void _Process(double delta)
        {
            Sync();
            Animate((float)delta);
        }

        /// <summary>Where the camera belongs for a player whose feet are at <paramref name="feet"/>.</summary>
        public static Vector3 EyePosition(Vector3 feet) => feet + new Vector3(0f, SimConstants.EyeHeight, 0f);

        /// <summary>
        /// Playtest 346eb3e: at the wheel of a long vehicle (the Mail Truck) the eyes are in its cab,
        /// over the bonnet and a little higher, not in the middle of the body where its cargo box
        /// stood in front of them. Playtest f5813b5: the cab's roof then hid the near road, so the eyes
        /// sit in the cab at a driver's height and the cab's shell is not drawn for its driver.
        /// </summary>
        public const float CabEyeForward = 1.15f, CabEyeHeight = 1.6f;

        /// <summary>Where the camera belongs for the local player whose feet are at <paramref name="feet"/>: the eyes, or a truck's cab.</summary>
        public static Vector3 EyePosition(Vector3 feet, ClientWorld? world)
        {
            if (world?.LocalTurret is { } turret) return GunnerEye(world, turret);
            var ride = world?.LocalRide;
            if (ride == null || ride.Def.BodyOffset <= 0f || ride.Def.OnWater) return EyePosition(feet);
            float hx = world!.Local.HeadX, hz = world.Local.HeadZ, len = Mathf.Sqrt(hx * hx + hz * hz);
            if (len < 1e-3f) return EyePosition(feet);
            return feet + new Vector3(hx / len * CabEyeForward, CabEyeHeight, hz / len * CabEyeForward);
        }

        /// <summary>
        /// Playtest f32ea2e: at a turret's controls the eyes are in the gunner's seat, over the gun's
        /// bearing and a little behind it (<see cref="Sim.TurretSight.Seat"/>; design choice 138), so the
        /// head and the barrels sit at the bottom of the view and the crosshair is on what the gun is.
        /// </summary>
        public static Vector3 GunnerEye(ClientWorld world, Sim.World.Construct turret)
        {
            float yaw = PlayerInput.YawToRadians(world.Local.Yaw);
            Sim.TurretSight.Seat(turret.CentreX, turret.CentreZ, -Mathf.Sin(yaw), -Mathf.Cos(yaw), out float x, out float z);
            return new Vector3(x, world.Map.HeightAt(turret.CentreX, turret.CentreZ) + Sim.TurretSight.SeatHeight, z);
        }

        /// <summary>Places the camera for this frame; runs after the player views have moved.</summary>
        public void Sync()
        {
            var world = _client?.World;
            if (world == null) return;
            if (FreeLook)
            {
                Position = FreePosition;
                Rotation = new Vector3(FreePitch, FreeYaw, 0f);
                _held.Visible = false;
                return;
            }
            if (Target != null) Position = EyePosition(Target.Position, world);
            Rotation = new Vector3(_client!.Input.PitchRadians, _client.Input.YawRadians, 0f);

            int slot = _client.Input.HeldSlot;
            var held = world.HasLocal && slot > 0 ? world.Hotbar.EntryAt(slot, 0) : null;
            _held.Visible = held != null && held.Stack.Kind == StackKind.Mail && !world.Local.Dead && world.LocalTurret == null;
            _curse ??= _held.GetNode<Node3D>("Curse");
            _curse.Visible = _held.Visible && held!.Stack.Cursed;
            var item = held != null && held.Stack.Kind == StackKind.Item ? world.Content.Items[held.Stack.DefIndex] : null;
            var tool = item != null && item.IsTool ? ToolModel(item) : null;
            if (tool != _tool)
            {
                if (_tool != null) _tool.Visible = false;
                _tool = tool;
            }
        }

        private Node3D? ToolModel(Sim.Content.ItemDef item)
        {
            if (_tools.TryGetValue(item.Index, out var model)) return model;
            model = WeaponModels.Tool(item.Id);
            if (model != null)
            {
                model.Name = item.Name;
                model.Visible = false;
                AddChild(model);
            }
            _tools[item.Index] = model;
            return model;
        }

        /// <summary>The hands: the weapon comes up, swings or kicks, and goes away again. The camera itself never moves for it.</summary>
        private void Animate(float delta)
        {
            var world = _client?.World;
            if (world == null) return;

            // A swing at a tree is the same swing.
            if (_client!.LocalHammerSwings + _client.LocalHarvestSwings != _swings)
            {
                _swings = _client.LocalHammerSwings + _client.LocalHarvestSwings;
                _swing = 0f;
                _cannonLeft = 0f;
                _drawnLeft = DrawnSeconds;
            }
            if (_client.LocalCannonShots != _shots)
            {
                _shots = _client.LocalCannonShots;
                _recoil = 1f;
                _cannonLeft = CannonSeconds;
                _drawnLeft = DrawnSeconds;
            }
            // Taking aim with an empty hopper still shows the cannon: the HUD says what it wants.
            if ((_client.Input.Buttons & InputButtons.AltAttack) != 0) { _cannonLeft = Mathf.Max(_cannonLeft, 0.6f); _drawnLeft = Mathf.Max(_drawnLeft, 0.6f); }
            _drawnLeft = Mathf.Max(0f, _drawnLeft - delta);
            _cannonLeft = Mathf.Max(0f, _cannonLeft - delta);

            // At a turret's controls the hands are on the gun's.
            bool want = !FreeLook && !world.Local.Dead && world.HasLocal && world.LocalTurret == null && (world.Raid != RaidState.None || _drawnLeft > 0f || _tool != null);
            _drawn = Mathf.MoveToward(_drawn, want ? 1f : 0f, delta * 5f);
            bool cannon = _cannonLeft > 0f;
            // A tool in the hand takes the hammer's place; the cannon still comes up for a shot.
            var swung = _tool ?? _hammer;
            if (_tool != null) _hammer.Visible = false;
            swung.Visible = _drawn > 0.01f && !cannon;
            if (_tool != null && FreeLook) _tool.Visible = false;
            _cannon.Visible = _drawn > 0.01f && cannon;
            var holstered = new Vector3(0f, -0.6f * (1f - Mathf.Ease(_drawn, 0.5f)), 0f);

            // The hammer: back over the shoulder in a blink, down across the view, and up again.
            float pitch = -0.95f, reach = 0f, roll = -0.3f;
            if (_swing >= 0f)
            {
                _swing += delta;
                float t = _swing / SwingSeconds;
                if (t >= 1f) _swing = -1f;
                else if (t < 0.25f) pitch = Mathf.Lerp(-0.95f, -0.2f, Mathf.Ease(t / 0.25f, 0.5f));
                else if (t < 0.55f) { pitch = Mathf.Lerp(-0.2f, -1.9f, Mathf.Ease((t - 0.25f) / 0.3f, 2f)); reach = (t - 0.25f) / 0.3f; }
                else { pitch = Mathf.Lerp(-1.9f, -0.95f, (t - 0.55f) / 0.45f); reach = 1f - (t - 0.55f) / 0.45f; }
                roll = Mathf.Lerp(-0.3f, 0.25f, reach);
            }
            swung.Position = HammerRest + holstered + new Vector3(-0.16f * reach, 0.12f * reach, -0.22f * reach);
            swung.Rotation = new Vector3(pitch, 0.1f, roll);

            _recoil = Mathf.MoveToward(_recoil, 0f, delta * 3.2f);
            float kick = _recoil * _recoil;
            _cannon.Position = CannonRest + holstered + new Vector3(0f, 0.03f * kick, 0.16f * kick);
            _cannon.Rotation = new Vector3(0.05f + 0.3f * kick, 0.1f, 0f);
        }
    }
}
