using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests;

/// <summary>
/// Pins the wire shape of the Sim input types (input-controls story 002 AC-1 and AC-6, ADR-0012 section 4): sizes,
/// the 16-byte edge layout, blittability, the held-bit mapping and the append-only enum values. A field change must be
/// a deliberate edit of these numbers.
/// </summary>
public class InputFrameShapeTests
{
    /// <summary>Measured on net10.0 and net8.0 (ADR-0012 estimated about 560 B): scalar header and held bits, 32 x 16 B edges, 4 x 2 B steps, alignment padding.</summary>
    private const int InputFrameSize = 560;

    private static void RequireUnmanaged<T>()
        where T : unmanaged
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    [Fact]
    public void InputFrame_IsUnmanaged_AndSizeofIsPinned()
    {
        RequireUnmanaged<InputFrame>();
        RequireUnmanaged<InputEdge>();
        RequireUnmanaged<InputStep>();
        RequireUnmanaged<InputState>();
        RequireUnmanaged<InputEdgeBuffer>();
        RequireUnmanaged<InputStepBuffer>();

        Assert.Equal(InputFrameSize, Unsafe.SizeOf<InputFrame>());
        Assert.Equal(32 * 16, Unsafe.SizeOf<InputEdgeBuffer>());
        Assert.Equal(4 * 2, Unsafe.SizeOf<InputStepBuffer>());
        Assert.Equal(2, Unsafe.SizeOf<InputStep>());
    }

