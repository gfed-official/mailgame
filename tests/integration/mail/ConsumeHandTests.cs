using System.Buffers.Binary;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 007 (ADR-0017 sections 5 and 9, ADR-0008, ADR-0009 erratum 2026-11-04): the hand path
/// <see cref="IMailApi.ConsumeHand"/> through the one consume routine, over the real Inventory and the 10-lot M-1a street, with an FX-W
/// recording wallet and an <see cref="IInventoryApi"/> wrapper that counts calls and can refuse one entry's removal (AC-11). Each test
/// runs its calls inside a real tick through a phase-12 probe. Joins the non-parallel collection for the advisory allocation report
/// (AC-7, see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class ConsumeHandTests
{
    private const int Right = 0;   // the destination every hand-over goes to (registry row 0)
    private const int Wrong = 1;   // the address of a wrong-house stack (registry row 1)

    // ---- AC-1 ----

    [Fact]
    [Trait("Id", "M-R6-1a")]
    [Trait("Id", "M-X-1c")]
    public void Ac1_A20LetterWrongHouseEntry_ConsumesExactlyTheLowestMailId_WithEventsInOrder()
    {
        // Arrange
        var rig = new HandRig();
        rig.Open();
        EntryRef e = default;
        rig.Run(() => e = rig.Give(0, "letter", Wrong, 20));
        Assert.True(rig.Inventory.TryReadEntry(e, out EntryView before));

        // Act
        HandConsumeSummary r = default;
        rig.Run(() => r = rig.Hand(0, e));

        // Assert: one unit, the oldest; its row retired, the other 19 untouched.
        Assert.Equal(new HandConsumeSummary(1, 1, MailRejectReason.None, false, -4), r);
        Assert.True(rig.Inventory.TryReadEntry(e, out EntryView after));
        Assert.Equal(19, after.Units);
        Assert.True(after.LowestMailId.Value > before.LowestMailId.Value);
        Assert.False(rig.Mail.Table.IsLive(before.LowestMailId));
        Assert.True(rig.Mail.Table.IsLive(after.LowestMailId));

        // Events in call order: Inventory's removal and seal (by the delivering player), MailConsumed, ComplaintRaised, DeliveryOutcome.
        EventEntry[] log = rig.Capture.Last;
        Assert.Equal(
            new[] { InvEventKinds.Removed, InvEventKinds.Version, MailEventKinds.MailConsumed, MailEventKinds.ComplaintRaised, MailEventKinds.DeliveryOutcome },
            log.Select(x => x.Kind).ToArray());
        Assert.Equal(rig.PlayerOf(0).Value, log[1].PayloadAs<InvVersionPayload>().ActorId);
        MailConsumedPayload consumed = log[2].PayloadAs<MailConsumedPayload>();
        Assert.Equal((before.LowestMailId.Value, -4L, (byte)EvalResult.Misdeliver), (consumed.MailId, consumed.Cents, consumed.Result));
        DeliveryOutcomePayload o = log[4].PayloadAs<DeliveryOutcomePayload>();
        Assert.Equal((byte)EvalResult.Misdeliver, o.Result);
        Assert.Equal((ushort)1, o.Units);
        Assert.Equal(-4L, o.Cents);
        Assert.Equal(rig.Mail.Registry.AddressOf(new DestinationOrdinal(Wrong)).Value, o.IntendedAddress);
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "M-R6-1b")]
    public void Ac2_FourMatchingEntries_GiveFourOutcomesInScanOrder_InOneTick_WithContiguousOutcomeSeq_AndNoMisdelivery()
    {
        // Arrange: four right-house entries of four kinds (one stack each).
        var rig = new HandRig();
        rig.Open();
        var entries = new EntryRef[4];
        string[] kinds = { "postcard", "letter", "small", "medium" };
        rig.Run(() =>
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                entries[i] = rig.Give(0, kinds[i], Right, 1);
            }
        });

        // Act
        HandConsumeSummary r = default;
        rig.Run(() => r = rig.Hand(0, entries));

        // Assert: 4 + 8 + 30 + 70 cents; the outcomes in scan order, numbered 1 to 4, in the consuming tick.
        Assert.Equal(new HandConsumeSummary(4, 4, MailRejectReason.None, false, 112), r);
        DeliveryOutcomePayload[] o = rig.Outcomes();
        Assert.Equal(kinds.Select(k => rig.Kind(k).Value).ToArray(), o.Select(p => p.Kind).ToArray());
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, o.Select(p => p.OutcomeSeq).ToArray());
        Assert.All(o, p => Assert.Equal((byte)EvalResult.Accept, p.Result));
        Assert.Equal((ushort)0, rig.Mail.MisdeliveriesThisShift);
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "M-R6-2")]
    [Trait("Id", "M-R9-2a")]
    public void Ac3_AClosedReject_ConsumesNothing_CallsNoInventoryWalletOrComplaint_RaisesNoOutcome_AndReportsClosed()
    {
        // Arrange: deliveries closed (the default state).
        var rig = new HandRig();
        EntryRef e = default;
        rig.Run(() => e = rig.Give(0, "letter", Right, 3));
        rig.Api.Calls = 0;
        long doorTokens = rig.Mail.Registry.TokensPm(new DestinationOrdinal(Right));

        // Act
        HandConsumeSummary r = default;
        rig.Run(() => r = rig.Hand(0, e));

        // Assert
        Assert.Equal(new HandConsumeSummary(0, 0, MailRejectReason.Closed, false, 0), r);
        Assert.Equal(0, rig.Api.Calls);
        Assert.Empty(rig.Wallet.CallTicks);
        Assert.Equal(doorTokens, rig.Mail.Registry.TokensPm(new DestinationOrdinal(Right)));
        Assert.Equal(0L, rig.Mail.Complaints.TeamLastTick);
        Assert.Empty(rig.Capture.Last);
        Assert.True(rig.Inventory.TryReadEntry(e, out EntryView v));
        Assert.Equal(3, v.Units);
    }

    // ---- AC-4 ----

    [Fact]
    [Trait("Id", "M-F8-1c")]
    [Trait("Id", "M-X-4")]
    [Trait("Id", "M-R9-2b")]
    public void Ac4_AtMinus496_AMisdeliverLandsOnMinus500_TheNextStopsAtTheFloor_WithOneRefusedDebit_NoComplaint_NoCount()
    {
        // Arrange
        var rig = new HandRig();
        rig.Open();
        EntryRef e = default;
        rig.Run(() => e = rig.Give(0, "letter", Wrong, 20));
        rig.Wallet.Balance = -496;
        var ticks = new List<uint>();

        // Act
        HandConsumeSummary first = default;
        HandConsumeSummary second = default;
        rig.Run(() => { ticks.Add(rig.Now); first = rig.Hand(0, e); });
        EventEntry[] firstLog = rig.Capture.Last;
        rig.Run(() => { ticks.Add(rig.Now); second = rig.Hand(0, e); });

        // Assert: exactly -500 is allowed; the next is refused with nothing consumed, no complaint and no count.
        Assert.Equal(new HandConsumeSummary(1, 1, MailRejectReason.None, false, -4), first);
        Assert.Equal(new HandConsumeSummary(0, 0, MailRejectReason.None, true, 0), second);
        Assert.Equal(-500L, rig.Wallet.Balance);
        Assert.Equal((2, 1, 0), (rig.Wallet.Debits, rig.Wallet.RefusedDebits, rig.Wallet.Credits));
        Assert.Contains(firstLog, x => x.Kind == MailEventKinds.ComplaintRaised);
        Assert.DoesNotContain(rig.Capture.Last, x => x.Kind == MailEventKinds.ComplaintRaised || x.Kind == MailEventKinds.MailConsumed);
        Assert.Equal((ushort)1, rig.Mail.MisdeliveriesThisShift);
        Assert.True(rig.Inventory.TryReadEntry(e, out EntryView v));
        Assert.Equal(19, v.Units);

        // One wallet call per unit attempted, each in its consuming tick.
        Assert.Equal(ticks, rig.Wallet.CallTicks);
    }

    // ---- AC-5 ----

    [Fact]
    [Trait("Id", "M-R16-1b")]
    [Trait("Id", "M-R16-1d")]
    public void Ac5_AFloorStopBroadcastsOncePerDoorAndPlayerPer30Ticks_AndTheWrongDeliveryCountIsPerOp()
    {
        // Arrange: two players at the floor with wrong-house stacks.
        var rig = new HandRig();
        rig.Join(1);
        rig.Open();
        EntryRef a = default;
        EntryRef b = default;
        rig.Run(() => { a = rig.Give(0, "letter", Wrong, 20); b = rig.Give(1, "letter", Wrong, 20); });
        rig.Wallet.Balance = -500;

        // Act + Assert: at T, player 0 twice and player 1 once -> two outcomes (one per player).
        rig.Run(() => { _ = rig.Hand(0, a); _ = rig.Hand(0, a); _ = rig.Hand(1, b); });
        DeliveryOutcomePayload[] atT = rig.Outcomes();
        Assert.Equal(new byte[] { 0, 1 }, atT.Select(p => p.Actor).ToArray());
        Assert.All(atT, p =>
        {
            Assert.Equal((ushort)0, p.Units);
            Assert.Equal(DeliveryOutcomePayload.FlagStoppedAtFloor, p.Flags);
            Assert.Equal((byte)DeliveryVia.Hand, p.Via);
            Assert.Equal((byte)EvalResult.Misdeliver, p.Result);
        });

        // T + 29: nothing; T + 30: one again.
        for (int i = 0; i < 28; i++)
        {
            rig.Run(() => { });
        }

        rig.Run(() => _ = rig.Hand(0, a));
        Assert.Empty(rig.Outcomes());
        rig.Run(() => _ = rig.Hand(0, a));
        Assert.Single(rig.Outcomes());

        // The wrong-delivery count goes up once per op that consumed a wrong-house unit; floor stops never count.
        Assert.Equal((ushort)0, rig.Mail.MisdeliveriesThisShift);
        rig.Wallet.Balance = 0;
        rig.Run(() => { _ = rig.Hand(0, a); _ = rig.Hand(1, b); });
        Assert.Equal((ushort)2, rig.Mail.MisdeliveriesThisShift);
    }

    // ---- AC-6 ----

    [Fact]
    [Trait("Id", "M-R6-4")]
    [Trait("Id", "M-R4-2c")]
    public void Ac6_TwoPlayersAtOneMailboxInOneTick_BothSucceedInCallOrder_AndTheGraceEndsAt90TicksAfterClose()
    {
        // Arrange
        var rig = new HandRig();
        rig.Join(1);
        rig.Open();
        EntryRef a = default;
        EntryRef b = default;
        EntryRef late1 = default;
        EntryRef late2 = default;
        rig.Run(() =>
        {
            a = rig.Give(0, "letter", Right, 1);
            b = rig.Give(1, "letter", Right, 1);
            late1 = rig.Give(0, "postcard", Right, 1);
            late2 = rig.Give(0, "small", Right, 1);
        });
        while (rig.Host.World.CurrentTick < 100)
        {
            rig.Run(() => { });
        }

        // Act
        HandConsumeSummary ra = default;
        HandConsumeSummary rb = default;
        HandConsumeSummary at90 = default;
        HandConsumeSummary at91 = default;
        rig.Run(() => { ra = rig.Hand(0, a); rb = rig.Hand(1, b); });
        DeliveryOutcomePayload[] pair = rig.Outcomes();
        rig.Run(() =>
        {
            uint now = rig.Now;
            rig.Mail.Registry.Deliveries.Set(false, now - 90, DeliveriesState.DefaultUnlockedMask);
            at90 = rig.Hand(0, now - 91, late1);
            rig.Mail.Registry.Deliveries.Set(false, now - 91, DeliveriesState.DefaultUnlockedMask);
            at91 = rig.Hand(0, now - 92, late2);
        });

        // Assert
        Assert.Equal(new HandConsumeSummary(1, 1, MailRejectReason.None, false, 8), ra);
        Assert.Equal(ra, rb);
        Assert.Equal(new byte[] { 0, 1 }, pair.Select(p => p.Actor).ToArray());
        Assert.Equal(new HandConsumeSummary(1, 1, MailRejectReason.None, false, 4), at90);
        Assert.Equal(new HandConsumeSummary(0, 0, MailRejectReason.Closed, false, 0), at91);
    }

    // ---- AC-7 ----

    [Fact]
    [Trait("Id", "M-PERF-1b")]
    public void Ac7_A40PostcardAccept_Makes40ConsumeUnitAnd40CreditCalls_AndOneOutcome_AllocationIsReported()
    {
        // Arrange: six full 40-postcard stacks to the right house (fixture-built; shift unlocks don't apply).
        var rig = new HandRig();
        rig.Open();
        var stacks = new EntryRef[6];
        rig.Run(() =>
        {
            for (int i = 0; i < stacks.Length; i++)
            {
                stacks[i] = rig.Give(0, "postcard", Right, 40);
            }
        });
        rig.Api.ConsumeUnitCalls = 0;

        // Act
        HandConsumeSummary r = default;
        rig.Run(() => r = rig.Hand(0, stacks[0]));
        DeliveryOutcomePayload[] o = rig.Outcomes();

        // Assert
        Assert.Equal(new HandConsumeSummary(1, 40, MailRejectReason.None, false, 160), r);
        Assert.Equal(40, rig.Api.ConsumeUnitCalls);
        Assert.Equal(40, rig.Wallet.Credits);
        Assert.Equal((ushort)40, Assert.Single(o).Units);

        // 0 B after warm-up: advisory until M0 (ADR-0017 erratum E2), reported, not asserted.
        long bytes = 0;
        rig.Run(() =>
        {
            int k = 1;
            EntityId dest = rig.Dest(Right);
            PlayerSlot slot = PlayerSlot.FromValue(0);
            uint now = rig.Now;
            (bytes, _) = AllocAssert.Measure(() => _ = rig.Mail.ConsumeHand(dest, new ReadOnlySpan<EntryRef>(stacks, k++, 1), slot, now));
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"MD-007 advisory: a 40-postcard hand delivery allocated {bytes} B (0 B gate blocking from M0, E2).");
    }

    // ---- AC-8, AC-9, AC-10 ----

    [Fact]
    [Trait("Id", "M-F9-3c")]
    public void Ac8_AnAcceptOfOnlyDeadLetters_MakesNoLateComplaint()
    {
        // Arrange: letters due shift 1, delivered in shift 3 (dead at the default limit of 2).
        var rig = new HandRig();
        rig.Open();
        EntryRef e = default;
        rig.Run(() => e = rig.Give(0, "letter", Right, 2));
        rig.Mail.CurrentShift = 3;

        // Act
        HandConsumeSummary r = default;
        rig.Run(() => r = rig.Hand(0, e));

        // Assert: paid 0, two dead units, no complaint and the late window untouched.
        Assert.Equal(new HandConsumeSummary(1, 2, MailRejectReason.None, false, 0), r);
        DeliveryOutcomePayload o = Assert.Single(rig.Outcomes());
        Assert.Equal(((ushort)0, (ushort)2, (ushort)0), (o.LateUnits, o.DeadUnits, o.ComplaintPermille));
        Assert.DoesNotContain(rig.Capture.Last, x => x.Kind == MailEventKinds.ComplaintRaised);
        Assert.False(rig.Mail.Complaints.LateCharged);
    }

    [Fact]
    [Trait("Id", "M-F9-3d")]
    public void Ac9_AMisdeliveryOfALateLetter_MakesNoLateComplaint()
    {
        // Arrange: a late letter (due shift 1, delivered in shift 2) to the wrong house.
        var rig = new HandRig();
        rig.Open();
        EntryRef e = default;
        rig.Run(() => e = rig.Give(0, "letter", Wrong, 1));
        rig.Mail.CurrentShift = 2;

        // Act
        rig.Run(() => _ = rig.Hand(0, e));

        // Assert: only the misdelivery complaint.
        ComplaintRaisedPayload c = Assert.Single(rig.Complaints());
        Assert.Equal((byte)ComplaintSource.Misdelivery, c.Source);
        Assert.Equal((ushort)1, Assert.Single(rig.Outcomes()).LateUnits);
        Assert.False(rig.Mail.Complaints.LateCharged);
    }

    [Fact]
    [Trait("Id", "M-F9-3e")]
    public void Ac10_TwoLateEntriesInOneHold_CountOnce()
    {
        // Arrange: two late right-house entries.
        var rig = new HandRig();
        rig.Open();
        EntryRef a = default;
        EntryRef b = default;
        rig.Run(() => { a = rig.Give(0, "letter", Right, 1); b = rig.Give(0, "postcard", Right, 1); });
        rig.Mail.CurrentShift = 2;

        // Act
        HandConsumeSummary r = default;
        rig.Run(() => r = rig.Hand(0, a, b));

        // Assert: late pay (8 and 4 cents at 500 permille), one late complaint of 2,000 on the first outcome.
        Assert.Equal(new HandConsumeSummary(2, 2, MailRejectReason.None, false, 6), r);
        ComplaintRaisedPayload c = Assert.Single(rig.Complaints());
        Assert.Equal(((byte)ComplaintSource.Late, (ushort)2_000), (c.Source, c.AmountPermille));
        Assert.Equal(new ushort[] { 2_000, 0 }, rig.Outcomes().Select(p => p.ComplaintPermille).ToArray());
    }

    // ---- AC-11 ----

    [Fact]
    [Trait("Id", "TD-208-12")]
    public void Ac11_ARefusedRemovalMidHandOver_AssertsCode89_OrStopsKeepingWhatHappened_AndWritesOneCheckRecord()
    {
        // Arrange: a 2-letter Accept entry, a wrong-house entry whose removal the test inventory refuses, then another Accept entry.
        var rig = new HandRig();
        rig.Open();
        EntryRef accept = default;
        EntryRef wrong = default;
        EntryRef after = default;
        rig.Run(() =>
        {
            accept = rig.Give(0, "letter", Right, 2);
            wrong = rig.Give(0, "letter", Wrong, 1);
            after = rig.Give(0, "postcard", Right, 1);
        });
        rig.Api.Refuse = wrong;
        Assert.True(rig.Inventory.TryReadEntry(wrong, out EntryView refused));
        int records = rig.Host.Services.CheckRing.Count;

        // Act
        HandConsumeSummary r = default;
        SimAssertException? thrown = null;
        rig.Run(() =>
        {
            try
            {
                r = rig.Hand(0, accept, wrong, after);
            }
            catch (SimAssertException e)
            {
                thrown = e;
            }
        });

        // Assert: one always-on record in every configuration (destination, refused unit).
        Assert.Equal(records + 1, rig.Host.Services.CheckRing.Count);
        SimCheckRecord record = rig.Host.Services.CheckRing[records];
        Assert.Equal(CheckCode.MailConsumeUnitRefused, record.Check);
        Assert.Equal((rig.Dest(Right).Value, (long)refused.LowestMailId.Value), ((uint)record.A, record.B));

        bool assertsOn = PerformativeMail.Sim.Sim.AssertsEnabled;
        if (assertsOn)
        {
            Assert.Equal(SimAssertCode.MailConsumeUnitRefused, Assert.IsType<SimAssertException>(thrown).Code);
            return;
        }

        // Asserts off: the hand-over stops; the two letters keep their notes and outcome, the penalty stays, nothing else is used.
        Assert.Null(thrown);
        Assert.Equal(new HandConsumeSummary(1, 2, MailRejectReason.None, false, 16 - 4), r);
        Assert.Equal(2, rig.Capture.Last.Count(x => x.Kind == MailEventKinds.MailConsumed));
        DeliveryOutcomePayload o = Assert.Single(rig.Outcomes());
        Assert.Equal((rig.Kind("letter").Value, (ushort)2, 16L), (o.Kind, o.Units, o.Cents));
        Assert.Equal((1, 2, 12L), (rig.Wallet.Debits, rig.Wallet.Credits, rig.Wallet.Balance));
        Assert.True(rig.Inventory.TryReadEntry(wrong, out EntryView w) && w.Units == 1);
        Assert.True(rig.Inventory.TryReadEntry(after, out EntryView p) && p.Units == 1);
        Assert.Empty(rig.Complaints());
        Assert.Equal((ushort)0, rig.Mail.MisdeliveriesThisShift);
    }

    // ---- AC-12 ----

    [Fact]
    [Trait("Id", "TD-208-10")]
    public void Ac12_OutcomeSeqFrom1_CountSaturates_TheDumpAppendsTheOutcomeState_AndRoundTripsByteIdentical()
    {
        // Arrange: a misdelivery (outcome 1), a floor stop (outcome 2), then a misdelivery with the count at 65,535 (outcome 3).
        var rig = new HandRig();
        rig.Open();
        EntryRef e = default;
        rig.Run(() => e = rig.Give(0, "letter", Wrong, 5));
        uint floorTick = 0;
        uint lastTick = 0;

        // Act
        rig.Run(() => _ = rig.Hand(0, e));
        rig.Wallet.Balance = -500;
        rig.Run(() => { floorTick = rig.Now; _ = rig.Hand(0, e); });
        rig.Wallet.Balance = 0;
        rig.Mail.MisdeliveriesThisShift = ushort.MaxValue;
        rig.Run(() => { lastTick = rig.Now; _ = rig.Hand(0, e); });

        // Assert: the numbering and the saturating count.
        Assert.Equal(3u, Assert.Single(rig.Outcomes()).OutcomeSeq);
        Assert.Equal(ushort.MaxValue, rig.Mail.MisdeliveriesThisShift);

        // The Mail section ends with the team complaint state (25 B), then outcomeSeq u32, lastZeroFloorTick i64 x lots x 8, the count u16.
        int lots = rig.Mail.Registry.Count;
        byte[] section = MailSection(rig.Host.World);
        int blockBytes = 4 + (lots * 8 * 8) + 2;
        // Story 009's deliveries state (17 B with openSinceTick) follows the outcome block, then story 013's shift spawner (53 B) ends the section.
        const int DeliveriesBytes = 17 + 53;
        ReadOnlySpan<byte> block = section.AsSpan(section.Length - DeliveriesBytes - blockBytes, blockBytes);
        ReadOnlySpan<byte> team = section.AsSpan(section.Length - DeliveriesBytes - blockBytes - 25, 25);
        Assert.Equal((long)lastTick, BinaryPrimitives.ReadInt64LittleEndian(team.Slice(8)));   // teamLastTick
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(block));
        for (int i = 0; i < lots * 8; i++)
        {
            long expected = i == (Right * 8) + 0 ? floorTick : -1L;
            Assert.Equal(expected, BinaryPrimitives.ReadInt64LittleEndian(block.Slice(4 + (i * 8))));
        }

        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(blockBytes - 2)));

        // CurrentShift is not dumped.
        rig.Mail.CurrentShift = 5;
        Assert.Equal(section, MailSection(rig.Host.World));

        // Dump -> load -> dump: a fresh world loaded with the block dumps it byte-identical and numbers on from it.
        var fresh = new HandRig();
        fresh.Mail.Consumer.ReadCanonical(block);
        byte[] reloaded = MailSection(fresh.Host.World);
        Assert.Equal(block.ToArray(), reloaded.AsSpan(reloaded.Length - DeliveriesBytes - blockBytes, blockBytes).ToArray());
        fresh.Open();
        EntryRef f = default;
        fresh.Run(() => f = fresh.Give(0, "letter", Right, 1));
        fresh.Run(() => _ = fresh.Hand(0, f));
        Assert.Equal(4u, Assert.Single(fresh.Outcomes()).OutcomeSeq);
    }

    private static byte[] MailSection(SimWorld world)
    {
        byte[] dump = DumpHash.Bytes(world);
        int pos = 6; // "PMSD" + format version
        while (BinaryPrimitives.ReadUInt16LittleEndian(dump.AsSpan(pos)) != SectionIds.Mail)
        {
            pos += 6 + (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(pos + 2));
        }

        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(pos + 2));
        return dump.AsSpan(pos + 6, length).ToArray();
    }

    /// <summary>
    /// Inventory and Mail composed over the M-1a street with a phase-12 probe, player 0 joined; Mail gets the FX-W wallet and the
    /// counting inventory wrapper. Stacks are built the ledger's way: Mail row, Intake spawn, take, insert into the player's hotbar.
    /// </summary>
    private sealed class HandRig
    {
        private static readonly ContentSet _content = BuiltInContent.CreateM1a();
        private Action? _next;
        private uint _req = 1;

        public HandRig()
        {
            MailSystem? mail = null;
            InventorySystem? inventory = null;
            RecordingWallet? wallet = null;
            TestInventory? api = null;
            var probe = new ProbeSystem(() => _next?.Invoke());
            SimProfile profile = SimProfile.ForTest(
                "md007",
                null,
                new SimSystemEntry(SystemId.GridInventory, ctx => inventory = new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
                new SimSystemEntry(SystemId.MailDestinations, ctx =>
                {
                    api = new TestInventory(ctx.Api<IInventoryApi>(SystemId.GridInventory));
                    wallet = new RecordingWallet(ctx.Tick);
                    return mail = new MailSystem(
                        ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, api, wallet, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots);
                }),
                new SimSystemEntry(SystemId.RunStructure, _ => probe));
            Capture = new CaptureSink();
            Host = SimComposition.Build(new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance, EventSink = Capture });
            Mail = mail!;
            Inventory = inventory!;
            Wallet = wallet!;
            Api = api!;
            Join(0);
        }

        public SimHost Host { get; }

        public MailSystem Mail { get; }

        public InventorySystem Inventory { get; }

        public RecordingWallet Wallet { get; }

        public TestInventory Api { get; }

        public CaptureSink Capture { get; }

        public uint Now => Host.World.CurrentTick;

        public void Run(Action body)
        {
            _next = body;
            try
            {
                Host.World.Tick();
            }
            finally
            {
                _next = null;
            }
        }

        public void Join(byte slot)
        {
            var op = new Op { Kind = OpKind.PlayerJoin, ReqId = _req++ };
            op.SetPayload(new PlayerJoinPayload { Slot = slot });
            Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
            Run(() => { });
            Assert.False(PlayerOf(slot).IsNone);
        }

        public void Open() => Mail.Registry.Deliveries.Set(true, 0, DeliveriesState.DefaultUnlockedMask);

        public EntityId PlayerOf(byte slot) => Host.Services.Slots.PlayerOf(PlayerSlot.FromValue(slot));

        public EntityId Dest(int ordinal) => Mail.Registry.EntityIdOf(new DestinationOrdinal((ushort)ordinal));

        public DefIndex Kind(string id)
        {
            ReadOnlySpan<MailKindDef> kinds = _content.MailKinds.AsSpan();
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i].Id == id)
                {
                    return DefIndex.FromValue((ushort)i);
                }
            }

            throw new ArgumentException(id);
        }

        /// <summary>In a tick: a new stack of <paramref name="count"/> units of <paramref name="kind"/> addressed to registry row <paramref name="ordinal"/>, in the slot's hotbar.</summary>
        public EntryRef Give(byte slot, string kind, int ordinal, int count)
        {
            DefIndex k = Kind(kind);
            ushort value = (ushort)_content.MailKinds[k].BaseValueCents;
            AddressId address = Mail.Registry.AddressOf(new DestinationOrdinal((ushort)ordinal));
            ContainerId hotbar = Inventory.HotbarOf(PlayerSlot.FromValue(slot));
            ActorId actor = ActorId.FromEntity(PlayerOf(slot));
            EntryId entry = default;
            for (int i = 0; i < count; i++)
            {
                MailId id = Mail.Table.Create(k, address, value, (byte)Mail.CurrentShift, MailFlags.None);
                Assert.Equal(1, Inventory.Spawn(Inventory.IntakeContainer, k, address.Value, 0, new[] { id }, MergePolicy.NoMerge).Placed);
                Assert.True(Inventory.TakeUnit(Inventory.IntakeContainer, Selector.Fifo, ActorId.System, out UnitRef u));
                Assert.True(Inventory.TryInsert(hotbar, in u, actor));
                if (i == 0)
                {
                    entry = EntryId.Real(Inventory.Entries.LastId);   // the first insert made the stack; the rest merged into it
                }
            }

            var e = new EntryRef(hotbar, entry);
            Assert.True(Inventory.TryReadEntry(e, out EntryView v));
            Assert.Equal(count, v.Units);
            return e;
        }

        public HandConsumeSummary Hand(byte slot, params EntryRef[] entries) => Hand(slot, Now, entries);

        public HandConsumeSummary Hand(byte slot, uint holdStartTick, params EntryRef[] entries) =>
            Mail.ConsumeHand(Dest(Right), entries, PlayerSlot.FromValue(slot), holdStartTick);

        public DeliveryOutcomePayload[] Outcomes() =>
            Capture.Last.Where(x => x.Kind == MailEventKinds.DeliveryOutcome).Select(x => x.PayloadAs<DeliveryOutcomePayload>()).ToArray();

        public ComplaintRaisedPayload[] Complaints() =>
            Capture.Last.Where(x => x.Kind == MailEventKinds.ComplaintRaised).Select(x => x.PayloadAs<ComplaintRaisedPayload>()).ToArray();
    }

    /// <summary>FX-W: a stub wallet that records its calls (with the tick), settable, <c>TryDebitPenalty</c> true iff <c>wallet − cents ≥ floor</c>.</summary>
    private sealed class RecordingWallet : IWalletGate
    {
        private readonly ITickReader _tick;

        public RecordingWallet(ITickReader tick)
        {
            _tick = tick;
        }

        public long Balance { get; set; }

        public int Debits { get; private set; }

        public int RefusedDebits { get; private set; }

        public int Credits { get; private set; }

        public List<uint> CallTicks { get; } = new(4096);

        public bool TryDebitPenalty(Cents amount, Cents floor)
        {
            CallTicks.Add(_tick.CurrentTick);
            Debits++;
            if (Balance - amount.Value < floor.Value)
            {
                RefusedDebits++;
                return false;
            }

            Balance -= amount.Value;
            return true;
        }

        public void Credit(Cents amount, CreditReason reason)
        {
            CallTicks.Add(_tick.CurrentTick);
            Credits++;
            Balance += amount.Value;
        }
    }

    /// <summary>The real Inventory behind a wrapper that counts Mail's calls and refuses <see cref="Refuse"/>'s removal (AC-11).</summary>
    private sealed class TestInventory : IInventoryApi
    {
        private readonly IInventoryApi _inner;

        public TestInventory(IInventoryApi inner)
        {
            _inner = inner;
        }

        public int Calls { get; set; }

        public int ConsumeUnitCalls { get; set; }

        public EntryRef? Refuse { get; set; }

        public int BacklogUnits => _inner.BacklogUnits;

        public int BacklogEntries => _inner.BacklogEntries;

        public ContainerId IntakeContainer => _inner.IntakeContainer;

        public bool ConsumeUnit(EntryRef entry, ConsumeReason reason, ActorId actor)
        {
            Calls++;
            ConsumeUnitCalls++;
            return entry != Refuse && _inner.ConsumeUnit(entry, reason, actor);
        }

        public bool TryReadEntry(EntryRef entry, out EntryView v)
        {
            Calls++;
            return _inner.TryReadEntry(entry, out v);
        }

        public SpawnResult Spawn(ContainerId c, DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids, MergePolicy p)
        {
            Calls++;
            return _inner.Spawn(c, mailKind, addressId, flags, ids, p);
        }

        public bool PeekOutfeed(ContainerId c, Selector s, out UnitRef u)
        {
            Calls++;
            return _inner.PeekOutfeed(c, s, out u);
        }

        public bool TakeUnit(ContainerId c, Selector s, ActorId actor, out UnitRef u)
        {
            Calls++;
            return _inner.TakeUnit(c, s, actor, out u);
        }

        public EntityId CreateWorldItem(EntryId entry, Vec3Mm posMm, WorldItemOrigin origin, bool onWater, uint createdTick)
        {
            Calls++;
            return _inner.CreateWorldItem(entry, posMm, origin, onWater, createdTick);
        }

        public DetachResult DetachForThrow(PlayerSlot slot, EntryId entryId, byte units)
        {
            Calls++;
            return _inner.DetachForThrow(slot, entryId, units);
        }

        public CatchResult TryCatch(PlayerSlot slot, in FlightPayload flight)
        {
            Calls++;
            return _inner.TryCatch(slot, in flight);
        }

        public bool TryInsert(ContainerId c, in UnitRef u, ActorId actor)
        {
            Calls++;
            return _inner.TryInsert(c, in u, actor);
        }

        public int CountItem(ReadOnlySpan<ContainerId> cs, DefIndex itemId)
        {
            Calls++;
            return _inner.CountItem(cs, itemId);
        }

        public short WeightPoints(PlayerSlot slot)
        {
            Calls++;
            return _inner.WeightPoints(slot);
        }

        public int SpeedPermille(PlayerSlot slot)
        {
            Calls++;
            return _inner.SpeedPermille(slot);
        }

        public bool AppendBacklog(in BacklogEntry e)
        {
            Calls++;
            return _inner.AppendBacklog(in e);
        }

        public IPanelView HostPanelView(PlayerSlot slot)
        {
            Calls++;
            return _inner.HostPanelView(slot);
        }

        public DetachCheck CheckDetachForThrow(PlayerSlot slot, uint rawEntryId, byte units, out byte hotbarIndex)
        {
            Calls++;
            return _inner.CheckDetachForThrow(slot, rawEntryId, units, out hotbarIndex);
        }
    }

    private sealed class ProbeSystem : ISimSystem, ISimPhase
    {
        private readonly Action _body;

        public ProbeSystem(Action body)
        {
            _body = body;
        }

        public void Bind(SimWorldBuilder builder) => builder.Bind(PhaseId.RunStep, this);

        public void Run(in TickContext ctx) => _body();
    }
}
