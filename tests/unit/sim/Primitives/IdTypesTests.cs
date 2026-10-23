using System.Reflection;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers the id value types (TR-sim-025, TR-sim-026 value-type half): layout, None and
/// +1 storage, EntryId real/temp layout, and explicit equality/hash/order (ADR-0004).
/// Assert-vs-masked behaviour is chosen by <see cref="TestBuildExpectation.SimAssertsExpected"/>;
/// the test assembly never defines SIM_ASSERT and never calls SimAssert itself.
/// </summary>
public class IdTypesTests
{
    /// <summary>
    /// Runs <paramref name="act"/>. With Sim asserts on, verifies it throws
    /// <see cref="SimAssertException"/> with <paramref name="code"/> and returns default;
    /// with asserts off, returns the (masked / None) result.
    /// </summary>
    private static T? RunExpectingAssertOrMask<T>(Func<T> act, ushort code)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => act());
            Assert.Equal(code, thrown.Code);
            return default;
        }

        return act();
    }

    // ---------------------------------------------------------------- AC-1: EntityId layout

    [Fact]
    public void EntityIdCompose_PlayerClassCounterOne_ValueIs16777217()
    {
        // Arrange
        const uint Expected = 16_777_217u;

        // Act
        EntityId id = EntityId.Compose(EntityClass.Player, 1);

        // Assert
        Assert.Equal(Expected, id.Value);
    }

    [Fact]
    public void EntityIdCompose_ConstructClassCounterOne_ValueIs50331649()
    {
        // Arrange
        const uint Expected = 50_331_649u;

        // Act
        EntityId id = EntityId.Compose(EntityClass.Construct, 1);

        // Assert
        Assert.Equal(Expected, id.Value);
    }

    [Fact]
    public void EntityIdDecode_RawValue50331649_ClassConstructCounterOne()
    {
        // Arrange
        var id = new EntityId(50_331_649u);

        // Act
        EntityClass cls = id.Class;
        uint counter = id.Counter;

        // Assert
        Assert.Equal(EntityClass.Construct, cls);
        Assert.Equal((byte)3, (byte)cls);
        Assert.Equal(1u, counter);
    }

    [Fact]
    public void EntityIdCompose_MaxCounterAndTestClass_RoundTripsWithoutOverflow()
    {
        // Arrange: highest class byte and highest counter exercise the top bits.
        // Act
        EntityId id = EntityId.Compose(EntityClass.TestLast, EntityId.CounterMask);

        // Assert
        Assert.Equal(0xFFFF_FFFFu, id.Value);
        Assert.Equal(EntityClass.TestLast, id.Class);
        Assert.Equal(0xFF_FFFFu, id.Counter);
    }

    [Fact]
    public void EntityId_NoneAndDefault_AreZeroAndEqual()
    {
        // Arrange
        EntityId defaulted = default;

        // Act / Assert
        Assert.Equal(0u, EntityId.None.Value);
        Assert.True(EntityId.None.IsNone);
        Assert.True(defaulted == EntityId.None);
        Assert.Equal(EntityClass.None, EntityId.None.Class);
        Assert.Equal(0u, EntityId.None.Counter);
        Assert.False(EntityId.Compose(EntityClass.Player, 1).IsNone);
    }

    [Fact]
    public void EntityIdCompose_CounterAboveMask_AssertsOrMasksTo24Bits()
    {
        // Arrange: WorldItem (0x02) has bit 24 clear, so an unmasked counter bit 24 would
        // visibly corrupt the class byte (Player's bit 24 is already set and would hide it).
        const uint Overflowing = 0x1FF_FFFFu;

        // Act
        EntityId masked = RunExpectingAssertOrMask(
            () => EntityId.Compose(EntityClass.WorldItem, Overflowing), SimAssertCode.IdCounterRange);

        // Assert (asserts on: the throw was verified in the helper)
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(EntityClass.WorldItem, masked.Class);
            Assert.Equal(0xFF_FFFFu, masked.Counter);
            Assert.Equal(0x02FF_FFFFu, masked.Value);
        }
    }

    [Fact]
    public void EntityClass_Values_MatchAdr0004Table()
    {
        // Arrange / Act / Assert
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 0xF0, 0xFF },
            new[]
            {
                (byte)EntityClass.None, (byte)EntityClass.Player, (byte)EntityClass.WorldItem,
                (byte)EntityClass.Construct, (byte)EntityClass.Vehicle, (byte)EntityClass.Enemy,
                (byte)EntityClass.Npc, (byte)EntityClass.Destination, (byte)EntityClass.Prop,
                (byte)EntityClass.TestFirst, (byte)EntityClass.TestLast,
            });
    }

    [Fact]
    public void ActorId_SystemAndNone_HaveReservedValues()
    {
        // Arrange
        ActorId defaulted = default;

        // Act / Assert
        Assert.Equal(0x0000_0001u, ActorId.System.Value);
        Assert.True(ActorId.System.IsSystem);
        Assert.False(ActorId.System.IsNone);
        Assert.Equal(0u, ActorId.None.Value);
        Assert.True(ActorId.None.IsNone);
        Assert.False(ActorId.None.IsSystem);
        Assert.True(defaulted == ActorId.None);
    }

    [Fact]
    public void ActorIdFromEntity_PlayerEntity_KeepsRawValueAndDiffersFromSystem()
    {
        // Arrange
        EntityId player = EntityId.Compose(EntityClass.Player, 1);

        // Act
        ActorId actor = ActorId.FromEntity(player);

        // Assert
        Assert.Equal(player.Value, actor.Value);
        Assert.NotEqual(ActorId.System, actor);
        Assert.Equal(new ActorId(player.Value), actor);
    }

    [Fact]
    public void MailId_NoneAndDefault_AreZero()
    {
        // Arrange
        MailId defaulted = default;

        // Act / Assert
        Assert.Equal(0u, MailId.None.Value);
        Assert.True(MailId.None.IsNone);
        Assert.True(defaulted == MailId.None);
        Assert.False(new MailId(1).IsNone);
        Assert.Equal(uint.MaxValue, new MailId(uint.MaxValue).Value);
    }

    // ---------------------------------------------------------------- AC-2: None and +1 storage

    [Fact]
    public void PlayerSlot_Default_IsNoneWithValue0xFF()
    {
        // Arrange
        PlayerSlot slot = default;

        // Act / Assert
        Assert.True(slot.IsNone);
        Assert.Equal((byte)0xFF, slot.Value);
        Assert.True(slot == PlayerSlot.None);
        Assert.Equal(PlayerSlot.NoneValue, slot.Value);
        Assert.Equal(8, PlayerSlot.Count);
    }

    [Fact]
    public void PlayerSlotFromValue_0xFF_IsNone()
    {
        // Arrange / Act
        PlayerSlot slot = PlayerSlot.FromValue(0xFF);

        // Assert (never asserts: 0xFF is the documented None encoding)
        Assert.True(slot.IsNone);
        Assert.Equal((byte)0xFF, slot.Value);
        Assert.True(slot == default);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)7)]
    public void PlayerSlotFromValue_ValidSlot_RoundTripsAndIsNotNone(byte raw)
    {
        // Arrange / Act
        PlayerSlot slot = PlayerSlot.FromValue(raw);

        // Assert
        Assert.Equal(raw, slot.Value);
        Assert.False(slot.IsNone);
        Assert.NotEqual(default, slot);
    }

    [Theory]
    [InlineData((byte)8)]
    [InlineData((byte)0xFE)]
    public void PlayerSlotFromValue_OutOfRange_AssertsOrReturnsNone(byte raw)
    {
        // Arrange / Act
        PlayerSlot slot = RunExpectingAssertOrMask(() => PlayerSlot.FromValue(raw), SimAssertCode.IdSlotRange);

        // Assert
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.True(slot.IsNone);
            Assert.Equal((byte)0xFF, slot.Value);
        }
    }

    [Fact]
    public void DefIndex_Default_IsNoneWithValue0xFFFF()
    {
        // Arrange
        DefIndex index = default;

        // Act / Assert
        Assert.True(index.IsNone);
        Assert.Equal((ushort)0xFFFF, index.Value);
        Assert.True(index == DefIndex.None);
        Assert.Equal(DefIndex.NoneValue, index.Value);
    }

    [Fact]
    public void DefIndexFromValue_0xFFFF_IsNone()
    {
        // Arrange / Act
        DefIndex index = DefIndex.FromValue(0xFFFF);

        // Assert
        Assert.True(index.IsNone);
        Assert.Equal((ushort)0xFFFF, index.Value);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)65_533)]
    public void DefIndexFromValue_ValidOrdinal_RoundTripsAndIsNotNone(ushort raw)
    {
        // Arrange / Act (0xFFFD is the highest ordinal: +1 storage must not overflow under checked arithmetic)
        DefIndex index = DefIndex.FromValue(raw);

        // Assert
        Assert.Equal(raw, index.Value);
        Assert.False(index.IsNone);
        Assert.Equal(DefIndex.MaxOrdinal, (ushort)65_533);
    }

    [Fact]
    public void DefIndexFromValue_OrdinalZero_IsNotNone()
    {
        // Arrange / Act
        DefIndex zero = DefIndex.FromValue(0);

        // Assert (default must never alias ordinal 0)
        Assert.False(zero.IsNone);
        Assert.Equal((ushort)0, zero.Value);
        Assert.NotEqual(default, zero);
    }

    [Theory]
    [InlineData((ushort)0xFFFE)]
    public void DefIndexFromValue_AboveMaxOrdinal_AssertsOrReturnsNone(ushort raw)
    {
        // Arrange / Act
        DefIndex index = RunExpectingAssertOrMask(() => DefIndex.FromValue(raw), SimAssertCode.DefIndexRange);

        // Assert
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.True(index.IsNone);
        }
    }

    // ---------------------------------------------------------------- AC-3: EntryId layout

    [Fact]
    public void EntryIdTemp_Slot5Counter0x12345_HasExactLayout()
    {
        // Arrange
        PlayerSlot slot = PlayerSlot.FromValue(5);
        const uint Expected = 0x8000_0000u | (5u << 20) | 0x12345u;

        // Act
        EntryId id = EntryId.Temp(slot, 0x12345);

        // Assert
        Assert.Equal(Expected, id.Value);
        Assert.True(id.IsTemp);
        Assert.Equal(0u, (id.Value >> 23) & 0xFFu); // bits 23-30 zero
        Assert.Equal(slot, id.TempSlot);
        Assert.Equal(0x12345u, id.TempCounter);
        Assert.False(id.IsNone);
    }

    [Fact]
    public void EntryIdTemp_Slot7MaxCounter_StaysInsideLayout()
    {
        // Arrange / Act
        EntryId id = EntryId.Temp(PlayerSlot.FromValue(7), 0xF_FFFF);

        // Assert
        Assert.Equal(0x8000_0000u | (7u << 20) | 0xF_FFFFu, id.Value);
        Assert.Equal(0u, (id.Value >> 23) & 0xFFu);
        Assert.Equal((byte)7, id.TempSlot.Value);
        Assert.Equal(0xF_FFFFu, id.TempCounter);
    }

    [Fact]
    public void EntryIdTemp_CounterAtLeast2Pow20_AssertsOrMasksTo20Bits()
    {
        // Arrange
        PlayerSlot slot = PlayerSlot.FromValue(2);

        // Act
        EntryId id = RunExpectingAssertOrMask(() => EntryId.Temp(slot, 0x10_0000u), SimAssertCode.EntryTempCtrRange);

        // Assert
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(0x8000_0000u | (2u << 20), id.Value);
            Assert.Equal(0u, id.TempCounter);
        }
    }

    [Fact]
    public void EntryIdTemp_NoneSlot_AssertsOrReturnsNone()
    {
        // Arrange / Act
        EntryId id = RunExpectingAssertOrMask(() => EntryId.Temp(PlayerSlot.None, 1), SimAssertCode.EntryTempSlotNone);

        // Assert
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.True(id.IsNone);
        }
    }

    [Fact]
    public void EntryIdReal_Bit31Clear_KeepsValueAndIsNotTemp()
    {
        // Arrange / Act
        EntryId id = EntryId.Real(0x7FFF_FFFFu);

        // Assert
        Assert.Equal(0x7FFF_FFFFu, id.Value);
        Assert.False(id.IsTemp);
        Assert.False(id.IsNone);
        Assert.True(id.TempSlot.IsNone);
        Assert.Equal(0u, id.TempCounter);
    }

    [Fact]
    public void EntryIdReal_Bit31Set_AssertsOrReturnsNone()
    {
        // Arrange / Act
        EntryId id = RunExpectingAssertOrMask(() => EntryId.Real(0x8000_0001u), SimAssertCode.EntryRealBit31);

        // Assert
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.True(id.IsNone);
        }
    }

    [Fact]
    public void EntryId_NoneAndDefault_AreZeroAndNotTemp()
    {
        // Arrange
        EntryId defaulted = default;

        // Act / Assert
        Assert.Equal(0u, EntryId.None.Value);
        Assert.True(EntryId.None.IsNone);
        Assert.False(EntryId.None.IsTemp);
        Assert.True(defaulted == EntryId.None);
    }

    [Fact]
    public void EntryIdFromValue_WellFormedRealAndTemp_RoundTrip()
    {
        // Arrange
        uint temp = 0x8000_0000u | (3u << 20) | 9u;

        // Act
        EntryId real = EntryId.FromValue(42u);
        EntryId tempId = EntryId.FromValue(temp);

        // Assert
        Assert.Equal(42u, real.Value);
        Assert.False(real.IsTemp);
        Assert.Equal(temp, tempId.Value);
        Assert.True(tempId.IsTemp);
    }

    [Fact]
    public void EntryIdFromValue_TempWithReservedBitsSet_AssertsOrReturnsNone()
    {
        // Arrange: bit 23 set inside a temp id.
        uint malformed = 0x8000_0000u | (1u << 23);

        // Act
        EntryId id = RunExpectingAssertOrMask(() => EntryId.FromValue(malformed), SimAssertCode.EntryTempLayout);

        // Assert
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.True(id.IsNone);
        }
    }

    // ---------------------------------------------------------------- StringId / FlightId basics

    [Fact]
    public void StringId_NoneAndDefault_HaveNamespaceNone()
    {
        // Arrange
        StringId defaulted = default;

        // Act / Assert
        Assert.True(StringId.None.IsNone);
        Assert.Equal(StringNamespace.None, StringId.None.Ns);
        Assert.Equal((ushort)0, StringId.None.Index);
        Assert.True(defaulted == StringId.None);
        Assert.Equal((byte)0, (byte)StringNamespace.None);
        Assert.Equal((byte)0xFF, (byte)StringNamespace.Run);
    }

    [Fact]
    public void StringId_Constructed_ExposesNamespaceAndIndex()
    {
        // Arrange / Act
        var id = new StringId(StringNamespace.Run, 3);

        // Assert
        Assert.Equal(StringNamespace.Run, id.Ns);
        Assert.Equal((ushort)3, id.Index);
        Assert.False(id.IsNone);
    }

    [Fact]
    public void FlightId_Constructed_ExposesValueIncludingWrapBoundaries()
    {
        // Arrange / Act
        var low = new FlightId(0);
        var high = new FlightId(ushort.MaxValue);

        // Assert (any value is valid; there is no None)
        Assert.Equal((ushort)0, low.Value);
        Assert.Equal(ushort.MaxValue, high.Value);
        Assert.Equal(default, low);
    }

    // ---------------------------------------------------------------- AC-4: equality, hashing, order

    private static readonly Type[] _allIdTypes =
    {
        typeof(EntityId), typeof(ActorId), typeof(MailId), typeof(PlayerSlot),
        typeof(EntryId), typeof(FlightId), typeof(DefIndex), typeof(StringId),
    };

    [Fact]
    public void IdTypes_Reflection_DeclareOwnEqualityMembers()
    {
        foreach (Type type in _allIdTypes)
        {
            // Arrange
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

            // Act
            MethodInfo? equalsObject = type.GetMethod(nameof(object.Equals), Flags, new[] { typeof(object) });
            MethodInfo? hash = type.GetMethod(nameof(object.GetHashCode), Flags, Type.EmptyTypes);
            MethodInfo? equalsTyped = type.GetMethod(nameof(object.Equals), Flags, new[] { type });
            MethodInfo? opEq = type.GetMethod("op_Equality", StaticFlags);
            MethodInfo? opNe = type.GetMethod("op_Inequality", StaticFlags);

            // Assert
            Assert.True(equalsObject is not null, type.Name + " must declare Equals(object)");
            Assert.True(hash is not null, type.Name + " must declare GetHashCode()");
            Assert.True(equalsTyped is not null, type.Name + " must declare Equals(T)");
            Assert.True(opEq is not null, type.Name + " must declare operator ==");
            Assert.True(opNe is not null, type.Name + " must declare operator !=");
            Assert.True(typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type), type.Name + " must implement IEquatable<T>");
        }
    }

    [Fact]
    public void IdTypes_Reflection_AreReadonlyStructsNotRecords()
    {
        foreach (Type type in _allIdTypes)
        {
            // Arrange / Act
            bool isStruct = type.IsValueType;
            bool hasPrintMembers = type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null;
            bool hasCloneMethod = type.GetMethod("<Clone>$") is not null;

            // Assert (record structs synthesize EqualityComparer-based members and PrintMembers)
            Assert.True(isStruct, type.Name);
            Assert.All(
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                field => Assert.True(field.IsInitOnly, type.Name + "." + field.Name + " must be readonly"));
            Assert.False(hasPrintMembers, type.Name);
            Assert.False(hasCloneMethod, type.Name);
        }
    }

    [Fact]
    public void FlightId_Reflection_ExposesNoComparableOrRelationalOperators()
    {
        // Arrange
        Type type = typeof(FlightId);
        string[] relational = { "op_LessThan", "op_GreaterThan", "op_LessThanOrEqual", "op_GreaterThanOrEqual" };

        // Act
        bool comparable = typeof(IComparable).IsAssignableFrom(type);
        bool comparableGeneric = typeof(IComparable<FlightId>).IsAssignableFrom(type);
        bool anyRelational = relational.Any(name => type.GetMethod(name, BindingFlags.Public | BindingFlags.Static) is not null);
        bool compareTo = type.GetMethod("CompareTo") is not null;

        // Assert
        Assert.False(comparable);
        Assert.False(comparableGeneric);
        Assert.False(anyRelational);
        Assert.False(compareTo);
    }

    [Fact]
    public void EntityIdAndMailId_Reflection_ImplementGenericComparable()
    {
        // Arrange / Act / Assert
        Assert.True(typeof(IComparable<EntityId>).IsAssignableFrom(typeof(EntityId)));
        Assert.True(typeof(IComparable<MailId>).IsAssignableFrom(typeof(MailId)));
    }

    [Fact]
    public void SingleFieldIds_GetHashCode_IsIntOfValue()
    {
        // Arrange
        const uint Raw = 0xDEAD_BEEFu;

        // Act / Assert
        Assert.Equal(unchecked((int)Raw), new EntityId(Raw).GetHashCode());
        Assert.Equal(unchecked((int)Raw), new ActorId(Raw).GetHashCode());
        Assert.Equal(unchecked((int)Raw), new MailId(Raw).GetHashCode());
        Assert.Equal(unchecked((int)0x8051_2345u), EntryId.Temp(PlayerSlot.FromValue(5), 0x12345).GetHashCode());
        Assert.Equal(77, EntryId.Real(77).GetHashCode());
        Assert.Equal(65_535, new FlightId(65_535).GetHashCode());
        Assert.Equal(5, PlayerSlot.FromValue(5).GetHashCode());
        Assert.Equal(0xFF, PlayerSlot.None.GetHashCode());
        Assert.Equal(12, DefIndex.FromValue(12).GetHashCode());
        Assert.Equal(0xFFFF, DefIndex.None.GetHashCode());
    }

    [Fact]
    public void StringId_GetHashCode_IsNsShiftedLeft16OrIndex()
    {
        // Arrange
        var run = new StringId(StringNamespace.Run, 3);
        var otherNs = new StringId((StringNamespace)7, 3);

        // Act / Assert
        Assert.Equal((0xFF << 16) | 3, run.GetHashCode());
        Assert.Equal((7 << 16) | 3, otherNs.GetHashCode());
        Assert.NotEqual(run.GetHashCode(), otherNs.GetHashCode());
        Assert.Equal(0, StringId.None.GetHashCode());
    }

    [Fact]
    public void EntityId_EqualityOperatorsAndEquals_AgreeOnValue()
    {
        // Arrange
        EntityId a = EntityId.Compose(EntityClass.Npc, 9);
        EntityId same = new(a.Value);
        EntityId other = EntityId.Compose(EntityClass.Npc, 10);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.False(a.Equals(other));
        Assert.False(a.Equals((object)other));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals(new MailId(a.Value)));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void ActorId_EqualityOperatorsAndEquals_AgreeOnValue()
    {
        // Arrange
        var a = new ActorId(5);
        var same = new ActorId(5);
        var other = new ActorId(6);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.False(a.Equals(other));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals(new EntityId(5)));
    }

    [Fact]
    public void MailId_EqualityOperatorsAndEquals_AgreeOnValue()
    {
        // Arrange
        var a = new MailId(5);
        var same = new MailId(5);
        var other = new MailId(6);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.False(a.Equals(other));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals(new EntityId(5)));
    }

    [Fact]
    public void PlayerSlot_EqualityOperatorsAndEquals_AgreeOnSlot()
    {
        // Arrange
        PlayerSlot a = PlayerSlot.FromValue(2);
        PlayerSlot same = PlayerSlot.FromValue(2);
        PlayerSlot other = PlayerSlot.FromValue(3);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.False(a.Equals(other));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals((byte)2));
        Assert.True(PlayerSlot.FromValue(0) != PlayerSlot.None);
    }

    [Fact]
    public void EntryId_EqualityOperatorsAndEquals_AgreeOnValue()
    {
        // Arrange
        EntryId a = EntryId.Real(10);
        EntryId same = EntryId.Real(10);
        EntryId other = EntryId.Real(11);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.False(a.Equals(other));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals(10u));
    }

    [Fact]
    public void FlightId_EqualityOperatorsAndEquals_AgreeOnValue()
    {
        // Arrange
        var a = new FlightId(10);
        var same = new FlightId(10);
        var other = new FlightId(11);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.False(a.Equals(other));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals((ushort)10));
    }

    [Fact]
    public void DefIndex_EqualityOperatorsAndEquals_AgreeOnOrdinal()
    {
        // Arrange
        DefIndex a = DefIndex.FromValue(4);
        DefIndex same = DefIndex.FromValue(4);
        DefIndex other = DefIndex.FromValue(5);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != other);
        Assert.False(a == other);
        Assert.False(a.Equals(other));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals((ushort)4));
        Assert.True(DefIndex.FromValue(0) != DefIndex.None);
    }

    [Fact]
    public void StringId_EqualityOperatorsAndEquals_RequireBothFields()
    {
        // Arrange
        var a = new StringId(StringNamespace.Run, 3);
        var same = new StringId(StringNamespace.Run, 3);
        var otherIndex = new StringId(StringNamespace.Run, 4);
        var otherNs = new StringId((StringNamespace)1, 3);

        // Act / Assert
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.True(a != otherIndex);
        Assert.True(a != otherNs);
        Assert.False(a == otherIndex);
        Assert.False(a == otherNs);
        Assert.False(a.Equals(otherIndex));
        Assert.False(a.Equals((object)otherNs));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void EntityIdCompareTo_DifferentValues_OrdersByRawValueAscending()
    {
        // Arrange: class byte dominates, then counter.
        EntityId playerHigh = EntityId.Compose(EntityClass.Player, 0xFF_FFFF);
        EntityId itemLow = EntityId.Compose(EntityClass.WorldItem, 1);
        EntityId playerLow = EntityId.Compose(EntityClass.Player, 1);
        EntityId highBit = new(0xFFFF_FFFFu); // would be negative if compared as int

        // Act
        EntityId[] sorted = { highBit, itemLow, playerHigh, playerLow, EntityId.None };
        Array.Sort(sorted);

        // Assert
        Assert.Equal(new[] { EntityId.None, playerLow, playerHigh, itemLow, highBit }, sorted);
        Assert.True(playerLow.CompareTo(itemLow) < 0);
        Assert.True(itemLow.CompareTo(playerLow) > 0);
        Assert.Equal(0, playerLow.CompareTo(new EntityId(playerLow.Value)));
        Assert.True(playerLow.CompareTo(highBit) < 0);
    }

    [Fact]
    public void MailIdCompareTo_DifferentValues_OrdersByRawValueAscendingLowerIsOlder()
    {
        // Arrange
        var oldest = new MailId(1);
        var newer = new MailId(2);
        var huge = new MailId(uint.MaxValue); // would be negative if compared as int

        // Act
        MailId[] sorted = { huge, newer, oldest, MailId.None };
        Array.Sort(sorted);

        // Assert
        Assert.Equal(new[] { MailId.None, oldest, newer, huge }, sorted);
        Assert.True(oldest.CompareTo(newer) < 0);
        Assert.True(huge.CompareTo(newer) > 0);
        Assert.Equal(0, newer.CompareTo(new MailId(2)));
    }
}
