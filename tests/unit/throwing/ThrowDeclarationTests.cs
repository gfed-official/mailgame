using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// throwing-catching story 006 (ADR-0015 sections 1 and 9; ADR-0007, ADR-0008; TC-N-3 Sim-size half, TR-throwing-014): the manifest
/// guardrail sizes and offsets of the three payloads, and the shared descriptor checks of the op and the two event kinds.
/// </summary>
public class ThrowDeclarationTests
{
    /// <summary>Rows: payload type, its size, and every field with its byte offset.</summary>
    public static TheoryData<Type, int, string> Layouts() => new()
    {
        {
            typeof(ThrowReleasePayload), 28,
            "EntryId 0, Units 4, Side 5, Flags 6, Reserved 7, FeetX 8, FeetY 12, FeetZ 16, DirX 20, DirY 22, DirZ 24, Pad 26"
        },
        {
            typeof(ThrowLaunched), 40,
            "FlightId 0, Thrower 2, Flags 3, EntryId 4, Units 8, Hop 9, KindDef 10, OriginX 12, OriginY 16, OriginZ 20, VelX 24, VelY 28, VelZ 32, LaunchTick 36"
        },
        {
            typeof(FlightResolved), 32,
            "FlightId 0, Terminal 2, Rank 3, Receiver 4, ContactX 8, ContactY 12, ContactZ 16, Tick 20, UnitsIn 24, UnitsFumbled 25, WorldItem 26, Pad 30"
        },
    };

    [Theory]
    [Trait("Id", "TC-N-3")]
    [MemberData(nameof(Layouts))]
    public void Payload_HasTheAdr0015SizeAndFieldOffsets(Type payload, int size, string fields)
    {
        // Arrange
        string[] expected = fields.Split(", ");

        // Act
        string[] actual = payload.GetFields().Where(f => !f.IsLiteral)
            .Select(f => (f.Name, Offset: (int)Marshal.OffsetOf(payload, f.Name)))
            .OrderBy(f => f.Offset)
            .Select(f => $"{f.Name} {f.Offset}")
            .ToArray();

        // Assert
        Assert.Equal(size, DescriptorAssert.SizeOf(payload));
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Id", "TR-throwing-014")]
    public void Descriptors_PassTheSharedOpAndEventChecks_InTheThrowingBlock()
    {
        DescriptorAssert.Check(ThrowDescriptors.ThrowRelease, OpKindBlock.Throwing);
        EventDescriptorAssert.Check(ThrowEventDescriptors.ThrowLaunched, OpKindBlock.Throwing);
        EventDescriptorAssert.Check(ThrowEventDescriptors.FlightResolved, OpKindBlock.Throwing);
    }
}
