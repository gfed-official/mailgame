using System.Buffers;
using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 001, "Dump" (ADR-0006 section 4): Players' state contributor writes per slot in ascending order the
/// player id, the life fields and every <see cref="CarrierState"/> field one by one; changing any single field changes the dump
/// bytes, and two identical joins produce identical bytes.
/// </summary>
public class PlayersDumpTests
{
    private const byte Marker = 7;

    private delegate void Mutation(ref PlayerRecord r);

    // ---------------------------------------------------------------- helpers

    /// <summary>The payload of the section with id <paramref name="id"/> in a whole-world dump (header, then id u16 / length u32 / payload).</summary>
    private static byte[] Section(byte[] dump, ushort id)
    {
        int pos = 6;
        while (pos < dump.Length)
        {
            ushort sectionId = BitConverter.ToUInt16(dump, pos);
            int length = (int)BitConverter.ToUInt32(dump, pos + 2);
            if (sectionId == id)
            {
                return dump.AsSpan(pos + 6, length).ToArray();
            }

            pos += 6 + length;
        }

        Assert.Fail($"Section {id} is not in the dump.");
        return Array.Empty<byte>();
    }

    private static byte[] PlayersSection(PlayersRig rig) => Section(DumpHash.Bytes(rig.Sim), SectionIds.Players);

    private static PlayersRig TwoPlayers()
    {
        var rig = PlayersRig.Create();
        rig.TickEmpty(1);
        rig.JoinAndTick(0);
        rig.JoinAndTick(3);
        return rig;
    }

