using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 004 (GDD MD3, MD4, MD5; TR-delivery-006, TR-delivery-008, TR-delivery-009; ADR-0014 sections 3 and 4, ADR-0016
/// sections 2 and 4): the press judged on this tick's 4b state, the settle refusal, the latch and the one request, through a ClientLayer
/// (<see cref="DeliverClientRig"/>). Greybox scope drops Toggle mode's arming settle (Mail Lead regroup 2026-11-08).
/// </summary>
public class DeliverPressLatchTests
{
    private const int HoldTicks = 12;

    [Fact]
    [Trait("Id", "M-MD3-4")]
    public void Press_OnTheTickASnapBackFlipsMatchToMisdeliverOne_IsJudgedOnTheNewBranch_AndStartsNoHold()
    {
        // Arrange: a settled MATCH with a wrong-house piece selected.
        var rig = new DeliverClientRig();
        PanelEntryFacts home = rig.Letter(rig.HomeAddress);
        rig.Panel.Inventory.Add(home);
        rig.Panel.Hotbar[2] = rig.Letter(rig.FarAddress);
        rig.Source.SelectedSlot = 2;
        rig.FocusOn(rig.Home);
        Assert.Equal(DeliverBranchKind.Match, rig.State.Branch.Kind);

        // Act: the reply's snap-back removes the match in the same tick as the press.
        rig.Panel.Inventory.Clear();
        rig.PressEdge(rig.Home);
        rig.Tick();

        // Assert
        Assert.Equal(DeliverBranchKind.MisdeliverOne, rig.State.Branch.Kind);
        Assert.Equal(InteractState.Idle, rig.Interaction.State);
        Assert.Equal(CancelReason.Unavailable, rig.Interaction.LastCancel);
        Assert.Equal(DeliverPromptKeys.CancelSettle, rig.Interaction.LastCancelReason);
        Assert.Equal(0, rig.Requests.Attempts);
    }

    [Theory]
    [Trait("Id", "M-MD4-1")]
    [InlineData("a-c: wrong house first shown at T", 0, 6)]
    [InlineData("d: MATCH displayed 0 ticks", 0, 0)]
    [InlineData("A at T, match at T+2, A at T+3", 3, 9)]
    [InlineData("A at T, match at T+2, A at T+3, B selected at T+4", 3, 10)]
    [InlineData("floorRefusal changes at T+4", 0, 6)]
    [InlineData("cents change at T+4", 0, 6)]
    public void Press_StartsAHoldOnlyFromTheSettledTick_AndEarlierPressesReportSettle(string script, int firstWrong, int earliest)
    {
        // Every press from the last wrong-house display up to the earliest hold.
        for (int press = firstWrong; press <= earliest; press++)
        {
            // Arrange
            var rig = new DeliverClientRig();
            rig.FocusOn(rig.Home);

            // Act
            for (int k = 0; k <= press; k++)
            {
                Script(rig, script, k);
                if (k == press)
                {
                    rig.PressEdge(rig.Home);
                }

                rig.Tick();
            }

            // Assert
            if (press < earliest)
            {
                Assert.Equal(InteractState.Idle, rig.Interaction.State);
                Assert.Equal(DeliverPromptKeys.CancelSettle, rig.Interaction.LastCancelReason);
            }
            else
            {
                Assert.Equal(InteractState.Holding, rig.Interaction.State);
            }
        }
    }

    [Theory]
    [Trait("Id", "M-MD5-1")]
    [Trait("Id", "M-MD6-2c")]
    [InlineData("MATCH")]
    [InlineData("MISDELIVER_ONE")]
    [InlineData("MATCH, #17 closes at tick 6 (closing grace)")]
    public void CompletedHold_SendsOneDeliverMatching_WithTheLatchedEntryHostStartAndClaim(string row)
    {
        // Arrange: the host estimate runs 100 ticks ahead of the client tick.
        var rig = new DeliverClientRig(offset: 100);
        bool misdeliver = row == "MISDELIVER_ONE";
        PanelEntryFacts wrong = rig.Letter(rig.FarAddress);
        rig.Panel.Hotbar[2] = wrong;
        rig.Source.SelectedSlot = 2;
        if (!misdeliver)
        {
            rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
        }

        rig.FocusOn(rig.Home);
        for (int i = 0; i < 6; i++)
        {
            rig.Tick();
        }

        // Act
        rig.PressEdge(rig.Home);
        uint pressTick = rig.Tick();
        uint pressEstimate = rig.Clock.EstimatedHostTick;
        for (int k = 1; k <= HoldTicks; k++)
        {
            if (k == 6 && row.Contains("closes", StringComparison.Ordinal))
            {
                rig.SetDeliveries(open: false, unlockedMask: 1UL, closeTick: rig.Clock.EstimatedHostTick);
            }

            rig.Tick();
        }

        // Assert
        Assert.Equal(1, rig.Requests.Sent);
        Assert.Equal(InteractState.Complete, rig.Interaction.State);
        Op op = rig.Requests.Last;
        DeliverMatchingPayload p = op.PayloadAs<DeliverMatchingPayload>();
        Assert.Equal(DeliveryOpKinds.DeliverMatching, op.Kind);
        Assert.Equal(rig.Home.Entity.Value, p.MailboxId);
        Assert.Equal(misdeliver ? wrong.EntryId.Value : 0u, p.SelectedEntryId);
        Assert.Equal(pressTick + 100u, p.HoldStartTick);
        Assert.Equal(rig.Clock.EstimatedHostTick - (uint)HoldTicks, p.HoldStartTick);
        Assert.Equal(pressEstimate, p.HoldStartTick);
        Assert.Equal(new ClaimedPosition(FocusRig.Feet.X, FocusRig.Feet.Y, FocusRig.Feet.Z), p.Claim);
    }

    // The M-MD4-1 scripts, applied before tick T + k; A and B are wrong-house pieces in slots 2 and 3.
    private static void Script(DeliverClientRig rig, string script, int k)
    {
        if (script.StartsWith("d:", StringComparison.Ordinal))
        {
            if (k == 0)
            {
                rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
            }

            return;
        }

        if (k == 0)
        {
            rig.Panel.Hotbar[2] = rig.Letter(rig.FarAddress);
            rig.Panel.Hotbar[3] = rig.Letter(rig.FarAddress);
            rig.Source.SelectedSlot = 2;
        }

        bool flips = script.Contains("match at T+2", StringComparison.Ordinal);
        if (flips && k == 2)
        {
            rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
        }
        else if (flips && k == 3)
        {
            rig.Panel.Inventory.Clear();
        }
        else if (k == 4 && script.Contains("B selected", StringComparison.Ordinal))
        {
            rig.Source.SelectedSlot = 3;
        }
        else if (k == 4 && script.StartsWith("floorRefusal", StringComparison.Ordinal))
        {
            rig.SetWallet(-498);
        }
        else if (k == 4 && script.StartsWith("cents", StringComparison.Ordinal))
        {
            rig.Panel.Hotbar[2] = DeliverClientRig.Restyle(in rig.Panel.Hotbar[2], 0, 40);
        }
    }
}
