using System.Security.Cryptography;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Tests.Unit.MailDestinations;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 013 (GDD R10; ADR-0017 section 10, ADR-0005): the manifest roller over FX-I50 (50 addresses in districts 1
/// and 2), and <see cref="IMailApi.PrepareShift"/> on composed worlds: the phase-8 roll, the lock at Delivery start, the M-1a shift stub's
/// wiring, and the shift-start current shift and wrong-delivery reset. The re-roll and cursed rows are dropped (greybox scope).
/// </summary>
public class ManifestRollerTests
{
    private const ulong Seed = 0x5EED_0013UL;
    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    // ---- AC-1 ----

    [Fact]
    [Trait("Id", "M-DET-1")]
    public void Ac1_SameInputs_WithTheAddressListShuffled_RollTheSameManifestAndForecast_AndAnotherVOrShiftDoesNot()
    {
        // Arrange
        AddressId[] fifty = DrawTestRig.Fifty();
        AddressId[] shuffled = Enumerable.Reverse(fifty).ToArray();
        var p = new SpawnProfile(3, 4_000, 240, 0, 0, 1, 1000, 0);

        // Act
        ShiftManifest a = Roll(p, fifty);
        ShiftManifest b = Roll(p, shuffled);
        ShiftManifest otherV = Roll(p with { SpawnBudgetCents = 4_100 }, fifty);
        ShiftManifest otherShift = Roll(p with { Shift = 4 }, fifty);

        // Assert
        Assert.True(a.UnitCount > 0);
        Assert.Equal(Hash(a), Hash(b));
        Assert.Equal(Forecast(a), Forecast(b));
        Assert.NotEqual(Hash(a), Hash(otherV));
        Assert.NotEqual(Hash(a), Hash(otherShift));
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "M-R10-1c")]
    [Trait("Id", "M-R10-1d")]
    public void Ac2_AProfileChangeAtDeliveryStartIsIgnored_AndTheForecastIsTheManifestsKindByDistrictCountsSummingToN()
    {
        // Arrange: Prep 6 ticks, so Delivery starts at tick 6.
        var rig = new Rig(withStub: true);
        while (rig.Host.World.CurrentTick < 6)
        {
            rig.Host.World.Tick();
        }

        MailShiftSpawner spawner = rig.Mail.Spawner;
        Assert.True(spawner.Locked);
        Assert.Equal(6u, spawner.DeliveryStartTick);
        string locked = Hash(spawner.Manifest);

        // Act: a doubled V in phase 0 of the Delivery-start tick.
        var changed = new SpawnProfile(1, 1_920, 10, 0, 0, 1, 1000, 0);
        rig.Run(() => rig.Mail.PrepareShift(in changed));

        // Assert: ignored and counted; nothing re-rolled.
        Assert.Equal(1, spawner.IgnoredPrepareCount);
        Assert.Equal(1, spawner.RollCount);
        Assert.Equal(locked, Hash(spawner.Manifest));

        ShiftManifest m = spawner.Manifest;
        int[] byKindDistrict = new int[KindCounts.MaxKinds * ShiftManifest.ForecastDistricts];
        for (int i = 0; i < m.UnitCount; i++)
        {
            byKindDistrict[(m.KindAt(i).Value * ShiftManifest.ForecastDistricts) + (int)m.AddressAt(i).District]++;
        }

        Assert.Equal(byKindDistrict, Forecast(m));
        Assert.Equal(m.UnitCount, Forecast(m).Sum());
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "M-R10-1a")]
    [Trait("Id", "M-PERF-1e")]
    public void Ac3_HundredPrepTicksAllocateNoMailId_AndAShift5RollMakesExactlyKDraws()
    {
        // Arrange: Prep 200 ticks; the stub prepares the shift in tick 0 and Mail rolls it in tick 1.
        var rig = new Rig(withStub: true, prepTicks: 200);
        RngStream stream = DrawTestRig.Seeded(Seed, 5);
        var shift5 = new SpawnProfile(5, 24_704, 240, 0, 0, 1, 1000, 0);

        // Act
        for (int t = 0; t < 100; t++)
        {
            rig.Host.World.Tick();
        }

        ShiftManifest m = Roll(shift5, DrawTestRig.Fifty(), stream);

        // Assert
        Assert.True(rig.Mail.Spawner.Manifest.UnitCount > 0);
        Assert.Equal(0u, rig.Host.Services.MailIds.Peek());
        Assert.True(m.UnitCount > 0);
        Assert.Equal((ulong)ManifestDraws.ExpectedDraws(m.BatchCount, m.UnitCount), stream.Draws);
    }

    // ---- AC-4 ----

    [Fact]
    public void Ac4_TheShippedShiftStubPreparesShift1InPrep_GivingANonEmptyDistrict1ManifestWhoseBatchesAllFallInsideDelivery()
    {
        // Arrange: the shipped composition with a 30-tick Prep and the default 240 s Delivery.
        var settings = new ShiftStubSettings { PrepTicks = 30 };
        SimHost host = SimComposition.Build(
            new SimBuildArgs(SimProfile.M1aGreybox.WithShiftStub(settings), _content, 7) { WorldSource = M1aWorldSource.Instance });
        MailSystem mail = Assert.Single(host.Systems.OfType<MailSystem>());

        // Act: through Prep to Delivery start.
        while (host.World.CurrentTick < 30)
        {
            host.World.Tick();
        }

        // Assert
        MailShiftSpawner spawner = mail.Spawner;
        ShiftManifest m = spawner.Manifest;
        Assert.Equal(1, spawner.RollCount);
        Assert.Equal(1, mail.CurrentShift);
        Assert.True(m.UnitCount > 0);
        Assert.Equal(30u, spawner.DeliveryStartTick);
        Assert.All(Enumerable.Range(0, m.UnitCount), i => Assert.Equal(1u, m.AddressAt(i).District));
        long close = 30 + (240L * SimSettings.TickRateHz);
        Assert.All(Enumerable.Range(0, m.BatchCount), b => Assert.InRange(30 + m.BatchOffset(b), 30, close - 1));
    }

    // ---- AC-5 ----

    [Fact]
    public void Ac5_PrepareShiftForShiftN_SetsMailsCurrentShiftToN()
    {
        // Arrange
        var rig = new Rig(withStub: false);
        var p = new SpawnProfile(3, 960, 240, 0, 0, 1, 1000, 0);

        // Act
        rig.Run(() => rig.Mail.PrepareShift(in p));

        // Assert
        Assert.Equal(3, rig.Mail.CurrentShift);
    }

    // ---- AC-6 ----

    [Fact]
    public void Ac6_PrepareShiftForTheNextShift_ZeroesTheWrongDeliveryCount()
    {
        // Arrange: shift 1 prepared, then misdeliveries counted.
        var rig = new Rig(withStub: false);
        var shift1 = new SpawnProfile(1, 960, 240, 0, 0, 1, 1000, 0);
        rig.Run(() => rig.Mail.PrepareShift(in shift1));
        rig.Mail.MisdeliveriesThisShift = 7;

        // Act
        var shift2 = shift1 with { Shift = 2 };
        rig.Run(() => rig.Mail.PrepareShift(in shift2));

        // Assert
        Assert.Equal(0, rig.Mail.MisdeliveriesThisShift);
    }

    private static ShiftManifest Roll(SpawnProfile p, AddressId[] addresses, RngStream? stream = null)
    {
        var shares = new KindShare[KindCounts.MaxKinds];
        var offsets = new byte[KindCounts.MaxKinds];
        int kinds = ManifestRoller.BuildKinds(_content.MailMixes.AsSpan()[0], _content.MailKinds.AsSpan(), p.Shift, ulong.MaxValue, shares, offsets);
        var m = new ShiftManifest(addresses.Length);
        var knobs = MailValueKnobs.From(_content.Mail, MailValueKnobs.Default.CursedValueMultPm, MailValueKnobs.Default.FoundValueMultPm);
        int n = ManifestRoller.Roll(
            stream ?? DrawTestRig.Seeded(Seed, (uint)p.Shift),
            in p,
            addresses,
            shares.AsSpan(0, kinds),
            offsets.AsSpan(0, kinds),
            BatchKnobs.From(_content.MailMixes.AsSpan()[0]),
            in knobs,
            m);
        Assert.True(n >= 0, $"roll failed: {n}");
        return m;
    }

    // SHA-256 over (batch, tick, kind, addressId, flags, spawnValue) per unit, in manifest order.
    private static string Hash(ShiftManifest m)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < m.UnitCount; i++)
        {
            int b = m.BatchOf(i);
            bytes.AddRange(BitConverter.GetBytes(b));
            bytes.AddRange(BitConverter.GetBytes(m.BatchOffset(b)));
            bytes.AddRange(BitConverter.GetBytes(m.KindAt(i).Value));
            bytes.AddRange(BitConverter.GetBytes(m.AddressAt(i).Value));
            bytes.Add((byte)m.FlagsAt(i));
            bytes.AddRange(BitConverter.GetBytes(m.SpawnValueAt(i)));
        }

        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static int[] Forecast(ShiftManifest m)
    {
        int[] f = new int[KindCounts.MaxKinds * ShiftManifest.ForecastDistricts];
        for (int k = 0; k < KindCounts.MaxKinds; k++)
        {
            for (int d = 0; d < ShiftManifest.ForecastDistricts; d++)
            {
                f[(k * ShiftManifest.ForecastDistricts) + d] = m.Forecast(DefIndex.FromValue((ushort)k), d);
            }
        }

        return f;
    }

    /// <summary>Inventory, Mail, a phase-0 probe op and, optionally, the shift stub (Delivery 10 s) on the M-1a street.</summary>
    private sealed class Rig : ISimSystem, IOpHandler
    {
        private static readonly OpKind _probeOp = (OpKind)0x0100;
        private Action? _next;
        private uint _req = 1;
        private MailSystem? _mail;

        public Rig(bool withStub, int prepTicks = 6)
        {
            var settings = new ShiftStubSettings { PrepTicks = prepTicks, DeliverySeconds = 10 };
            var entries = new List<SimSystemEntry>
            {
                new(SystemId.GridInventory, ctx => new InventorySystem(ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Events, ctx.Tick, ctx.EntityIds, ctx.Slots)),
                new(SystemId.MailDestinations, ctx => _mail = new MailSystem(
                    ctx.Events, ctx.MailIds, ctx.Rng, ctx.Tick, ctx.Api<IInventoryApi>(SystemId.GridInventory), ctx.WalletGate, ctx.World!, ctx.Content, ctx.StatSheet, ctx.CheckRing, ctx.Counters, ctx.Slots)),
                new(SystemId.ManualDelivery, _ => this),
            };
            if (withStub)
            {
                entries.Add(new(SystemId.RunStructure, ctx => new ShiftStub(ctx.Api<IMailApi>(SystemId.MailDestinations), ctx.RunControl, ctx.Events, settings)));
            }

            SimProfile profile = SimProfile.ForTest("md013", null, entries.ToArray());
            Host = SimComposition.Build(new SimBuildArgs(profile, _content, 7) { WorldSource = M1aWorldSource.Instance });
        }

        public SimHost Host { get; }

        public MailSystem Mail => _mail!;

        public OpKind Kind => _probeOp;

        public void Bind(SimWorldBuilder builder) => builder.BindOp(OpDescriptor.Empty(_probeOp, true, 0, 0), this);

        public Reply Apply(in Op op, in TickContext ctx)
        {
            _next?.Invoke();
            return Reply.Ok(in op);
        }

        /// <summary>Runs one tick whose phase 0 runs <paramref name="body"/>.</summary>
        public void Run(Action body)
        {
            _next = body;
            var op = new Op { Kind = _probeOp, ReqId = _req++ };
            Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
            Host.World.Tick();
            _next = null;
        }
    }
}
