using System.Buffers;
using System.Buffers.Binary;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 006 (GDD F9, R9; TR-mail-010, TR-mail-012; ADR-0017 sections 7 and 9): the lazy door and team
/// <see cref="ComplaintBuckets"/>, the team-wide late window, and <see cref="MailEventKinds.ComplaintRaised"/> through
/// <see cref="MailSystem.RaiseComplaint"/>. Letters raw 5,000 permille; the shipped M-1a balance (cap 20,000, window 900 ticks,
/// late 2,000, team cap 40,000 before playerScale) over the 10-lot M-1a street.
/// </summary>
public class ComplaintBucketTests
{
    private const int Letter = 5_000;
    private const int TwentyLetters = 20 * Letter;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();
    private static readonly IStatSheet _plain = StatSheet.Build(_content, new StatSources([]));
    private static readonly IStatSheet _fragile = StatSheet.Build(_content, new StatSources([
        new Modifier(SourceKind.Perk, DefIndex.FromValue(0), 0, PlayerSlot.None, Stat.MisdeliveryComplaintMultPm, StatSheet.NoQualifier, ModifierOp.Mul, 500, ModifierLifetime.Run)]));

    private sealed class Rig
    {
        private readonly long[] _block = new long[SimCounterSink.RequiredLength];

        public Rig(int playerScalePm = 1000)
        {
            var b = new WorldTablesBuilder();
            M1aStreet.Author(b);
            Registry = new DestinationRegistry(b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm), _content);
            Buckets = new ComplaintBuckets(Registry, _content.Mail, playerScalePm, Counters);
        }

        public SimCounters Counters { get; } = new();

        public DestinationRegistry Registry { get; }

        public ComplaintBuckets Buckets { get; }

        public int Charge(int door, long now, int rawPm, IStatSheet? stats = null)
        {
            var sink = new SimCounterSink(_block);
            return Buckets.ChargeMisdelivery(new DestinationOrdinal((ushort)door), now, rawPm, stats ?? _plain, ref sink);
        }

