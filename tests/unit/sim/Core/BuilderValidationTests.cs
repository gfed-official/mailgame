using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-012 for story 017 (GDD SC-R4-1 rows 1 to 4, ADR-0003 section 3, ADR-0007 section 4, ADR-0008 sections 3 and 4):
/// every build-time violation throws <see cref="SimBuildException"/> with its own code, a valid build freezes and ticks, and no
/// bind surface is reachable once the world is <c>Ready</c>.
/// </summary>
public class BuilderValidationTests
{
    private const OpKind KindA = (OpKind)0x0100;
    private const OpKind KindB = (OpKind)0x0101;
    private const OpKind KindC = (OpKind)0x0102;
    private const EventKind EvKind = (EventKind)0x0200;

    private sealed class StubHandler : IOpHandler
    {
        public StubHandler(OpKind kind)
        {
            Kind = kind;
        }

        public OpKind Kind { get; }

        public Reply Apply(in Op op, in TickContext ctx) => Reply.Ok(in op);
    }

    private sealed class NoopPhase : ISimPhase
    {
        public int Runs { get; private set; }

        public void Run(in TickContext ctx) => Runs++;
    }

    private sealed class NoopCheck : ITickEndCheck
    {
        public void Check(uint tick)
        {
        }
    }

    private static OpDescriptor Op(OpKind kind, params ReasonDef[] reasons) => OpDescriptor.Empty(kind, false, 1, 0, reasons);

    private static SimBuildErrorCode CodeOf(Action action) => Assert.Throws<SimBuildException>(action).Code;

    // ---------------------------------------------------------------- AC-1 (SC-R4-1 rows 1 to 4)

    public static TheoryData<string, SimBuildErrorCode> Ac1Rows => new()
    {
        { "second handler for one OpKind", SimBuildErrorCode.DuplicateOpHandler },
        { "declared OpKind with no handler", SimBuildErrorCode.MissingOpHandler },
        { "ninth BindEventReader", SimBuildErrorCode.ReaderLimit },
        { "any bind after Freeze", SimBuildErrorCode.BindAfterFreeze },
    };

    [Theory]
    [Trait("Id", "SC-R4-1")]
    [MemberData(nameof(Ac1Rows))]
    public void Ac1_BuildViolation_ThrowsItsOwnCode(string scenario, SimBuildErrorCode expected)
    {
        // Arrange + Act
        Action violation = scenario switch
        {
            "second handler for one OpKind" => SecondHandler,
            "declared OpKind with no handler" => MissingHandler,
            "ninth BindEventReader" => NinthReader,
            _ => BindAfterFreeze,
        };

        // Assert
        Assert.Equal(expected, CodeOf(violation));
    }

    private static void SecondHandler()
    {
        var builder = new SimWorldBuilder();
        OpDescriptor d = Op(KindA);
        builder.BindOp(d, new StubHandler(KindA));
        builder.BindOp(d, new StubHandler(KindA));
    }

    private static void MissingHandler()
    {
        var builder = new SimWorldBuilder();
        builder.BindOp(Op(KindA), new StubHandler(KindA));
        builder.DeclareOp(Op(KindB));
        builder.Freeze();
    }

    private static void NinthReader()
    {
        var builder = new SimWorldBuilder();
        for (byte i = 1; i <= 9; i++)
        {
            builder.BindEventReader(new ReaderId(i), new[] { EventKind.OpReply });
        }
    }

    private static void BindAfterFreeze()
    {
        var builder = new SimWorldBuilder();
        builder.Freeze();
        builder.Bind(PhaseId.MailStep, new NoopPhase());
    }

    [Fact]
    public void Ac1_EveryBindSurfaceAfterFreeze_ThrowsBindAfterFreeze()
    {
        var builder = new SimWorldBuilder();
        builder.Freeze();
        Action[] binds =
        {
            () => builder.Bind(PhaseId.MailStep, new NoopPhase()),
            () => builder.BindTickEnd(new NoopCheck()),
            () => builder.DeclareOp(Op(KindA)),
            () => builder.BindOp(Op(KindA), new StubHandler(KindA)),
            () => builder.BindEventKind(EventDescriptor.For<Pay4>(EvKind, ReplicationClass.Internal, false, 0, 0, 4)),
            () => builder.BindEventSink(new RecordingSink()),
            () => builder.BindEventReader(new ReaderId(1), new[] { EventKind.OpReply }),
            () => builder.Freeze(),
        };

        foreach (Action bind in binds)
        {
            Assert.Equal(SimBuildErrorCode.BindAfterFreeze, CodeOf(bind));
        }
    }