    /// <summary>The expected Players section for the record <paramref name="r"/> in slot 0 only, written field by field.</summary>
    private static byte[] Expected(params (byte Slot, PlayerRecord Record)[] players)
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());
        w.BeginSection(1);
        w.WriteU32((uint)players.Length);
        foreach ((byte slot, PlayerRecord r) in players)
        {
            w.WriteU8(slot);
            w.WriteU32(r.PlayerId.Value);
            w.WriteU8((byte)r.Life);
            w.WriteU32(r.ClockInTick);
            w.WriteU32(r.FrozenUntilTick);
            w.WriteU32(r.ProtectedUntilTick);
            w.WriteU8(r.TeleportSeq);
            w.WriteU16(r.LookYaw);
            w.WriteU16((ushort)r.LookPitch);
            CarrierState c = r.Carrier;
            w.WriteI32(c.X);
            w.WriteI32(c.Y);
            w.WriteI32(c.Z);
            w.WriteI32(c.RemX);
            w.WriteI32(c.RemZ);
            w.WriteI32(c.RemY);
            w.WriteI32(c.Vx);
            w.WriteI32(c.Vz);
            w.WriteI32(c.Vy);
            w.WriteU16(c.BodyYaw);
            w.WriteU8((byte)c.Loco);
            w.WriteU16((ushort)c.Flags);
            w.WriteU8(c.CoyoteLeft);
            w.WriteU8(c.JumpBufferLeft);
            w.WriteU8(c.HaulOutTicks);
            w.WriteU32(c.SupportId);
            w.WriteI32(c.SupportLocalX);
            w.WriteI32(c.SupportLocalY);
            w.WriteI32(c.SupportLocalZ);
            w.WriteU16(c.SupportYawRef);
            for (int i = 0; i < PushPairArray.Length; i++)
            {
                w.WriteU16(c.Pair[i].OverlapTicks);
                w.WriteI32(c.Pair[i].WindowStartX);
                w.WriteI32(c.Pair[i].WindowStartZ);
                w.WriteU8(c.Pair[i].Ghosted);
            }

            w.WriteU16(c.WadeSlowPm);   // story 009: appended after the pairs
        }

        w.EndSection();
        return output.WrittenSpan[6..].ToArray();
    }

    private static object MarkerFor(Type t) =>
        t == typeof(EntityId) ? new EntityId(Marker) : t.IsEnum ? Enum.ToObject(t, Marker) : Convert.ChangeType(Marker, t);

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void TwoIdenticalJoinSequences_ProduceIdenticalDumpBytes()
    {
        // Arrange + Act
        byte[] a = DumpHash.Bytes(TwoPlayers().Sim);
        byte[] b = DumpHash.Bytes(TwoPlayers().Sim);

        // Assert
        Assert.Equal(a, b);
        Assert.Equal(DumpHash.Sha256(TwoPlayers().Sim), DumpHash.Sha256(TwoPlayers().Sim));
    }

    [Fact]
    public void Dump_HasAPlayersSection_AfterSimCore_AndAJoinChangesIt()
    {
        // Arrange
        var rig = PlayersRig.Create();
        byte[] before = PlayersSection(rig);

        // Act
        rig.JoinAndTick(0);
        byte[] after = PlayersSection(rig);

        // Assert
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, before);
        Assert.NotEqual(before, after);
        Assert.Equal(SectionIds.Players, new PlayersStateContributor(rig.Players).SectionId);
        Assert.NotEqual(SectionIds.SimCore, SectionIds.Players);
    }

    [Fact]
    public void Dump_AfterLeave_ReturnsToTheEmptyPlayersSection()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(2);

        // Act
        rig.LeaveAndTick(2);

        // Assert
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PlayersSection(rig));
    }

    // ---------------------------------------------------------------- layout

    [Fact]
    public void Section_WritesSlotsInAscendingOrder_FieldByField_InTheDocumentedLayout()
    {
        // Arrange: slot 3 joins before slot 0, yet slot 0 is written first.
        var rig = PlayersRig.Create();
        rig.JoinAndTick(3);
        rig.JoinAndTick(0);
        ref PlayerRecord r0 = ref rig.Record(0);
        r0.Carrier.Pair[6].WindowStartZ = -12;
        r0.Carrier.Vx = -4000;
        r0.LookPitch = -300;
        ref PlayerRecord r3 = ref rig.Record(3);
        r3.Carrier.Flags = CarrierFlags.OnSupport | CarrierFlags.Wading;
        r3.Carrier.SupportId = 0xFEEDu;

        // Act
        byte[] actual = PlayersSection(rig);

        // Assert
        Assert.Equal(Expected((0, rig.Record(0)), (3, rig.Record(3))), actual);
    }

    [Fact]
    public void Section_EmptyWorld_IsJustAZeroCount()
    {
        var rig = PlayersRig.Create();
        Assert.Equal(Expected(), PlayersSection(rig));
    }

    [Theory]
    [InlineData((short)-2, (byte)0xFE, (byte)0xFF)]
    [InlineData((short)-300, (byte)0xD4, (byte)0xFE)]
    [InlineData((short)1500, (byte)0xDC, (byte)0x05)]
    [InlineData(short.MinValue, (byte)0x00, (byte)0x80)]
    public void Dump_LookPitch_IsWrittenAsTwosComplementLittleEndianU16(short pitch, byte low, byte high)
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        rig.Record(0).LookPitch = pitch;

        // Act
        byte[] section = PlayersSection(rig);

        // Assert: count u32 (4) + slot (1) + id (4) + life (1) + three deadlines (12) + teleportSeq (1) + lookYaw (2) = 25.
        Assert.Equal(new[] { low, high }, section.AsSpan(25, 2).ToArray());
    }

    [Fact]
    public void Dump_NegativeLookPitch_IsWrittenAsTwosComplementU16()
    {
        // Arrange
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        rig.Record(0).LookPitch = -1;

        // Act
        byte[] section = PlayersSection(rig);

        // Assert
        Assert.Equal(new byte[] { 0xFF, 0xFF }, section.AsSpan(25, 2).ToArray());
    }

    // ---------------------------------------------------------------- per-field sensitivity

    [Fact]
    public void EveryCarrierStateField_ChangesTheDumpBytes()
    {
        // Arrange
        var rig = TwoPlayers();
        byte[] baseline = DumpHash.Bytes(rig.Sim);
        FieldInfo[] fields = typeof(CarrierState).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(fields, f => f.Name == "Pair");
        PlayerRecord original = rig.Record(3);

        foreach (FieldInfo f in fields.Where(f => f.Name != "Pair"))
        {
            // Act: write a marker into one field (via a boxed copy) and dump.
            object box = original.Carrier;
            f.SetValue(box, MarkerFor(f.FieldType));
            rig.Record(3).Carrier = (CarrierState)box;
            byte[] changed = DumpHash.Bytes(rig.Sim);
            rig.Record(3) = original;

            // Assert
            Assert.False(baseline.AsSpan().SequenceEqual(changed), $"CarrierState.{f.Name} is not in the dump.");
        }

        Assert.True(baseline.AsSpan().SequenceEqual(DumpHash.Bytes(rig.Sim)), "The restore left a difference.");
    }

    [Fact]
    public void EveryPushPairField_OfEverySlot_ChangesTheDumpBytes()
    {
        // Arrange
        var rig = TwoPlayers();
        byte[] baseline = DumpHash.Bytes(rig.Sim);
        FieldInfo[] pairFields = typeof(PushPairState).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(4, pairFields.Length);

        for (int i = 0; i < PushPairArray.Length; i++)
        {
            foreach (FieldInfo f in pairFields)
            {
                // Act
                object pair = rig.Record(0).Carrier.Pair[i];
                f.SetValue(pair, MarkerFor(f.FieldType));
                rig.Record(0).Carrier.Pair[i] = (PushPairState)pair;
                byte[] changed = DumpHash.Bytes(rig.Sim);
                rig.Record(0).Carrier.Pair[i] = default;

                // Assert
                Assert.False(baseline.AsSpan().SequenceEqual(changed), $"Pair[{i}].{f.Name} is not in the dump.");
            }
        }

        Assert.True(baseline.AsSpan().SequenceEqual(DumpHash.Bytes(rig.Sim)));
    }

    [Fact]
    public void EveryPlayerRecordField_ExceptTheDerivedCapsule_ChangesTheDumpBytes()
    {
        // Arrange
        var rig = TwoPlayers();
        byte[] baseline = DumpHash.Bytes(rig.Sim);
        PlayerRecord original = rig.Record(0);
        FieldInfo[] fields = typeof(PlayerRecord).GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (FieldInfo f in fields.Where(f => f.Name != nameof(PlayerRecord.Carrier)))
        {
            // Act
            object box = original;
            f.SetValue(box, MarkerFor(f.FieldType));
            rig.Record(0) = (PlayerRecord)box;
            byte[] changed = DumpHash.Bytes(rig.Sim);
            rig.Record(0) = original;

            // Assert: the capsule is derived from the settings, so it is deliberately not dumped.
            bool derived = f.Name is nameof(PlayerRecord.CapsuleRadiusMm) or nameof(PlayerRecord.CapsuleHeightMm);
            Assert.Equal(derived, baseline.AsSpan().SequenceEqual(changed));
        }
    }

    [Fact]
    public void PlayerId_InTheRecord_ChangesTheDump_AndSlotIsPartOfTheKey()
    {
        // Arrange: the same single player in slot 1 versus slot 2 dumps differently (slot is written).
        var a = PlayersRig.Create();
        a.JoinAndTick(1);
        var b = PlayersRig.Create();
        b.JoinAndTick(2);

        // Assert
        Assert.NotEqual(PlayersSection(a), PlayersSection(b));
    }
}
