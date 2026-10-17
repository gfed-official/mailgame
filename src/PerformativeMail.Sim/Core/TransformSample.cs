using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// One transform sample from a remote (or loopback) player: a padding-free blittable struct of integers only (no floats), 24 bytes.
/// Samples are not ops: the adapter writes them with <see cref="SimWorld.TryWriteSample"/>, and Sim Core latches them in the tick
/// prologue. Sim Core copies the fields and writes them field by field (<see cref="WriteCanonical"/>); it never interprets the
/// body, so the Player Character epic can change it without touching the ring, latch, recorder or dump mechanics.
/// <para>
/// PROVISIONAL — layout owned by ADR-0013/0020 (Player Character). The mechanics fields are <see cref="Slot"/> and
/// <see cref="Seq"/>; the rest is the Player Character GDD's sample content as integers.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var s = new TransformSample { Seq = 7, PosXMm = 1500, PosYMm = 0, PosZMm = -2000, YawBam = 0x4000 };
/// bool queued = world.TryWriteSample(PlayerSlot.FromValue(2), in s);
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TransformSample
{
    /// <summary>Per-sender sequence number, monotonic within a connection (u32; the adapter widens a narrower wire value).</summary>
    public uint Seq;

    /// <summary>Position X in millimetres.</summary>
    public int PosXMm;

    /// <summary>Position Y in millimetres.</summary>
    public int PosYMm;

    /// <summary>Position Z in millimetres.</summary>
    public int PosZMm;

    /// <summary>Yaw as a binary angle (0 to 65535 = one turn).</summary>
    public ushort YawBam;

    /// <summary>Pitch as a binary angle.</summary>
    public ushort PitchBam;

    /// <summary>Raw slot value (0 to 7) of the sender; <see cref="SimWorld.TryWriteSample"/> overwrites it from its slot argument.</summary>
    public byte Slot;

    /// <summary>Flag bits (meaning owned by Player Character).</summary>
    public byte Flags;

    /// <summary>Explicit padding, always written (so the struct has no implicit padding).</summary>
    public byte Pad0;

    /// <summary>Explicit padding, always written.</summary>
    public byte Pad1;

    /// <summary>Writes every field, one by one, in declaration order (little-endian).</summary>
    /// <param name="w">The canonical writer; must be inside a section.</param>
    /// <example><code>sample.WriteCanonical(ref writer);</code></example>
    public readonly void WriteCanonical(ref CanonicalWriter w)
    {
        w.WriteU32(Seq);
        w.WriteI32(PosXMm);
        w.WriteI32(PosYMm);
        w.WriteI32(PosZMm);
        w.WriteU16(YawBam);
        w.WriteU16(PitchBam);
        w.WriteU8(Slot);
        w.WriteU8(Flags);
        w.WriteU8(Pad0);
        w.WriteU8(Pad1);
    }
}