    [Fact]
    public void Ac1_BindOpWithNullHandler_FreezeThrowsMissingOpHandler_AndALaterHandlerFixesIt()
    {
        var builder = new SimWorldBuilder();
        OpDescriptor d = Op(KindA);
        builder.BindOp(d, null!);

        Assert.Equal(SimBuildErrorCode.MissingOpHandler, CodeOf(() => builder.Freeze()));

        // A failed freeze leaves the builder usable: attach the handler and retry.
        builder.BindOp(d, new StubHandler(KindA));
        Assert.True(builder.Freeze().TryGetOp(KindA, out _, out _));
    }

    [Fact]
    public void Ac1_DeclareOpWithADifferentDescriptorForADeclaredKind_ThrowsDuplicateOpHandler()
    {
        var builder = new SimWorldBuilder();
        builder.DeclareOp(Op(KindA));

        Assert.Equal(SimBuildErrorCode.DuplicateOpHandler, CodeOf(() => builder.DeclareOp(Op(KindA))));
    }

    // ---------------------------------------------------------------- AC-2 descriptor codes

    [Fact]
    public void Ac2_OpDescriptorPayloadOver48_ThrowsPayloadTooLarge()
    {
        var builder = new SimWorldBuilder();
        var tooBig = new OpDescriptor(KindA, 49, false, 1, 0, Array.Empty<ReasonDef>());

        Assert.Equal(SimBuildErrorCode.PayloadTooLarge, CodeOf(() => builder.DeclareOp(tooBig)));
        Assert.Equal(SimBuildErrorCode.PayloadTooLarge, CodeOf(() => builder.BindOp(tooBig, new StubHandler(KindA))));
    }

    [Fact]
    public void Ac2_EventDescriptorPayloadOver48_ThrowsPayloadTooLarge()
    {
        var builder = new SimWorldBuilder();
        var tooBig = new EventDescriptor(EvKind, ReplicationClass.Internal, 49, false, 0, 0, 0);

        Assert.Equal(SimBuildErrorCode.PayloadTooLarge, CodeOf(() => builder.BindEventKind(tooBig)));
    }

    [Fact]
    public void Ac2_OpDescriptorSystemReasonBelow64_ThrowsSystemReasonBelow64()
    {
        var builder = new SimWorldBuilder();

        Assert.Equal(SimBuildErrorCode.SystemReasonBelow64, CodeOf(() => builder.DeclareOp(Op(KindA, new ReasonDef(64, "ok"), new ReasonDef(63, "low")))));
        Assert.Equal(SimBuildErrorCode.SystemReasonBelow64, CodeOf(() => builder.BindOp(Op(KindB, new ReasonDef(0, "zero")), new StubHandler(KindB))));
    }

    [Fact]
    public void Ac2_OpDescriptorSystemReason64_IsAccepted()
    {
        var builder = new SimWorldBuilder();

        builder.BindOp(Op(KindA, new ReasonDef(64, "first")), new StubHandler(KindA));

        Assert.True(builder.Freeze().TryGetOp(KindA, out _, out _));
    }

    [Fact]
    public void Ac2_SecondEventDescriptorForOneKind_ThrowsDuplicateEventKind()
    {
        var builder = new SimWorldBuilder();
        builder.BindEventKind(EventDescriptor.For<Pay4>(EvKind, ReplicationClass.Internal, false, 0, 0, 4));

        Assert.Equal(
            SimBuildErrorCode.DuplicateEventKind,
            CodeOf(() => builder.BindEventKind(EventDescriptor.For<Pay12>(EvKind, ReplicationClass.Replicated, false, 0, 0, 12))));
    }