    [Fact]
    public void InputEdge_IsExactlySixteenBytes_WithTheAdrLayout()
    {
        Assert.Equal(16, Unsafe.SizeOf<InputEdge>());

        var edge = new InputEdge(
            FrameAction.Aim, EdgeKind.Cancel, arrivalSeq: 0xA1B2, routerStateId: 0x0304,
            reason: EdgeReason.Busy, deviceClass: 0x55, targetEntity: 0x01020304u, targetSlot: 0x66, presentedSeq: 0xC1D2);
        ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref edge, 1));

        Assert.True(BitConverter.IsLittleEndian);
        byte[] expected =
        {
            (byte)FrameAction.Aim, (byte)EdgeKind.Cancel, (byte)EdgeReason.Busy, 0x55,
            0x04, 0x03,                     // RouterStateId
            0xB2, 0xA1,                     // ArrivalSeq
            0x04, 0x03, 0x02, 0x01,         // TargetEntity
            0x66, 0x00,                     // TargetSlot, reserved
            0xD2, 0xC1,                     // PresentedSeq
        };
        Assert.Equal(expected, raw.ToArray());
    }

    [Fact]
    public void InputEdge_RoundTripsEveryField_AndDefaultsOptionalOnesToZero()
    {
        var full = new InputEdge(FrameAction.Interact, EdgeKind.Press, 7, 9, EdgeReason.EntryStale, 3, 0x0200_0001u, 4, 11);
        var plain = new InputEdge(FrameAction.Jump, EdgeKind.Release, 8, 9);

        Assert.Equal(FrameAction.Interact, full.Action);
        Assert.Equal(EdgeKind.Press, full.Kind);
        Assert.Equal(EdgeReason.EntryStale, full.Reason);
        Assert.Equal((byte)3, full.DeviceClass);
        Assert.Equal((ushort)9, full.RouterStateId);
        Assert.Equal((ushort)7, full.ArrivalSeq);
        Assert.Equal(0x0200_0001u, full.TargetEntity);
        Assert.Equal((byte)4, full.TargetSlot);
        Assert.Equal((ushort)11, full.PresentedSeq);
        Assert.Equal(EdgeReason.None, plain.Reason);
        Assert.Equal(0u, plain.TargetEntity);
        Assert.Equal((byte)0, plain.TargetSlot);
        Assert.Equal((ushort)0, plain.PresentedSeq);
        Assert.Equal((byte)0, plain.DeviceClass);
    }

    [Fact]
    public void InputFrameFields_HaveTheAdrWidths()
    {
        Type t = typeof(InputFrame);
        Assert.Equal(typeof(uint), t.GetProperty(nameof(InputFrame.Tick))!.PropertyType);
        Assert.Equal(typeof(short), t.GetProperty(nameof(InputFrame.MoveX))!.PropertyType);
        Assert.Equal(typeof(short), t.GetProperty(nameof(InputFrame.MoveY))!.PropertyType);
        Assert.Equal(typeof(ushort), t.GetProperty(nameof(InputFrame.LookYaw))!.PropertyType);
        Assert.Equal(typeof(short), t.GetProperty(nameof(InputFrame.LookPitch))!.PropertyType);
        Assert.Equal(typeof(short), t.GetProperty(nameof(InputFrame.FocusCosMinPm))!.PropertyType);
        Assert.Equal(typeof(ushort), t.GetProperty(nameof(InputFrame.RouterStateId))!.PropertyType);
        Assert.Equal(typeof(InputFrameFlags), t.GetProperty(nameof(InputFrame.Flags))!.PropertyType);
        Assert.Equal(typeof(ulong), t.GetProperty(nameof(InputFrame.HeldBits))!.PropertyType);
        Assert.Equal(typeof(byte), t.GetProperty(nameof(InputFrame.EdgeCount))!.PropertyType);
        Assert.Equal(typeof(byte), t.GetProperty(nameof(InputFrame.StepCount))!.PropertyType);
        Assert.Equal(typeof(ushort), t.GetProperty(nameof(InputFrame.DroppedEdges))!.PropertyType);
        Assert.Equal(typeof(sbyte), typeof(InputStep).GetProperty(nameof(InputStep.N))!.PropertyType);
        Assert.Equal(32, InputFrame.MaxEdges);
        Assert.Equal(4, InputFrame.MaxSteps);
    }

    [Fact]
    public void EdgeAndStepStorage_AreInlineArrays_OfTheDocumentedCapacity()
    {
        Assert.NotNull(typeof(InputEdgeBuffer).GetCustomAttribute<InlineArrayAttribute>());
        Assert.Equal(32, typeof(InputEdgeBuffer).GetCustomAttribute<InlineArrayAttribute>()!.Length);
        Assert.Equal(4, typeof(InputStepBuffer).GetCustomAttribute<InlineArrayAttribute>()!.Length);
    }

    [Fact]
    public void EnumValues_AreExplicitAndAppendOnly()
    {
        Assert.Equal(1, (int)EdgeKind.Press);
        Assert.Equal(2, (int)EdgeKind.Release);
        Assert.Equal(3, (int)EdgeKind.Cancel);
        Assert.Equal(
            new[] { "None", "Back", "StateChange", "FocusLost", "DeviceLost", "ToggleSecondPress", "EntryStale", "Busy" },
            Enum.GetNames<EdgeReason>());
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(i, (int)Enum.GetValues<EdgeReason>()[i]);
        }

        Assert.Equal(0, (int)FrameAction.Interact);
        Assert.Equal(1, (int)FrameAction.Jump);
        Assert.Equal(2, (int)FrameAction.Sprint);
        Assert.Equal(3, (int)FrameAction.Aim);
        Assert.All(Enum.GetValues<FrameAction>(), a => Assert.True((int)a < FrameActions.MaxCount));
        Assert.Equal(64, FrameActions.MaxCount);
        Assert.Equal(1, (int)InputFrameFlags.FirstInBatch);
        Assert.Equal(2, (int)InputFrameFlags.FocusLost);
        Assert.Equal(4, (int)InputFrameFlags.Paused);
        Assert.Equal(8, (int)InputFrameFlags.LookReseeded);
    }

    [Fact]
    public void HeldBits_BitN_IsFrameActionN()
    {
        foreach (FrameAction a in Enum.GetValues<FrameAction>())
        {
            Assert.Equal(1UL << (int)a, FrameActions.Bit(a));
        }

        Assert.Equal(0b0001UL, FrameActions.Bit(FrameAction.Interact));
        Assert.Equal(0b1000UL, FrameActions.Bit(FrameAction.Aim));
        Assert.Equal(1UL << 63, FrameActions.Bit((FrameAction)63));
        Assert.Equal(0UL, FrameActions.Bit((FrameAction)64));
        Assert.Equal(0UL, FrameActions.Bit((FrameAction)255));
    }

    [Fact]
    public void Frame_ByDefault_HasNoEdgesStepsOrFlags_AndIndexersThrowOutOfRange()
    {
        InputFrame f = default;

        Assert.Equal(0, f.EdgeCount);
        Assert.Equal(0, f.StepCount);
        Assert.True(f.Edges.IsEmpty);
        Assert.True(f.Steps.IsEmpty);
        Assert.False(f.IsFirstTickInBatch);
        Assert.Throws<ArgumentOutOfRangeException>(() => f.GetEdge(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.GetStep(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.GetStep(-1));
    }

    [Fact]
    public void DefaultClientInputSettings_HoldSettingIs400()
    {
        IClientInputSettings settings = new DefaultClientInputSettings();

        Assert.Equal(400, settings.HoldSettingMs);
        Assert.Equal(400, DefaultClientInputSettings.DefaultHoldSettingMs);
    }
}