        public long DoorTokens(int door) => Registry.TokensPm(new DestinationOrdinal((ushort)door));
    }

    // ---- AC-1 ----

    [Fact]
    [Trait("Id", "M-F9-1")]
    [Trait("Id", "M-R9-1")]
    [Trait("Id", "M-R9-2c")]
    public void Ac1_FreshBuckets_CapEachOpAndDoorAt20000_RefillLinearlyOver900Ticks_AndTheMultiplierAppliesAfterTheFullDebit()
    {
        // a: one 20-letter op -> 20,000.
        Assert.Equal(20_000, new Rig().Charge(0, 0, TwentyLetters));

        // b: six one-letter ops at six doors -> 5,000 each.
        var six = new Rig();
        Assert.All(Enumerable.Range(0, 6).Select(d => six.Charge(d, 0, Letter)), v => Assert.Equal(5_000, v));

        // c: two 20-letter ops at one door in one tick -> 20,000 then 0.
        var two = new Rig();
        Assert.Equal(new[] { 20_000, 0 }, new[] { two.Charge(0, 7, TwentyLetters), two.Charge(0, 7, TwentyLetters) });

        // d: a 40-postcard op (raw 120,000) -> 20,000.
        Assert.Equal(20_000, new Rig().Charge(0, 0, 120_000));

        // e: emptied at T, T + 450 allows 10,000 and T + 900 allows 20,000; 200,000 idle ticks fill it exactly, no overflow.
        const long T = 1_000;
        var half = new Rig();
        Assert.Equal(20_000, half.Charge(0, T, TwentyLetters));
        Assert.Equal(10_000, half.Charge(0, T + 450, TwentyLetters));
        var full = new Rig();
        Assert.Equal(20_000, full.Charge(0, T, TwentyLetters));
        Assert.Equal(20_000, full.Charge(0, T + 900, TwentyLetters));
        var idle = new Rig();
        Assert.Equal(20_000, idle.Charge(0, T, TwentyLetters));
        Assert.Equal(0, idle.Charge(0, T + 200_000, 0));
        Assert.Equal(20_000L * 900, idle.DoorTokens(0));

        // f: one letter per 900 ticks -> 5,000 each.
        var paced = new Rig();
        Assert.All(Enumerable.Range(0, 10).Select(i => paced.Charge(0, 900L * i, Letter)), v => Assert.Equal(5_000, v));

        // a, g: MisdeliveryComplaintMult 500 raises 10,000, but the door (now empty) and the team bucket (40,000 -> 20,000) are
        // debited the full 20,000 x 900.
        var fragile = new Rig();
        Assert.Equal(10_000, fragile.Charge(0, 0, TwentyLetters, _fragile));
        Assert.Equal(0L, fragile.DoorTokens(0));
        Assert.Equal(20_000L * 900, fragile.Buckets.TeamTokensPm);
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "M-F9-2")]
    public void Ac2_OneLetterEvery15TicksAtOneDoorFor1800Ticks_Sums59666_AndTheFirst900TicksSum39666()
    {
        // Arrange: n = 1, fresh buckets.
        var rig = new Rig();
        long total = 0;
        long first = 0;

        // Act: ticks 0, 15, ..., 1,785.
        for (long now = 0; now < 1_800; now += 15)
        {
            int v = rig.Charge(0, now, Letter);
            total += v;
            first += now < 900 ? v : 0;
        }

        // Assert
        Assert.InRange(total, 59_666 - 2, 59_666 + 2);
        Assert.InRange(first, 39_666 - 2, 39_666 + 2);
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "M-F9-3")]
    public void Ac3_ChargeLate_Raises2000_ThenNothingUntilT900_TeamWide_AndGracePeriodRaisesNothingAndKeepsTheWindow()
    {
        // Arrange
        const long T = 5_000;
        var rig = new Rig();

        // Act / Assert: +2,000 at T and lastLateTick = T; the window has no door, so T + 899 -> 0 and T + 900 -> +2,000.
        Assert.Equal(2_000, rig.Buckets.ChargeLate(T, graceSuppresses: false));
        Assert.Equal(T, rig.Buckets.LastLateTick);
        Assert.Equal(0, rig.Buckets.ChargeLate(T + 899, graceSuppresses: false));
        Assert.Equal(2_000, rig.Buckets.ChargeLate(T + 900, graceSuppresses: false));

        // Grace Period -> 0 and lastLateTick not set, even past the window; the next ungraced call still charges.
        Assert.Equal(0, rig.Buckets.ChargeLate(T + 1_800, graceSuppresses: true));
        Assert.Equal(T + 900, rig.Buckets.LastLateTick);
        var graced = new Rig();
        Assert.Equal(0, graced.Buckets.ChargeLate(T, graceSuppresses: true));
        Assert.False(graced.Buckets.LateCharged);
        Assert.Equal(2_000, graced.Buckets.ChargeLate(T + 1, graceSuppresses: false));

        // The caller's c/d/e contract (story 007; Evan approved 2026-10-29 (TD-187 item 1)): a dead-letter-only Accept and a
        // Misdeliver op make no call, so the window is unchanged; two late entries in one hold make one call, so +2,000 once.
    }

    // ---- AC-4 ----

    [Fact]
    [Trait("Id", "M-F9-4")]
    public void Ac4_EightTwentyLetterOpsAtEightDoorsInOneTick_Sum155000AtN8And40000AtN1_AndTheTeamBucketRefills40000Per900Ticks()
    {
        // Arrange
        var eight = new Rig(playerScalePm: 3864);
        var solo = new Rig(playerScalePm: 1000);
        var half = new Rig(playerScalePm: 1000);

        // Act: RH(40,000 x 3864 / 10^6) = RH(154.56) = 155 points at n = 8; at n = 1 doors 0 and 1 empty the team bucket at tick 0.
        int[] atEight = Enumerable.Range(0, 8).Select(d => eight.Charge(d, 0, TwentyLetters)).ToArray();
        int[] atSolo = Enumerable.Range(0, 8).Select(d => solo.Charge(d, 0, TwentyLetters)).ToArray();
        int refilled = Enumerable.Range(2, 6).Sum(d => solo.Charge(d, 900, TwentyLetters));
        half.Charge(0, 0, TwentyLetters);
        half.Charge(1, 0, TwentyLetters);
        int halfRefilled = Enumerable.Range(2, 6).Sum(d => half.Charge(d, 450, TwentyLetters));
        solo.Charge(8, 900 + 200_000, 0);   // a zero-raw touch refills without debiting

        // Assert: in call order; the doors charged after tick 0 are still full, so only the team bucket limits them.
        Assert.Equal(new[] { 20_000, 20_000, 20_000, 20_000, 20_000, 20_000, 20_000, 15_000 }, atEight);
        Assert.Equal(new[] { 20_000, 20_000, 0, 0, 0, 0, 0, 0 }, atSolo);
        Assert.Equal(40_000, refilled);
        Assert.Equal(20_000, halfRefilled);
        Assert.Equal(40_000L * 900, solo.Buckets.TeamTokensPm);   // emptied at 900, 200,000 idle ticks later: exactly full
    }

    // ---- AC-5 ----

    [Fact]
    [Trait("Id", "M-R9-2a")]
    public void Ac5_EachNonZeroRaiseEmitsExactlyOneComplaintRaised_AZeroRaiseEmitsNone_AndTheDescriptorIsValid()
    {
        // Arrange: Inventory and Mail composed as shipped over the M-1a street, plus a probe that charges and raises in phase 12.
        MailSystem? mail = null;
        var raised = new List<int>();
        var probe = new ProbeSystem(() =>
        {
            var d = new DestinationOrdinal(3);
            SimCounterSink sink = new SimCounters().PrimitiveSink();
            foreach (int amount in new[]
            {
                mail!.Complaints.ChargeMisdelivery(d, 1, TwentyLetters, _plain, ref sink),
                mail.Complaints.ChargeMisdelivery(d, 1, TwentyLetters, _plain, ref sink),
            })
            {
                raised.Add(amount);
                mail.RaiseComplaint(d, ComplaintSource.Misdelivery, amount);
            }

            foreach (int amount in new[] { mail.Complaints.ChargeLate(1, false), mail.Complaints.ChargeLate(1, false) })
            {
                raised.Add(amount);
                mail.RaiseComplaint(d, ComplaintSource.Late, amount);
            }
        });
        SimProfile profile = SimProfile.ForTest(
            "md006",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx => new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
            new SimSystemEntry(SystemId.MailDestinations, ctx => mail = new MailSystem(
                ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), ctx.WalletGate, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters)),
            new SimSystemEntry(SystemId.RunStructure, _ => probe));
        var capture = new CaptureSink();
        SimHost host = SimComposition.Build(new SimBuildArgs(profile, _content, 6) { WorldSource = M1aWorldSource.Instance, EventSink = capture });

        // Act
        host.World.Tick();

        // Assert: four charges (20,000, 0, 2,000, 0), two events.
        Assert.Equal(new[] { 20_000, 0, 2_000, 0 }, raised);
        EventEntry[] complaints = capture.Last.Where(e => e.Kind == MailEventKinds.ComplaintRaised).ToArray();
        Assert.Equal(2, complaints.Length);
        Assert.All(complaints, e => Assert.Equal((ReplicationClass.Replicated, MailEventDescriptors.AudienceAll), (e.Class, e.Audience)));
        ComplaintRaisedPayload[] p = complaints.Select(e => e.PayloadAs<ComplaintRaisedPayload>()).ToArray();
        uint door = mail!.Registry.EntityIdOf(new DestinationOrdinal(3)).Value;
        Assert.Equal(new ComplaintRaisedPayload { Destination = door, AmountPermille = 20_000, Source = (byte)ComplaintSource.Misdelivery }, p[0]);
        Assert.Equal(new ComplaintRaisedPayload { Destination = door, AmountPermille = 2_000, Source = (byte)ComplaintSource.Late }, p[1]);
        Assert.Empty(EventDescriptorAssert.Validate(MailEventDescriptors.ComplaintRaised, OpKindBlock.MailDelivery));
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

    // ---- AC-6 ----

    [Fact]
    [Trait("Id", "MD006-AC6")]
    public void Ac6_ASecondCallAtTheSameTickRefillsNothing_NoWallClockIsRead_AndARegressedTickAsserts()
    {
        // Arrange
        var rig = new Rig();

        // Act: a 3-letter op at tick 100, then one more at the same tick; a late charge, then one more at the same tick.
        Assert.Equal(15_000, rig.Charge(0, 100, 3 * Letter));
        long door = rig.DoorTokens(0);
        long team = rig.Buckets.TeamTokensPm;
        Assert.Equal(5_000, rig.Charge(0, 100, 3 * Letter));
        Assert.Equal(2_000, rig.Buckets.ChargeLate(100, false));
        Assert.Equal(0, rig.Buckets.ChargeLate(100, false));

        // Assert: the second charge refilled 0 (only its own 5,000 x 900 was debited), and lastLateTick stayed 100.
        door -= 5_000L * 900;
        team -= 5_000L * 900;
        Assert.Equal(door, rig.DoorTokens(0));
        Assert.Equal(team, rig.Buckets.TeamTokensPm);
        Assert.Equal(100L, rig.Buckets.LastLateTick);
        Assert.Contains(MailScan.TypesIn(MailScan.MailNamespace), t => t == typeof(ComplaintBuckets));
        Assert.Empty(MailScan.WallClockReads(MailScan.MailNamespace));

        // A touch before the stored tick asserts under SIM_ASSERT; Release treats the elapsed time as 0 (Evan approved 2026-10-29 (TD-187 item 5)).
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.MailComplaintTickRegressed, Assert.Throws<SimAssertException>(() => rig.Charge(0, 99, 0)).Code);
            Assert.Equal(SimAssertCode.MailComplaintTickRegressed, Assert.Throws<SimAssertException>(() => rig.Buckets.ChargeLate(99, false)).Code);
        }
        else
        {
            Assert.Equal(0, rig.Charge(0, 99, 0));
            Assert.Equal((door, team), (rig.DoorTokens(0), rig.Buckets.TeamTokensPm));
            Assert.Equal(0, rig.Buckets.ChargeLate(99, false));
            Assert.Equal(100L, rig.Buckets.LastLateTick);
        }
    }

    [Fact]
    [Trait("Id", "MD006-AC6")]
    public void Dump_AppendsTheTeamStateAfterTheRegistry_And_TheBareRegistryLayoutIsUnchangedWithoutBuckets()
    {
        // Arrange
        var rig = new Rig();
        var table = new MailTableRig().Table;
        rig.Charge(0, 30, TwentyLetters);
        rig.Buckets.ChargeLate(40, false);

        // Act
        byte[] bare = Dump(new MailStateContributor(table, rig.Registry));
        byte[] withTeam = Dump(new MailStateContributor(table, rig.Registry, rig.Buckets));

        // Assert: 8 + 8 + 8 + 1 bytes appended after the registry, field by field.
        Assert.Equal(bare.Length + 25, withTeam.Length);
        Assert.Equal(bare.AsSpan(6).ToArray(), withTeam.AsSpan(6, bare.Length - 6).ToArray());
        ReadOnlySpan<byte> t = withTeam.AsSpan(bare.Length);
        Assert.Equal(20_000L * 900, BinaryPrimitives.ReadInt64LittleEndian(t));
        Assert.Equal(30L, BinaryPrimitives.ReadInt64LittleEndian(t.Slice(8)));
        Assert.Equal(40L, BinaryPrimitives.ReadInt64LittleEndian(t.Slice(16)));
        Assert.Equal((byte)1, t[24]);
    }

    private static byte[] Dump(MailStateContributor contributor)
    {
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());
        w.BeginSection(contributor.SectionId);
        contributor.Write(ref w);
        w.EndSection();
        return output.WrittenSpan.ToArray();
    }
}
