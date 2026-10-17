using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-025 and TR-sim-026 for story 018 (GDD SC-R19-1 allocation half, ADR-0004 sections 2 and 3): the
/// first ids of each class, the separate mail counter, reserved and test classes, legality (inside <c>Tick()</c> only,
/// owner thread, the Constructed exception for Destination and Prop) and exhaustion that never wraps. Behaviour that
/// differs between SIM_ASSERT and other builds branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// The 0 B clause is in <see cref="IdAllocatorAllocationTests"/>.
/// </summary>
public class IdAllocatorTests
{
    private static readonly EntityId _sentinel = new(0xDEAD_BEEFu);

    private sealed class ForwardingReader : ITickReader, IWorldStateReader
    {
        public SimWorld? Target { get; set; }

        public uint CurrentTick => Target!.CurrentTick;

        public TickStage Stage => Target!.Stage;

        public PhaseId Phase => Target!.Phase;

        public SimState State => Target!.State;
    }

    private sealed class BodyPhase : ISimPhase
    {
        private readonly Rig _rig;

        public BodyPhase(Rig rig)
        {
            _rig = rig;
        }

        public void Run(in TickContext ctx) => _rig.RunBody();
    }

    /// <summary>One world's allocators plus a way to run a body inside a tick, returning (not throwing) what the body threw.</summary>
    private sealed class Rig
    {
        private Action _body = static () => { };
        private Exception? _thrown;

        public Rig(uint[]? initial = null, bool allowTest = false, uint lastMail = 0, SimState? stateOnly = null)
        {
            Counters = new SimCounters();
            Ring = new SimCheckRing();
            ITickReader reader;
            if (stateOnly is { } state)
            {
                World = InState(state);
                reader = World;
            }
            else
            {
                var holder = new ForwardingReader();
                var builder = new SimWorldBuilder();
                builder.Bind(PhaseId.PlayersStep, new BodyPhase(this));
                World = builder.Freeze();
                holder.Target = World;
                reader = holder;
            }

            Ids = new EntityIds(reader, Ring, Counters, initial, allowTest);
            Mail = new MailIds(reader, Ring, Counters, lastMail);
        }

        public SimCounters Counters { get; }

        public SimCheckRing Ring { get; }

        public SimWorld World { get; }

        public EntityIds Ids { get; }

        public MailIds Mail { get; }

        /// <summary>Ticks once and runs <paramref name="body"/> in phase 1; returns what it threw (null if nothing).</summary>
        public Exception? InTick(Action body)
        {
            _body = body;
            _thrown = null;
            World.Tick();
            return _thrown;
        }

        /// <summary>Runs <paramref name="body"/> on this thread, outside any tick.</summary>
        public static Exception? Direct(Action body)
        {
            try
            {
                body();
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        public void RunBody()
        {
            try
            {
                _body();
            }
            catch (Exception e)
            {
                _thrown = e;
            }
        }

        private static SimWorld InState(SimState state)
        {
            var phases = new ISimPhase[14][];
            for (int i = 0; i < phases.Length; i++)
            {
                phases[i] = Array.Empty<ISimPhase>();
            }

            var settings = new SimCoreSettings();
            var parts = new SimWorldParts(phases, Array.Empty<ITickEndCheck>(), settings, new SimCounters(), new SimCheckRing(settings.CheckRingCapacity), new NullPhaseProbe())
            {
                InitialState = state,
            };
            return new SimWorld(parts);
        }
    }

    /// <summary>SIM_ASSERT builds throw the code; other builds throw nothing.</summary>
    private static void AssertRefusal(Exception? thrown, ushort code)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException e = Assert.IsType<SimAssertException>(thrown);
            Assert.Equal(code, e.Code);
        }
        else
        {
            Assert.Null(thrown);
        }
    }

    // ---- AC-1 (SC-R19-1) ----

