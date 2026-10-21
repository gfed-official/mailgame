using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 013 performance note: <see cref="InteractionGate.Evaluate"/>, <see cref="InteractionGate.ContextCancelReason"/>,
/// the table lookup and the <see cref="CarrierStatus"/> builder allocate 0 B once warm (story 014 calls Evaluate per candidate per tick).
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class InteractionGateAllocationTests
{
    /// <summary>A warm loop of 1 000 Evaluate + ContextCancelReason calls (plus table lookups and builds) allocates 0 B.</summary>
    [Fact]
    [Trait("Id", "PCM-P11-2d")]
    public void WarmLoop_EvaluateAndContextCancelReason_AllocatesZeroBytes()
    {
        // Arrange
        var gate = new InteractionGate(new FrameworkReasons(InteractionGateTests.DryLandId, InteractionGateTests.ClosePanelId));
        AnchorDef[] anchors =
        {
            InteractionGateTests.Anchor(Verb.Deliver),
            InteractionGateTests.Anchor(Verb.Open, AnchorFlags.AllowWhileSwimming),
            InteractionGateTests.Anchor(Verb.Deconstruct, AnchorFlags.BuildModeOnly),
            InteractionGateTests.Anchor(Verb.Dismount, AnchorFlags.SeatOf),
        };
        CarrierStatus[] statuses =
        {
            InteractionGateTests.Status(),
            InteractionGateTests.Status(panel: true),
            InteractionGateTests.Status(swimming: true),
            InteractionGateTests.Status(BodyContext.Build),
            InteractionGateTests.Status(BodyContext.Mounted, seat: anchors[3].Ref),
            InteractionGateTests.Status(BodyContext.Dead, life: LifeState.Dead),
        };
        var record = new PlayerRecord { Life = LifeState.Alive };
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("1000 x (Evaluate + ContextCancelReason + table + build)", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                ref readonly AnchorDef a = ref anchors[i & 3];
                ref readonly CarrierStatus prev = ref statuses[i % 6];
                ref readonly CarrierStatus cur = ref statuses[(i + 1) % 6];
                GateResult r = gate.Evaluate(in a, in cur);
                sink += (int)r.Kind + r.Reason.Index;
                sink += (int)InteractionGate.ContextCancelReason(in prev, in cur);
                sink += (int)VerbContextTable.Cell((ContextVerb)(i & 7), (InputContext)(i % 5));
                CarrierStatus s = CarrierStatus.Build(in record, in record.Carrier, (i & 1) != 0, false, false, false, anchors[3].Ref, (ushort)i);
                sink += (int)s.ToContextFlags() + (InputContexts.RouterHolding(in s) ? 1 : 0);
            }
        });

        // Assert
        Assert.NotEqual(0, sink);
    }
}
