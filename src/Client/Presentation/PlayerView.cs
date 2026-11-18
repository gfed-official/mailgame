using Godot;
using PerformativeMail.Sim;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Another carrier: a <see cref="CharacterModel"/> in the player's colour with a name tag, a
    /// letter in the hand when mail is held, and the hammer or cannon out for a moment after it was
    /// used. The local player's view has no meshes: the game is first person, and the node only
    /// marks where the feet are for the camera to sit on.
    /// </summary>
    public partial class PlayerView : Node3D
    {
        /// <summary>How long a weapon stays in the hand after its last use.</summary>
        private const float WeaponSeconds = 2.5f;

        /// <summary>True for the local player, whose body is not drawn.</summary>
        public bool Bodiless { get; private set; }

        private CharacterModel? _model;
        private Node3D? _held, _hammer, _cannon;
        private Vector3 _last;
        private bool _placed;
        private float _speed, _weaponLeft;
        private uint _attackTick, _hurtTick;

        /// <summary>Whom this view shows.</summary>
        public ClientPlayer? Carrier;

        public void Setup(string playerName, int slot, bool local)
        {
            Bodiless = local;
            if (local) return;

            _model = Characters.Carrier(Palette.PlayerColour(slot));
            AddChild(_model);
            // Yaw 0 faces −Z (the sim's convention and Godot's): the letter is held out in front.
            _held = WeaponModels.Letter();
            _held.Position = new Vector3(0.02f, -0.6f, -0.12f);
            _held.Rotation = new Vector3(0.3f, 0f, 0f);
            _held.Visible = false;
            _model.ArmL.AddChild(_held);
            _hammer = WeaponModels.Hammer();
            // In the fist the handle points forward and a little along the arm; the cannon's barrel continues the arm.
            _hammer.Rotation = new Vector3(-2.1f, 0f, 0f);
            _hammer.Visible = false;
            _model.Hand.AddChild(_hammer);
            _cannon = WeaponModels.Cannon();
            _cannon.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0f, 0f);
            _cannon.Visible = false;
            _model.Hand.AddChild(_cannon);

            AddChild(new SizedLabel3D
            {
                Name = "NameTag",
                Text = playerName,
                Position = new Vector3(0f, 2.25f, 0f),
                FontSize = 48,
                PixelSize = 0.008f,
                OutlineSize = 8,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            });
        }

        /// <summary>Moves a drawn carrier to its interpolated pose and plays what the snapshots and events say it does.</summary>
        public void Show(ClientPlayer player, Vector3 position, float yaw, float delta)
        {
            if (_model == null) return;
            // A respawn is a jump, not a sprint across the map.
            float moved = _placed ? new Vector2(position.X - _last.X, position.Z - _last.Z).Length() : 0f;
            if (delta > 0f && moved < 3f) _speed = Mathf.Lerp(_speed, moved / delta, 1f - Mathf.Exp(-12f * delta));
            _last = position;
            _placed = true;
            Position = position;
            Rotation = new Vector3(0f, yaw, 0f);

            if (player.AttackTick != _attackTick)
            {
                _attackTick = player.AttackTick;
                _weaponLeft = WeaponSeconds;
                bool hammer = player.AttackWeapon == Weapon.Hammer;
                _hammer!.Visible = hammer;
                _cannon!.Visible = !hammer;
                if (hammer) _model.Strike();
            }
            else if (_weaponLeft > 0f && (_weaponLeft -= delta) <= 0f) _hammer!.Visible = _cannon!.Visible = false;
            if (player.HurtTick != _hurtTick)
            {
                _hurtTick = player.HurtTick;
                _model.Flash();
            }
            _model.Aiming = _cannon!.Visible;
            _model.Animate(delta, _speed, windup: false, down: player.Dead);
            _held!.Visible = player.HeldSlot > 0 && !player.Dead && _weaponLeft <= 0f;
        }
    }
}