    [Fact]
    public void Ac2_OpKindNone_ThrowsInvalidKind()
    {
        var builder = new SimWorldBuilder();

        Assert.Equal(SimBuildErrorCode.InvalidKind, CodeOf(() => builder.DeclareOp(Op(OpKind.None))));
        Assert.Equal(SimBuildErrorCode.InvalidKind, CodeOf(() => builder.BindOp(Op(OpKind.None), new StubHandler(OpKind.None))));
    }

    [Fact]
    public void Ac2_EventKindNone_ThrowsInvalidKind()
    {
        var builder = new SimWorldBuilder();
        var none = new EventDescriptor(EventKind.None, ReplicationClass.Internal, 0, false, 0, 0, 0);

        Assert.Equal(SimBuildErrorCode.InvalidKind, CodeOf(() => builder.BindEventKind(none)));
    }

    [Fact]
    public void Ac2_ValidBuild_FreezesAndTicksWithoutThrowing()
    {
        // Arrange: one op kind, one event kind, one reader, phase binders and a tick-end check.
        var builder = new SimWorldBuilder();
        var phase = new NoopPhase();
        builder.BindOp(Op(KindA, new ReasonDef(64, "stale")), new StubHandler(KindA));
        builder.BindEventKind(EventDescriptor.For<Pay4>(EvKind, ReplicationClass.Internal, false, 0, 0, 4));
        builder.BindEventReader(new ReaderId(1), new[] { EvKind });
        builder.Bind(PhaseId.MailStep, phase);
        builder.BindTickEnd(new NoopCheck());

        // Act
        SimWorld world = builder.Freeze();
        world.Tick();
        world.Tick();

        // Assert
        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(2, phase.Runs);
        Assert.True(world.TryGetOp(KindA, out OpDescriptor? d, out IOpHandler? h));
        Assert.Equal(KindA, d.Kind);
        Assert.Equal(KindA, h.Kind);
    }

    [Fact]
    public void Ac2_TryGetOp_UnsortedBindOrder_FindsEveryKindAndMissesUndeclared()
    {
        var builder = new SimWorldBuilder();
        OpKind[] order = { KindC, KindA, KindB };
        foreach (OpKind kind in order)
        {
            builder.BindOp(Op(kind), new StubHandler(kind));
        }

        SimWorld world = builder.Freeze();

        foreach (OpKind kind in order)
        {
            Assert.True(world.TryGetOp(kind, out OpDescriptor? d, out IOpHandler? h));
            Assert.Equal(kind, d.Kind);
            Assert.Equal(kind, h.Kind);
        }

        Assert.False(world.TryGetOp((OpKind)0x0103, out _, out _));
        Assert.False(world.TryGetOp(OpKind.None, out _, out _));
    }

    [Fact]
    public void Ac2_FrozenWorld_ExposesNoBuildSurface()
    {
        // The world has no Bind/Declare member: build errors cannot be raised from a Ready world.
        string[] buildMembers = typeof(SimWorld).GetMethods().Select(m => m.Name).Where(n => n.StartsWith("Bind") || n.StartsWith("Declare") || n == "Freeze").ToArray();

        Assert.Empty(buildMembers);
    }

    [Fact]
    public void Ac2_SimBuildErrorCode_ValuesArePinned()
    {
        Assert.Equal(1, (int)SimBuildErrorCode.ReservedPhase);
        Assert.Equal(2, (int)SimBuildErrorCode.BindAfterFreeze);
        Assert.Equal(3, (int)SimBuildErrorCode.DuplicateEventKind);
        Assert.Equal(4, (int)SimBuildErrorCode.InvalidEventDescriptor);
        Assert.Equal(5, (int)SimBuildErrorCode.EventSinkAlreadyBound);
        Assert.Equal(6, (int)SimBuildErrorCode.ReaderLimit);
        Assert.Equal(7, (int)SimBuildErrorCode.InvalidEventReader);
        Assert.Equal(8, (int)SimBuildErrorCode.DuplicateOpHandler);
        Assert.Equal(9, (int)SimBuildErrorCode.MissingOpHandler);
        Assert.Equal(10, (int)SimBuildErrorCode.PayloadTooLarge);
        Assert.Equal(11, (int)SimBuildErrorCode.SystemReasonBelow64);
        Assert.Equal(12, (int)SimBuildErrorCode.InvalidKind);
    }
}
