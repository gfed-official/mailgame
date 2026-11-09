using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 005 AC-1 (ADR-0009 section 1 and errata 2026-10-16; ADR-0007 section 4): the <c>DeliverMatching</c> payload is
/// exactly 24 B in the ADR's field order, and the descriptor passes the shared descriptor checks in the Mail/Delivery block.
/// </summary>
public class DeliverMatchingOpTests
{
    [Fact]
    [Trait("Id", "TR-delivery-008")]
    public void Payload_Is24BytesPaddingFree_InTheAdrFieldOrder_AndTheDescriptorPassesTheSharedChecks()
    {
        // Act
        string[] fields = typeof(DeliverMatchingPayload).GetFields()
            .Select(f => $"{f.Name} {(int)Marshal.OffsetOf<DeliverMatchingPayload>(f.Name)}")
            .ToArray();

        // Assert
        Assert.Equal(24, DescriptorAssert.SizeOf(typeof(DeliverMatchingPayload)));
        Assert.Equal(24, DescriptorAssert.SumOfFieldSizes(typeof(DeliverMatchingPayload)));
        Assert.Equal(new[] { "MailboxId 0", "SelectedEntryId 4", "HoldStartTick 8", "Claim 12" }, fields);
        DescriptorAssert.Check(DeliveryDescriptors.DeliverMatching, OpKindBlock.MailDelivery);
        Assert.Equal(DeliveryDescriptors.DeliverMatchingCodecId, DeliveryDescriptors.DeliverMatching.WireCodecId);
    }
}