    [Fact]
    [Trait("Id", "SC-R19-1")]
    public void Ac1_FirstIds_PlayerAndConstruct()
    {
        var rig = new Rig();
        EntityId player = _sentinel;
        EntityId construct = _sentinel;
        EntityId player2 = _sentinel;

        Exception? thrown = rig.InTick(() =>
        {
            player = rig.Ids.Allocate(EntityClass.Player);
            construct = rig.Ids.Allocate(EntityClass.Construct);
            player2 = rig.Ids.Allocate(EntityClass.Player);
        });

        Assert.Null(thrown);
        Assert.Equal(16_777_217u, player.Value);
        Assert.Equal(50_331_649u, construct.Value);
        Assert.Equal(16_777_218u, player2.Value);
        Assert.Equal(2u, rig.Ids.Peek(EntityClass.Player));
        Assert.Equal(1u, rig.Ids.Peek(EntityClass.Construct));
        Assert.True(player.CompareTo(construct) < 0);
    }

    [Fact]
    [Trait("Id", "SC-R19-1")]
    public void Ac1_EveryEntityClass_StartsAtCounterOne()
    {
        var rig = new Rig();
        var ids = new List<EntityId>();

        Exception? thrown = rig.InTick(() =>
        {
            for (byte c = 1; c <= 8; c++)
            {
                ids.Add(rig.Ids.Allocate((EntityClass)c));
            }
        });

        Assert.Null(thrown);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal((uint)(i + 1) << 24 | 1u, ids[i].Value);
        }
    }

    [Fact]
    [Trait("Id", "SC-R19-1")]
    public void Ac1_MailBatchOfThree_AllocatesConsecutiveIds_AndEmptyBatchAllocatesNone()
    {
        var rig = new Rig();
        var mail = new List<uint>();
        uint zeroBatchPeek = 99;

        Exception? thrown = rig.InTick(() =>
        {
            // A zero-unit batch allocates nothing.
            zeroBatchPeek = rig.Mail.Peek();
            for (int i = 0; i < 3; i++)
            {
                mail.Add(rig.Mail.Allocate().Value);
            }
        });

        Assert.Null(thrown);
        Assert.Equal(0u, zeroBatchPeek);
        Assert.Equal(new uint[] { 1, 2, 3 }, mail);
        Assert.Equal(3u, rig.Mail.Peek());
    }

    [Fact]
    [Trait("Id", "SC-R19-1")]
    public void Ac1_MailCounterIsSeparateFromEntityCounters()
    {
        var rig = new Rig();
        EntityId e = _sentinel;
        MailId m = default;

        Exception? thrown = rig.InTick(() =>
        {
            _ = rig.Mail.Allocate();
            m = rig.Mail.Allocate();
            e = rig.Ids.Allocate(EntityClass.WorldItem);
        });

        Assert.Null(thrown);
        Assert.Equal(2u, m.Value);
        Assert.Equal(0x0200_0001u, e.Value);
    }

    [Theory]
    [Trait("Id", "SC-R19-1")]
    [InlineData((byte)0x09)]
    [InlineData((byte)0x80)]
    [InlineData((byte)0xEF)]
    [InlineData((byte)0x00)]
    public void Ac1_ReservedClass_ReturnsNone_WritesOneRecord_AndAssertsUnderSimAssert(byte cls)
    {
        var rig = new Rig();
        EntityId id = _sentinel;

        Exception? thrown = rig.InTick(() => id = rig.Ids.Allocate((EntityClass)cls));

        AssertRefusal(thrown, SimAssertCode.IdReservedClass);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(EntityId.None, id);
        }

        Assert.Equal(1, rig.Ring.Count);
        Assert.Equal((ushort)CheckCode.IdReservedClass, rig.Ring[0].Code);
        Assert.Equal(cls, rig.Ring[0].A);
        Assert.Equal(0u, rig.Ids.Peek((EntityClass)cls));
    }

    [Fact]
    [Trait("Id", "SC-R19-1")]
    public void Ac1_WholeReservedRange_NeverAllocates()
    {
        var rig = new Rig(allowTest: true);
        int issued = 0;

        _ = rig.InTick(() =>
        {
            for (int c = 0x09; c <= 0xEF; c++)
            {
                try
                {
                    issued += rig.Ids.Allocate((EntityClass)c).IsNone ? 0 : 1;
                }
                catch (SimAssertException)
                {
                    // SIM_ASSERT builds throw; the refusal itself is what is under test.
                }
            }
        });

        Assert.Equal(0, issued);
        Assert.Equal(1, rig.Ring.Count);
        Assert.Equal((uint)(0xEF - 0x09), rig.Ring.Suppressed(CheckCode.IdReservedClass));
    }

    [Fact]
    [Trait("Id", "SC-R19-1")]
    public void Ac1_TestClass_RefusedByDefault_AndSucceedsWithAllowTestClasses()
    {
        var refusing = new Rig();
        EntityId refused = _sentinel;
        Exception? thrown = refusing.InTick(() => refused = refusing.Ids.Allocate((EntityClass)0xF0));
        AssertRefusal(thrown, SimAssertCode.IdReservedClass);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(EntityId.None, refused);
        }

        Assert.Equal(1, refusing.Ring.Count);

        var allowing = new Rig(allowTest: true);
        EntityId first = _sentinel;
        EntityId last = _sentinel;
        thrown = allowing.InTick(() =>
        {
            first = allowing.Ids.Allocate((EntityClass)0xF0);
            last = allowing.Ids.Allocate(EntityClass.TestLast);
        });

        Assert.Null(thrown);
        Assert.Equal(0xF000_0001u, first.Value);
        Assert.Equal(0xFF00_0001u, last.Value);
        Assert.Equal(0, allowing.Ring.Count);
    }

    // ---- AC-2: legality ----

    [Fact]
    public void Ac2_PlayerAllocatedBetweenTicks_AssertsUnderSimAssert_ElseNoneAndCounts()
    {
        var rig = new Rig();
        EntityId id = _sentinel;

        Exception? thrown = Rig.Direct(() => id = rig.Ids.Allocate(EntityClass.Player));

        AssertRefusal(thrown, SimAssertCode.IdAllocIllegal);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(EntityId.None, id);
        }

        Assert.Equal(1L, rig.Counters[CounterId.IdAllocIllegal]);
        Assert.Equal(0u, rig.Ids.Peek(EntityClass.Player));
        Assert.Equal(0, rig.Ring.Count);
    }

    [Fact]
    public void Ac2_MailAllocatedBetweenTicks_IsIllegal()
    {
        var rig = new Rig();
        MailId id = new(99);

        Exception? thrown = Rig.Direct(() => id = rig.Mail.Allocate());

        AssertRefusal(thrown, SimAssertCode.IdAllocIllegal);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(MailId.None, id);
        }

        Assert.Equal(1L, rig.Counters[CounterId.IdAllocIllegal]);
        Assert.Equal(0u, rig.Mail.Peek());
    }

    [Fact]
    public void Ac2_DestinationAndProp_AreLegalWhileConstructed_OtherClassesAndMailAreNot()
    {
        var rig = new Rig(stateOnly: SimState.Constructed);
        EntityId dest = _sentinel;
        EntityId prop = _sentinel;

        Exception? thrown = Rig.Direct(() =>
        {
            dest = rig.Ids.Allocate(EntityClass.Destination);
            prop = rig.Ids.Allocate(EntityClass.Prop);
        });

        Assert.Null(thrown);
        Assert.Equal(0x0700_0001u, dest.Value);
        Assert.Equal(0x0800_0001u, prop.Value);
        Assert.Equal(0L, rig.Counters[CounterId.IdAllocIllegal]);

        EntityId player = _sentinel;
        thrown = Rig.Direct(() => player = rig.Ids.Allocate(EntityClass.Player));
        AssertRefusal(thrown, SimAssertCode.IdAllocIllegal);
        Assert.Equal(1L, rig.Counters[CounterId.IdAllocIllegal]);

        thrown = Rig.Direct(() => rig.Mail.Allocate());
        AssertRefusal(thrown, SimAssertCode.IdAllocIllegal);
        Assert.Equal(2L, rig.Counters[CounterId.IdAllocIllegal]);
    }

    [Fact]
    public void Ac2_DestinationWhileReadyBetweenTicks_IsIllegal()
    {
        var rig = new Rig();

        Exception? thrown = Rig.Direct(() => rig.Ids.Allocate(EntityClass.Destination));

        AssertRefusal(thrown, SimAssertCode.IdAllocIllegal);
        Assert.Equal(1L, rig.Counters[CounterId.IdAllocIllegal]);
    }

    [Fact]
    public void Ac2_AllocationFromAnotherThreadInsideATick_IsIllegal()
    {
        var rig = new Rig();
        Exception? offThread = null;
        EntityId id = _sentinel;

        Exception? thrown = rig.InTick(() =>
        {
            var worker = new Thread(() => offThread = Rig.Direct(() => id = rig.Ids.Allocate(EntityClass.Player)));
            worker.Start();
            worker.Join();
        });

        Assert.Null(thrown);
        AssertRefusal(offThread, SimAssertCode.IdAllocIllegal);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(EntityId.None, id);
        }

        Assert.Equal(1L, rig.Counters[CounterId.IdAllocIllegal]);
        Assert.Equal(0u, rig.Ids.Peek(EntityClass.Player));
    }

    // ---- AC-2: exhaustion ----

    [Fact]
    public void Ac2_EntityClassAtMax_ReturnsNone_WritesOneRecord_AndNeverWraps()
    {
        var initial = new uint[256];
        initial[(byte)EntityClass.Player] = EntityId.CounterMask;
        initial[(byte)EntityClass.Construct] = EntityId.CounterMask - 1;
        var rig = new Rig(initial);
        EntityId exhausted = _sentinel;
        EntityId lastConstruct = _sentinel;
        EntityId afterLast = _sentinel;

        Exception? thrown = rig.InTick(() => exhausted = rig.Ids.Allocate(EntityClass.Player));

        AssertRefusal(thrown, SimAssertCode.IdExhausted);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(EntityId.None, exhausted);
        }

        Assert.Equal(1, rig.Ring.Count);
        Assert.Equal((ushort)CheckCode.IdExhausted, rig.Ring[0].Code);
        Assert.Equal((long)EntityClass.Player, rig.Ring[0].A);
        Assert.Equal((long)EntityId.CounterMask, rig.Ring[0].B);
        Assert.Equal(EntityId.CounterMask, rig.Ids.Peek(EntityClass.Player));

        // The class below it issues its final id, then is exhausted too: no OverflowException, no wrap.
        thrown = rig.InTick(() => lastConstruct = rig.Ids.Allocate(EntityClass.Construct));
        Assert.Null(thrown);
        Assert.Equal(0x03FF_FFFFu, lastConstruct.Value);
        thrown = rig.InTick(() => afterLast = rig.Ids.Allocate(EntityClass.Construct));
        AssertRefusal(thrown, SimAssertCode.IdExhausted);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(EntityId.None, afterLast);
        }

        Assert.Equal(EntityId.CounterMask, rig.Ids.Peek(EntityClass.Construct));
    }

    [Fact]
    public void Ac2_ExhaustedClass_DoesNotAffectOtherClasses()
    {
        var initial = new uint[256];
        initial[(byte)EntityClass.Enemy] = EntityId.CounterMask;
        var rig = new Rig(initial);
        EntityId npc = _sentinel;

        Exception? thrown = rig.InTick(() => npc = rig.Ids.Allocate(EntityClass.Npc));

        Assert.Null(thrown);
        Assert.Equal(0x0600_0001u, npc.Value);
    }

    [Fact]
    public void Ac2_InitialCounterAboveMax_IsClampedToExhausted()
    {
        var initial = new uint[256];
        initial[(byte)EntityClass.Vehicle] = uint.MaxValue;
        var rig = new Rig(initial);

        Assert.Equal(EntityId.CounterMask, rig.Ids.Peek(EntityClass.Vehicle));
    }

    [Fact]
    public void Ac2_MailAtMax_ReturnsZero_WritesOneRecord_AndNeverWraps()
    {
        var rig = new Rig(lastMail: uint.MaxValue - 1);
        MailId last = default;
        MailId exhausted = new(99);
        MailId again = new(99);

        Exception? thrown = rig.InTick(() => last = rig.Mail.Allocate());
        Assert.Null(thrown);
        Assert.Equal(uint.MaxValue, last.Value);

        thrown = rig.InTick(() => exhausted = rig.Mail.Allocate());

        AssertRefusal(thrown, SimAssertCode.IdExhausted);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(MailId.None, exhausted);
        }

        Assert.Equal(1, rig.Ring.Count);
        Assert.Equal((ushort)CheckCode.IdExhausted, rig.Ring[0].Code);
        Assert.Equal(0L, rig.Ring[0].A);
        Assert.Equal((long)uint.MaxValue, rig.Ring[0].B);

        thrown = rig.InTick(() => again = rig.Mail.Allocate());
        AssertRefusal(thrown, SimAssertCode.IdExhausted);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(MailId.None, again);
        }

        Assert.Equal(uint.MaxValue, rig.Mail.Peek());
    }

    [Fact]
    public void Ac2_MailInitiallyAtMax_ReturnsZeroOnTheFirstAllocation()
    {
        var rig = new Rig(lastMail: uint.MaxValue);
        MailId id = new(99);

        Exception? thrown = rig.InTick(() => id = rig.Mail.Allocate());

        AssertRefusal(thrown, SimAssertCode.IdExhausted);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(MailId.None, id);
        }

        Assert.Equal(1, rig.Ring.Count);
    }
}

