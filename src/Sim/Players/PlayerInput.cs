using System;

namespace PerformativeMail.Sim.Players
{
    [Flags]
    public enum InputButtons : byte
    {
        None = 0,
        Sprint = 1 << 0,
        Jump = 1 << 1,
        Interact = 1 << 2,
        /// <summary>Swing the hammer.</summary>
        Attack = 1 << 3,
        /// <summary>Fire the mail cannon.</summary>
        AltAttack = 1 << 4,
    }

    /// <summary>
    /// One tick of player input. Movement is a world-space direction quantised to ±127 so the
    /// server and the predicting client step from bit-identical values.
    /// </summary>
    public struct PlayerInput
    {
        /// <summary>Client input tick; strictly increasing per client.</summary>
        public uint Seq;
        public sbyte MoveX;
        public sbyte MoveZ;
        /// <summary>Facing, 0..65535 = 0..360°.</summary>
        public ushort Yaw;
        public InputButtons Buttons;
        /// <summary>Selected hotbar slot, 0-based (0 = hands).</summary>
        public byte HeldSlot;
        /// <summary>
        /// The server tick the client drew the raiders at when it made this input, its low 16 bits;
        /// 0: not said (doc 06 §4.3 lag compensation rewinds them there for the hit).
        /// </summary>
        public ushort View;

        /// <summary>The wire's <see cref="View"/> of a drawn tick: never 0, which says none.</summary>
        public static ushort ViewOf(double drawnTick)
        {
            if (drawnTick < 1.0) return 0;
            ushort v = (ushort)((long)System.Math.Round(drawnTick) & 0xFFFF);
            return v == 0 ? (ushort)1 : v;
        }

        public bool Has(InputButtons b) => (Buttons & b) != 0;

        public static sbyte QuantizeAxis(float v)
        {
            if (v > 1f) v = 1f;
            if (v < -1f) v = -1f;
            return (sbyte)Math.Round(v * 127f);
        }

        public static ushort QuantizeYaw(float radians)
        {
            double turns = radians / (2.0 * Math.PI);
            turns -= Math.Floor(turns);
            return (ushort)((int)Math.Round(turns * 65536.0) & 0xFFFF);
        }

        public static float YawToRadians(ushort yaw) => (float)(yaw / 65536.0 * 2.0 * Math.PI);
    }
}
