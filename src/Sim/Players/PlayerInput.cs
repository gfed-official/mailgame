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