/// <summary>Covers the allocation guardrail of story 018 (control manifest): <c>Allocate</c> allocates 0 B. Joins the non-parallel collection (see <see cref="AllocAssert"/>).</summary>
[Collection(NonParallelCollection.Name)]
public class IdAllocatorAllocationTests
{
    private sealed class Reader : ITickReader
    {
        public uint CurrentTick => 0;

        public TickStage Stage => TickStage.InPhase;

        public PhaseId Phase => PhaseId.PlayersStep;
    }

    [Fact]
    public void Allocate_EntityAndMail_AllocateNothing_InOptimisedBuilds()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        var counters = new SimCounters();
        var ring = new SimCheckRing();
        var reader = new Reader();
        var ids = new EntityIds(reader, ring, counters);
        var mail = new MailIds(reader, ring, counters);
        long sum = 0;

        // Legal path; the refused path is measured in the next test.
        (long bytes, int _) = AllocAssert.Measure(() =>
        {
            sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                sum += ids.Allocate(i % 2 == 0 ? EntityClass.Player : EntityClass.Construct).Value;
                sum += mail.Allocate().Value;
            }
        });

        Assert.Equal(0L, bytes);
        Assert.NotEqual(0L, sum);
        Assert.True(ids.Peek(EntityClass.Player) >= 500u);
    }

    [Fact]
    public void Allocate_ReservedClassRefusal_AllocatesNothing_InOptimisedBuilds()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        if (TestBuildExpectation.SimAssertsExpected)
        {
            return; // the refusal throws under SIM_ASSERT; exception allocation is not the guardrail.
        }

        var ids = new EntityIds(new Reader(), new SimCheckRing(), new SimCounters());
        long none = 0;

        (long bytes, int _) = AllocAssert.Measure(() =>
        {
            none = 0;
            for (int i = 0; i < 1000; i++)
            {
                none += ids.Allocate((EntityClass)0x20).Value;
            }
        });

        Assert.Equal(0L, bytes);
        Assert.Equal(0L, none);
    }
}
