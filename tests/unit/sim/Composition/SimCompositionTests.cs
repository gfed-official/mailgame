using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.CompositionTests;

/// <summary>
/// The composition owner a host would be (test side): it creates the content set once and passes the same reference to every
/// <see cref="SimComposition.Build"/> call. <see cref="ContentBuildCount"/> lives here, never in the Sim (content-data 008 AC4).
/// </summary>
public sealed class CompositionOwner
{
    private ContentSet? _content;

    /// <summary>How many times the content factory ran for this owner.</summary>
    public int ContentBuildCount { get; private set; }

    /// <summary>The content set, built on first use.</summary>
    public ContentSet Content
    {
        get
        {
            if (_content is null)
            {
                _content = BuiltInContent.CreateM1a();
                ContentBuildCount++;
            }

            return _content;
        }
    }

    /// <summary>Builds a host with this owner's content.</summary>
    internal SimHost Build(SimProfile profile, ulong seed = 7, SimInitialCounters? counters = null, IHostClock? clock = null)
        => SimComposition.Build(new SimBuildArgs(profile, Content, seed, counters, clock));
}

/// <summary>
/// Story 025 (GDD SC-R4-1 row 5, ADR-0003 section 4, TR-sim-012/013): <see cref="SimComposition.Build"/> graph codes
/// (AC-1), the topological construction order of every profile (AC-1), and the built host (AC-2), plus the carried-over
/// content-data 008 AC4 obligation (one shared <see cref="ContentSet"/>, factory run once per owner), TD-043 items 2 and 3,
/// TD-045 item 1 and TD-052 item 1.
/// </summary>
[Trait("Id", "SC-R4-1")]
public class SimCompositionTests : IClassFixture<CompositionOwner>
{
    private readonly CompositionOwner _owner;

    public SimCompositionTests(CompositionOwner owner)
    {
        _owner = owner;
    }

    // ---------------------------------------------------------------- fixtures

    public interface IProbeApi
    {
        SystemId Id { get; }
    }

    public interface INotProvidedApi
    {
    }

    private sealed class ActionPhase : ISimPhase
    {
        private readonly Action<TickContext> _body;

        public ActionPhase(Action<TickContext> body)
        {
            _body = body;
        }

        public void Run(in TickContext ctx) => _body(ctx);
    }

    private sealed class ActionCheck : ITickEndCheck
    {
        private readonly Action _body;

        public ActionCheck(Action body)
        {
            _body = body;
        }

        public void Check(uint tick) => _body();
    }

    /// <summary>A test system: logs its construction, its phase run and its tick-end check, and may run extra phase work.</summary>
    private sealed class ProbeSystem : ISimSystem, IProbeApi
    {
        private readonly List<string> _log;
        private readonly PhaseId _phase;
        private readonly Action<TickContext>? _work;

        public ProbeSystem(SystemId id, List<string> log, PhaseId phase = PhaseId.RunStep, Action<TickContext>? work = null)
        {
            Id = id;
            _log = log;
            _phase = phase;
            _work = work;
            _log.Add("new " + id);
        }

        public SystemId Id { get; }

        public void Bind(SimWorldBuilder builder)
        {
            builder.Bind(_phase, new ActionPhase(ctx =>
            {
                _log.Add("phase " + Id);
                _work?.Invoke(ctx);
            }));
            builder.BindTickEnd(new ActionCheck(() => _log.Add("end " + Id)));
        }
    }

    private static SimSystemEntry Probe(SystemId id, List<string> log) => new(id, _ => new ProbeSystem(id, log));

    private static SimSystemEntry Entry(SystemId id, SimSystemFactory factory) => new(id, factory);

    private static SimBuildException BuildThrows(CompositionOwner owner, SimProfile profile)
        => Assert.Throws<SimBuildException>(() => owner.Build(profile));

    private static Op JoinOp(uint req, byte slot)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = req };
        op.SetPayload(new PlayerJoinPayload { Slot = slot });
        return op;
    }

    // ---------------------------------------------------------------- AC-1: graph codes

    [Fact]
    public void Ac1_GraphWithCycleAmongProfileSystems_ThrowsDependencyCycle_BeforeAnyConstruction()
    {
        // Arrange
        var log = new List<string>();
        SystemGraph cyclic = SystemGraph.FromEdges(
            (SystemId.Economy, new[] { SystemId.Perks }),
            (SystemId.Perks, new[] { SystemId.Economy }));
        SimProfile profile = SimProfile.ForTest("cycle", cyclic, Probe(SystemId.Economy, log), Probe(SystemId.Perks, log));

        // Act
        SimBuildException e = BuildThrows(_owner, profile);

        // Assert
        Assert.Equal(SimBuildErrorCode.DependencyCycle, e.Code);
        Assert.Empty(log);
    }

    [Fact]
    public void Ac1_CycleOutsideTheBuiltSystems_StillThrowsDependencyCycle()
    {
        // Arrange: a three-node cycle among systems the profile does not build.
        SystemGraph cyclic = SystemGraph.FromEdges(
            (SystemId.Vehicles, new[] { SystemId.EnemyAiWaves }),
            (SystemId.EnemyAiWaves, new[] { SystemId.Defenses }),
            (SystemId.Defenses, new[] { SystemId.Vehicles }));
        SimProfile profile = SimProfile.ForTest("cycle-elsewhere", cyclic, Probe(SystemId.GridInventory, new List<string>()));

        // Act
        SimBuildException e = BuildThrows(_owner, profile);

        // Assert
        Assert.Equal(SimBuildErrorCode.DependencyCycle, e.Code);
    }

    [Fact]
    public void Ac1_SelfDependency_IsACycle()
    {
        // Arrange
        SystemGraph self = SystemGraph.FromEdges((SystemId.GridInventory, new[] { SystemId.GridInventory }));

        // Act
        bool cyclic = self.TryFindCycle(out SystemId member);
        SimBuildException e = BuildThrows(_owner, SimProfile.ForTest("self", self));

        // Assert
        Assert.True(cyclic);
        Assert.Equal(SystemId.GridInventory, member);
        Assert.Equal(SimBuildErrorCode.DependencyCycle, e.Code);
    }

    [Fact]
    public void Ac1_ConstructorAsksForApiOfUndeclaredDependency_ThrowsUndeclaredDependency()
    {
        // Arrange: shipped graph, Grid declares no dependencies; Grid asks for Mail's API.
        var log = new List<string>();
        SimProfile profile = SimProfile.ForTest(
            "undeclared",
            null,
            Probe(SystemId.MailDestinations, log),
            Entry(SystemId.GridInventory, c => new ProbeSystem(c.Api<IProbeApi>(SystemId.MailDestinations).Id, log)));

        // Act
        SimBuildException e = BuildThrows(_owner, profile);

        // Assert
        Assert.Equal(SimBuildErrorCode.UndeclaredDependency, e.Code);
        Assert.Contains("does not declare", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ac1_ApiOfDeclaredDependencyThatTheProfileDoesNotBuild_ThrowsUndeclaredDependency()
    {
        // Arrange: Mail declares Grid, but Grid is not in the profile.
        var log = new List<string>();
        SimProfile profile = SimProfile.ForTest(
            "unbuilt-provider",
            null,
            Entry(SystemId.MailDestinations, c => new ProbeSystem(c.Api<IProbeApi>(SystemId.GridInventory).Id, log)));

        // Act
        SimBuildException e = BuildThrows(_owner, profile);

        // Assert
        Assert.Equal(SimBuildErrorCode.UndeclaredDependency, e.Code);
        Assert.Contains("not built", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ac1_ApiOfProviderConstructedLater_ThrowsUndeclaredDependency()
    {
        // Arrange: Mail before Grid in the per-test order (not topological).
        var log = new List<string>();
        SimProfile profile = SimProfile.ForTest(
            "late-provider",
            null,
            Entry(SystemId.MailDestinations, c => new ProbeSystem(c.Api<IProbeApi>(SystemId.GridInventory).Id, log)),
            Probe(SystemId.GridInventory, log));

        // Act
        SimBuildException e = BuildThrows(_owner, profile);

        // Assert
        Assert.Equal(SimBuildErrorCode.UndeclaredDependency, e.Code);
        Assert.False(SystemGraph.Shipped.IsTopologicalOrder(SimComposition.ConstructionOrder(profile), out SystemId offender));
        Assert.Equal(SystemId.MailDestinations, offender);
    }

    [Fact]
    public void Ac1_ApiTypeTheProviderDoesNotImplement_ThrowsUndeclaredDependency()
    {
        // Arrange
        var log = new List<string>();
        SimProfile profile = SimProfile.ForTest(
            "wrong-api",
            null,
            Probe(SystemId.GridInventory, log),
            Entry(SystemId.MailDestinations, c =>
            {
                c.Api<INotProvidedApi>(SystemId.GridInventory);
                return new ProbeSystem(SystemId.MailDestinations, log);
            }));

        // Act
        SimBuildException e = BuildThrows(_owner, profile);

        // Assert
        Assert.Equal(SimBuildErrorCode.UndeclaredDependency, e.Code);
        Assert.Contains("does not provide", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ac1_ApiOfDeclaredBuiltProvider_ResolvesToTheProviderInstance()
    {
        // Arrange
        var log = new List<string>();
        IProbeApi? resolved = null;
        SimProfile profile = SimProfile.ForTest(
            "declared",
            null,
            Probe(SystemId.GridInventory, log),
            Entry(SystemId.MailDestinations, c =>
            {
                resolved = c.Api<IProbeApi>(SystemId.GridInventory);
                return new ProbeSystem(SystemId.MailDestinations, log);
            }));

        // Act
        SimHost host = _owner.Build(profile);

        // Assert
        Assert.Same(host.Systems[0], resolved);
        Assert.Equal(SystemId.GridInventory, resolved!.Id);
    }

    [Fact]
    public void Ac1_SameSystemIdComposedTwice_ThrowsDuplicateSystem_BeforeAnyConstruction()
    {
        // Arrange
        var log = new List<string>();
        SimProfile profile = SimProfile.ForTest("dup", null, Probe(SystemId.GridInventory, log), Probe(SystemId.GridInventory, log));

        // Act
        SimBuildException e = BuildThrows(_owner, profile);

        // Assert
        Assert.Equal(SimBuildErrorCode.DuplicateSystem, e.Code);
        Assert.Empty(log);
    }

    [Fact]
    public void Ac1_MailIdsOutsideMail_ThrowsUndeclaredDependency_AndMailGetsTheWorldsAllocator()
    {
        // Arrange
        var log = new List<string>();
        MailIds? mailIds = null;
        SimProfile bad = SimProfile.ForTest("mailids-grid", null, Entry(SystemId.GridInventory, c =>
        {
            _ = c.MailIds;
            return new ProbeSystem(SystemId.GridInventory, log);
        }));
        SimProfile good = SimProfile.ForTest("mailids-mail", null, Entry(SystemId.MailDestinations, c =>
        {
            mailIds = c.MailIds;
            return new ProbeSystem(SystemId.MailDestinations, log);
        }));

        // Act
        SimBuildException e = BuildThrows(_owner, bad);
        SimHost host = _owner.Build(good);

        // Assert
        Assert.Equal(SimBuildErrorCode.UndeclaredDependency, e.Code);
        Assert.Same(host.Services.MailIds, mailIds);
    }

    [Fact]
    public void Ac1_RunControlOutsideRunStructure_ThrowsUndeclaredDependency_AndRunStructureCanStopInPhase12()
    {
        // Arrange
        var log = new List<string>();
        SimProfile bad = SimProfile.ForTest("stop-throwing", null, Entry(SystemId.Throwing, c =>
        {
            _ = c.RunControl;
            return new ProbeSystem(SystemId.Throwing, log);
        }));
        SimProfile good = SimProfile.ForTest("stop-run", null, Entry(SystemId.RunStructure, c =>
        {
            IRunControl stop = c.RunControl;
            return new ProbeSystem(SystemId.RunStructure, log, PhaseId.RunStep, _ => stop.RequestStop(StopReason.Unspecified));
        }));

        // Act
        SimBuildException e = BuildThrows(_owner, bad);
        SimHost host = _owner.Build(good);
        host.World.Tick();

        // Assert
        Assert.Equal(SimBuildErrorCode.UndeclaredDependency, e.Code);
        Assert.Equal(SimState.Stopped, host.World.State);
        Assert.Equal(0, host.World.Counters[CounterId.StopOutOfPhase]);
    }

    [Fact]
    public void Ac1_ShippedGraph_IsAcyclic()
    {
        // Act
        bool cyclic = SystemGraph.Shipped.TryFindCycle(out SystemId member);

        // Assert
        Assert.False(cyclic);
        Assert.Equal(SystemId.None, member);
        Assert.True(SystemGraph.Shipped.IsShipped);
    }

    public static TheoryData<string> ShippedProfiles => new() { nameof(SimProfile.M1aGreybox), nameof(SimProfile.Full) };

    private static SimProfile ShippedProfile(string name) => name == nameof(SimProfile.Full) ? SimProfile.Full : SimProfile.M1aGreybox;

    [Theory]
    [MemberData(nameof(ShippedProfiles))]
    public void Ac1_EveryShippedProfilesConstructionOrder_IsATopologicalOrderOfItsGraph(string name)
    {
        // Arrange
        SimProfile profile = ShippedProfile(name);

        // Act
        SystemId[] order = SimComposition.ConstructionOrder(profile);
        bool valid = profile.Graph.IsTopologicalOrder(order, out SystemId offender);

        // Assert
        Assert.True(valid, $"{name}: {offender} comes before one of its dependencies.");
        Assert.Equal(profile.Count, order.Length);
        Assert.False(profile.IsPerTest);
    }

    [Fact]
    public void Ac1_ShippedOrder_IsTopological_AndCoversEverySystemIdExceptNoneOnce()
    {
        // Arrange
        SystemId[] order = SimComposition.ShippedOrder.ToArray();
        SystemId[] defined = Enum.GetValues<SystemId>().Where(id => id != SystemId.None).ToArray();

        // Act
        bool valid = SystemGraph.Shipped.IsTopologicalOrder(order, out SystemId offender);

        // Assert
        Assert.True(valid, $"{offender} comes before one of its dependencies.");
        Assert.Equal(defined.Length, order.Length);
        Assert.Equal(defined.ToHashSet(), order.ToHashSet());
        Assert.Equal(order, SimComposition.ConstructionOrder(SimProfile.Full));
    }

    [Fact]
    public void Ac1_EveryShippedDependency_IsADefinedSystemOtherThanNoneAndItself()
    {
        foreach (SystemId id in Enum.GetValues<SystemId>())
        {
            foreach (SystemId dep in SystemGraph.ShippedDependencies(id).ToArray())
            {
                Assert.True(Enum.IsDefined(dep) && dep != SystemId.None && dep != id, $"{id} -> {dep}");
            }
        }
    }

    [Fact]
    public void Ac1_M1aGreyboxOrder_IsTheSixGreyboxSystemsInShippedOrder()
    {
        // Act
        SystemId[] order = SimComposition.ConstructionOrder(SimProfile.M1aGreybox);

        // Assert
        Assert.Equal(
            new[] { SystemId.GridInventory, SystemId.PlayerCharacter, SystemId.MailDestinations, SystemId.ManualDelivery, SystemId.Throwing, SystemId.RunStructure },
            order);
        Assert.False(SimProfile.M1aGreybox.Includes(SystemId.Vehicles));
        Assert.Equal("M1aGreybox", SimProfile.M1aGreybox.Name);
    }

    [Fact]
    public void Ac1_IsTopologicalOrder_RejectsReversedAndRepeatedOrders_AndIgnoresUnbuiltDependencies()
    {
        // Arrange
        SystemGraph g = SystemGraph.Shipped;

        // Act + Assert
        Assert.False(g.IsTopologicalOrder(new[] { SystemId.ManualDelivery, SystemId.MailDestinations }, out SystemId reversed));
        Assert.Equal(SystemId.ManualDelivery, reversed);
        Assert.False(g.IsTopologicalOrder(new[] { SystemId.GridInventory, SystemId.GridInventory }, out SystemId repeated));
        Assert.Equal(SystemId.GridInventory, repeated);
        Assert.True(g.IsTopologicalOrder(new[] { SystemId.Throwing }, out SystemId none)); // all of Throwing's deps unbuilt
        Assert.Equal(SystemId.None, none);
    }

    [Fact]
    public void Ac1_FromEdges_CopiesRows_RejectsTwoRowsForOneSystem_AndDeclaresOnlyListedEdges()
    {
        // Arrange
        var deps = new[] { SystemId.GridInventory };
        SystemGraph g = SystemGraph.FromEdges((SystemId.MailDestinations, deps));
        deps[0] = SystemId.Perks;

        // Act + Assert
        Assert.True(g.Declares(SystemId.MailDestinations, SystemId.GridInventory));
        Assert.False(g.Declares(SystemId.MailDestinations, SystemId.Perks));
        Assert.False(g.Declares(SystemId.GridInventory, SystemId.MailDestinations));
        Assert.False(g.IsShipped);
        Assert.Throws<ArgumentException>(() => SystemGraph.FromEdges(
            (SystemId.Perks, Array.Empty<SystemId>()),
            (SystemId.Perks, Array.Empty<SystemId>())));
    }

    [Fact]
    public void Ac1_ConstructionOrder_IsBinderAndTickEndOrder()
    {
        // Arrange: three systems bound to one phase, constructed in a (topological) order that is not id order.
        var log = new List<string>();
        SimProfile profile = SimProfile.ForTest(
            "order",
            null,
            Probe(SystemId.HealthDamage, log),
            Probe(SystemId.GridInventory, log),
            Probe(SystemId.WorldGeneration, log));
        SimHost host = _owner.Build(profile);
        log.Clear();

        // Act
        host.World.Tick();

        // Assert
        Assert.Equal(
            new[]
            {
                "phase HealthDamage", "phase GridInventory", "phase WorldGeneration",
                "end HealthDamage", "end GridInventory", "end WorldGeneration",
            },
            log);
        Assert.Equal(new[] { SystemId.HealthDamage, SystemId.GridInventory, SystemId.WorldGeneration }, host.Order);
    }

    [Fact]
    public void Ac1_ContextUsedAfterBuild_ThrowsInvalidOperation()
    {
        // Arrange
        SimSystemContext? captured = null;
        SimProfile profile = SimProfile.ForTest("capture", null, Entry(SystemId.GridInventory, c =>
        {
            captured = c;
            return new ProbeSystem(SystemId.GridInventory, new List<string>());
        }));
        _owner.Build(profile);

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => captured!.Events);
        Assert.Throws<InvalidOperationException>(() => captured!.Api<IProbeApi>(SystemId.GridInventory));
    }

    [Fact]
    public void Ac1_FactoryReturningNull_ThrowsInvalidOperation()
    {
        // Arrange
        SimProfile profile = SimProfile.ForTest("null", null, Entry(SystemId.GridInventory, _ => null!));

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => _owner.Build(profile));
    }

    [Fact]
    public void Ac2_DefaultArgsOrMissingContent_ThrowArgumentException()
    {
        // Act + Assert
        Assert.Throws<ArgumentException>(() => SimComposition.Build(default));
        Assert.Throws<ArgumentException>(() => SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, null!, 1)));
        Assert.Throws<ArgumentNullException>(() => SimComposition.ConstructionOrder(null!));
    }

    // ---------------------------------------------------------------- AC-2: the built host

    [Fact]
    public void Ac2_PerTestProfile_WorldIsReadyAtTick0OnTheCallingThread_AndClientIsBuilt()
    {
        // Arrange
        var log = new List<string>();
        SimProfile profile = SimProfile.ForTest("ready", null, Probe(SystemId.GridInventory, log));

        // Act
        SimHost host = _owner.Build(profile);

        // Assert
        Assert.Equal(SimState.Ready, host.World.State);
        Assert.Equal(0u, host.World.CurrentTick);
        Assert.Equal(Environment.CurrentManagedThreadId, host.World.OwnerThreadId);
        Assert.NotNull(host.Client);
        Assert.Equal(0u, host.Client.ClientTick);
        Assert.Single(host.Systems);
        Assert.True(profile.IsPerTest);
        Assert.Same(_owner.Content.Settings.Core, host.World.Settings);
    }

    [Fact]
    public void Ac2_WorldAndClient_HoldNoReferenceToEachOther()
    {
        // Arrange
        SimHost host = _owner.Build(SimProfile.ForTest("graph", null, Probe(SystemId.GridInventory, new List<string>())));

        // Act
        bool clientReachableFromWorld = ObjectGraph.Reaches(host.World, host.Client);
        bool worldReachableFromClient = ObjectGraph.Reaches(host.Client, host.World);

        // Assert
        Assert.False(clientReachableFromWorld);
        Assert.False(worldReachableFromClient);
        Assert.NotSame(host.World.Counters, host.Client.Counters);
        Assert.True(ObjectGraph.Reaches(host.World, host.Services.EntityIds) || ObjectGraph.Reaches(host.World, host.World.Counters)); // walker sanity
    }

    [Fact]
    public void Ac2_HostClock_DefaultsToNotYetValid_AndAGivenClockReachesTheClient()
    {
        // Arrange
        var clock = new FixedClock { EstimatedHostTick = 12, IsValid = true };

        // Act
        SimHost unsynced = _owner.Build(SimProfile.ForTest("noclock", null));
        SimHost synced = _owner.Build(SimProfile.ForTest("clock", null), clock: clock);

        // Assert
        Assert.False(unsynced.Client.HostClock.IsValid);
        Assert.Equal(0u, unsynced.Client.HostClock.EstimatedHostTick);
        Assert.Same(clock, synced.Client.HostClock);
    }

    private sealed class FixedClock : IHostClock
    {
        public uint EstimatedHostTick { get; set; }

        public bool IsValid { get; set; }
    }

    [Fact]
    public void Ac2_RunSeed_ReachesTheRngRegistry_AndTheSystemsOwnView()
    {
        // Arrange
        const ulong Seed = 0xC0FFEE_1234UL;
        IRngStreams? view = null;
        SimProfile profile = SimProfile.ForTest("rng", null, Entry(SystemId.MailDestinations, c =>
        {
            view = c.Rng;
            return new ProbeSystem(SystemId.MailDestinations, new List<string>());
        }));
        SimHost host = _owner.Build(profile, Seed);
        var reference = new RngRegistry(Seed, new SimCounters());
        var other = new RngRegistry(Seed + 1, new SimCounters());

        // Act
        uint[] fromSystem = Draw(view!.Create(in RngNames.Mail, 1));
        uint[] fromHost = Draw(host.Services.Rng.Create(in RngNames.Mail, 2));
        uint[] expected1 = Draw(reference.Create(in RngNames.Mail, 1));
        uint[] expected2 = Draw(reference.Create(in RngNames.Mail, 2));
        uint[] otherSeed = Draw(other.Create(in RngNames.Mail, 1));

        // Assert
        Assert.Equal(expected1, fromSystem);
        Assert.Equal(expected2, fromHost);
        Assert.NotEqual(otherSeed, fromSystem);
        Assert.Same(host.Services.Rng.ViewFor(SystemId.MailDestinations), view);
    }

    private static uint[] Draw(RngStream s)
    {
        var values = new uint[8];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = s.Next32();
        }

        return values;
    }

    [Fact]
    public void Ac2_InitialCounters_SeedEntityAndMailIdsAndOpSeq()
    {
        // Arrange (TD-043 item 2: last issued, 0 = none)
        var counters = new SimInitialCounters
        {
            LastOpSeq = 41,
            LastMailId = 99,
            LastEntityCounters = ImmutableArray.Create(0u, 5u, 0u, 7u),
        };
        MailId allocated = MailId.None;
        SimProfile profile = SimProfile.ForTest("counters", null, Entry(SystemId.MailDestinations, c =>
        {
            MailIds mail = c.MailIds;
            return new ProbeSystem(SystemId.MailDestinations, new List<string>(), PhaseId.MailStep, _ =>
            {
                if (allocated.IsNone)
                {
                    allocated = mail.Allocate();
                }
            });
        }));
        SimHost host = _owner.Build(profile, counters: counters);

        // Act
        uint lastOpSeqBefore = host.World.LastOpSeq;
        Op join = JoinOp(1, 0);
        bool queued = host.World.EnqueueOp(ActorId.System, in join);
        host.World.Tick();

        // Assert
        Assert.Equal(41u, lastOpSeqBefore);
        Assert.True(queued);
        Assert.Equal(42u, host.World.LastOpSeq);
        Assert.Equal(100u, allocated.Value);
        Assert.Equal(6u, host.Services.EntityIds.Peek(EntityClass.Player));
        Assert.Equal(EntityId.Compose(EntityClass.Player, 6), host.Services.Slots.Entry(PlayerSlot.FromValue(0)).PlayerId);
        Assert.Equal(7u, host.Services.EntityIds.Peek(EntityClass.Construct));
    }

    [Fact]
    public void Ac2_NoInitialCounters_EverythingStartsAtNone()
    {
        // Act
        SimHost host = _owner.Build(SimProfile.ForTest("zero", null));

        // Assert
        Assert.Equal(0u, host.World.LastOpSeq);
        Assert.Equal(0u, host.Services.MailIds.Peek());
        Assert.Equal(0u, host.Services.EntityIds.Peek(EntityClass.Player));
        Assert.True(new SimInitialCounters().EntityCountersSpan.IsEmpty);
    }

    [Fact]
    public void Ac2_M1aGreybox_BuildsWithSimCoreServicesOnly_AndTicks()
    {
        // Arrange
        SimHost host = _owner.Build(SimProfile.M1aGreybox, seed: 99);
        Op join = JoinOp(1, 3);

        // Act
        host.World.EnqueueOp(ActorId.System, in join);
        for (int i = 0; i < 3; i++)
        {
            host.World.Tick();
        }

        // Assert
        Assert.Empty(host.Systems);
        Assert.Equal(6, host.Order.Length);
        Assert.Equal(SimState.Running, host.World.State);
        Assert.Equal(3u, host.World.CurrentTick);
        Assert.True(host.Services.Slots.Entry(PlayerSlot.FromValue(3)).Occupied);
        Assert.Equal(0, host.World.Counters[CounterId.BadTick]);
    }

    [Fact]
    public void Ac2_M1aGreybox_PlayerOpsAreTheCoreHandlers_AndTheLeaveHandlerHasTheWorldsLatch()
    {
        // Arrange (TD-045 item 1, TD-052 item 1)
        SimHost host = _owner.Build(SimProfile.M1aGreybox);

        // Act
        bool hasJoin = host.World.TryGetOp(OpKind.PlayerJoin, out _, out IOpHandler join);
        bool hasLeave = host.World.TryGetOp(OpKind.PlayerLeave, out _, out IOpHandler leave);

        // Assert
        Assert.True(hasJoin);
        Assert.True(hasLeave);
        Assert.IsType<PlayerJoinHandler>(join);
        PlayerLeaveHandler leaveHandler = Assert.IsType<PlayerLeaveHandler>(leave);
        Assert.Same(host.World.SampleLatchState, leaveHandler.Latch);
    }

    [Fact]
    public void Ac2_BuildOnAnotherThread_WorldAndAllocatorsShareThatOwnerThread()
    {
        // Arrange (TD-043 item 3)
        ContentSet content = _owner.Content;
        SimHost? host = null;
        int builderThread = 0;
        var thread = new Thread(() =>
        {
            builderThread = Environment.CurrentManagedThreadId;
            host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, 3));
        });

        // Act
        thread.Start();
        thread.Join();

        // Assert
        Assert.NotNull(host);
        Assert.NotEqual(Environment.CurrentManagedThreadId, builderThread);
        Assert.Equal(builderThread, host!.World.OwnerThreadId);
        Assert.Equal(builderThread, host.Services.EntityIds.OwnerThreadId);
        Assert.Equal(builderThread, host.Services.MailIds.OwnerThreadId);
    }

    [Fact]
    public void Ac2_EventFacade_IsAttachedToTheWorldsLog_AndForwardsRaisesFromAPhase()
    {
        // Arrange
        EventLogFacade? events = null;
        bool raised = false;
        int countInPhase = -1;
        SimProfile profile = SimProfile.ForTest("events", null, Entry(SystemId.GridInventory, c =>
        {
            EventLogFacade e = c.Events;
            events = e;
            Assert.False(e.IsAttached);
            return new ProbeSystem(SystemId.GridInventory, new List<string>(), PhaseId.PlayersStep, _ =>
            {
                using EventCallScope scope = e.BeginCall();
                var reply = default(Reply);
                raised = e.TryRaise(EventKind.OpReply, 0, in reply);
                countInPhase = e.Count;
            });
        }));
        SimHost host = _owner.Build(profile);

        // Act
        host.World.Tick();

        // Assert
        Assert.Same(host.Services.Events, events);
        Assert.True(events!.IsAttached);
        Assert.Same(host.World.Events, events.Log);
        Assert.True(raised);
        Assert.Equal(1, countInPhase);
    }

    // ---------------------------------------------------------------- the canonical dump (TD-045 item 1, dump order)

    /// <summary>A system that binds one dump section (writes its section id) and, optionally, creates an RNG stream it owns.</summary>
    private sealed class DumpSystem : ISimSystem, ISimStateContributor
    {
        public DumpSystem(ushort sectionId, IRngStreams? streams = null)
        {
            SectionId = sectionId;
            streams?.Create(in RngNames.Mail, 1); // the stream becomes live in the world's registry
        }

        public ushort SectionId { get; }

        public void Bind(SimWorldBuilder builder) => builder.BindStateContributor(this);

        public void Write(ref CanonicalWriter w) => w.WriteU16(SectionId);
    }

    private static List<(ushort Id, byte[] Payload)> Sections(SimWorld world)
    {
        byte[] dump = DumpHash.Bytes(world);
        var sections = new List<(ushort, byte[])>();
        int pos = 6; // "PMSD" + format version
        while (pos < dump.Length)
        {
            ushort id = BitConverter.ToUInt16(dump, pos);
            int length = (int)BitConverter.ToUInt32(dump, pos + 2);
            sections.Add((id, dump[(pos + 6)..(pos + 6 + length)]));
            pos += 6 + length;
        }

        return sections;
    }

    // Section 1 offsets for a world without test classes: tick 0, eight class counters 4..35 (Player at 4, Construct at 12),
    // test-class count 36, last mail id 40, last opSeq 44, live stream count 48.
    private const int PlayerCounterOffset = 4;
    private const int MailOffset = 40;
    private const int OpSeqOffset = 44;
    private const int StreamCountOffset = 48;

    private SimHost BuildForDump(ulong seed, SimInitialCounters? counters)
        => _owner.Build(
            SimProfile.ForTest("dump", null, Entry(SystemId.MailDestinations, c => new DumpSystem(30, c.Rng))),
            seed,
            counters);

    [Fact]
    public void Ac2_ComposedWorldsDumpSection1_IsWrittenFromTheCompositionsAllocatorsAndRegistry()
    {
        // Arrange (TD-045 item 1: BindCoreState gets the composition's own EntityIds, MailIds, RngRegistry and SlotTable)
        var counters = new SimInitialCounters { LastOpSeq = 41, LastMailId = 99, LastEntityCounters = ImmutableArray.Create(0u, 5u) };

        // Act
        byte[] section1 = Sections(BuildForDump(11, counters).World)[0].Payload;
        byte[] fresh = Sections(_owner.Build(SimProfile.ForTest("fresh", null)).World)[0].Payload;

        // Assert
        Assert.Equal(5u, BitConverter.ToUInt32(section1, PlayerCounterOffset));
        Assert.Equal(99u, BitConverter.ToUInt32(section1, MailOffset));
        Assert.Equal(41u, BitConverter.ToUInt32(section1, OpSeqOffset));
        Assert.Equal(1u, BitConverter.ToUInt32(section1, StreamCountOffset));
        Assert.Equal(0u, BitConverter.ToUInt32(fresh, MailOffset));
        Assert.Equal(0u, BitConverter.ToUInt32(fresh, StreamCountOffset));
    }

    [Fact]
    public void Ac2_ComposedWorldsDumpSection1_FollowsTheRunSeedThroughTheRegistry()
    {
        // Act
        byte[] seedA = Sections(BuildForDump(11, null).World)[0].Payload;
        byte[] seedAAgain = Sections(BuildForDump(11, null).World)[0].Payload;
        byte[] seedB = Sections(BuildForDump(12, null).World)[0].Payload;

        // Assert: the same seed dumps identically; another seed changes only the stream rows.
        Assert.Equal(seedA, seedAAgain);
        Assert.NotEqual(seedA, seedB);
        Assert.Equal(seedA[..(StreamCountOffset + 4)], seedB[..(StreamCountOffset + 4)]);
    }

    [Fact]
    public void Ac1_DumpSectionOrder_IsConstructionOrder_AndFlipsWithIt()
    {
        // Arrange: two independent systems (no edge either way), so both orders are topological.
        SimProfile gridFirst = SimProfile.ForTest(
            "grid-first",
            null,
            Entry(SystemId.GridInventory, _ => new DumpSystem(20)),
            Entry(SystemId.HealthDamage, _ => new DumpSystem(10)));
        SimProfile healthFirst = SimProfile.ForTest(
            "health-first",
            null,
            Entry(SystemId.HealthDamage, _ => new DumpSystem(10)),
            Entry(SystemId.GridInventory, _ => new DumpSystem(20)));

        // Act
        ushort[] a = Sections(_owner.Build(gridFirst).World).Select(x => x.Id).ToArray();
        ushort[] b = Sections(_owner.Build(healthFirst).World).Select(x => x.Id).ToArray();

        // Assert
        Assert.Equal(new ushort[] { SectionIds.SimCore, 20, 10 }, a);
        Assert.Equal(new ushort[] { SectionIds.SimCore, 10, 20 }, b);
    }

    // ---------------------------------------------------------------- content-data 008 AC4 (carried over)

    [Fact]
    public void CarriedOver_TwoBuildsWithOneContentSet_ShareTheInstance_AndTheFactoryRunsOnce()
    {
        // Arrange: a fresh owner, so its count starts at 0.
        var owner = new CompositionOwner();
        var seen = new List<ContentSet>();
        SimProfile Capture() => SimProfile.ForTest("content", null, Entry(SystemId.GridInventory, c =>
        {
            seen.Add(c.Content);
            return new ProbeSystem(SystemId.GridInventory, new List<string>());
        }));

        // Act
        SimHost a = owner.Build(Capture(), seed: 1);
        SimHost b = owner.Build(Capture(), seed: 2);

        // Assert
        Assert.Equal(1, owner.ContentBuildCount);
        Assert.NotSame(a.World, b.World);
        Assert.True(ReferenceEquals(a.Content, b.Content));
        Assert.True(ReferenceEquals(owner.Content, a.Content));
        Assert.Equal(2, seen.Count);
        Assert.Same(seen[0], seen[1]);
        Assert.Same(a.Content, seen[0]);
    }

    // ---------------------------------------------------------------- EventLogFacade before attachment

    [Fact]
    public void EventFacade_BeforeAttachment_RaiseIsAnOutsideTickMisuse_ReadIsFalse_ScopeIsInert()
    {
        // Arrange
        var counters = new SimCounters();
        var facade = new EventLogFacade(counters);
        var cursor = new EventCursor(new ReaderId(1));
        var reply = default(Reply);

        // Act
        bool read = facade.Read(ref cursor, out _);
        using (facade.BeginCall())
        {
        }

        // Assert
        Assert.False(read);
        Assert.Equal(0, facade.Count);
        Assert.Equal(0u, facade.CurrentCallSeq);
        Assert.Null(facade.Log);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            // Asserts first, like EventLog's own outside-tick misuse, so nothing is counted.
            Assert.Throws<SimAssertException>(() => facade.TryRaise(EventKind.OpReply, 0, in reply));
            Assert.Equal(0, counters[CounterId.EventsOutsideTick]);
        }
        else
        {
            Assert.False(facade.TryRaise(EventKind.OpReply, 0, in reply));
            Assert.Equal(1, counters[CounterId.EventsOutsideTick]);
            Assert.Equal(1, counters[CounterId.EventsDropped]);
        }
    }

    [Fact]
    public void EventFacade_AttachTwice_Throws()
    {
        // Arrange
        SimHost host = _owner.Build(SimProfile.ForTest("attach", null));

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => host.Services.Events.Attach(host.World.Events));
    }
}

/// <summary>Reachability over instance fields (reference identity), for the "no reference to each other" check.</summary>
internal static class ObjectGraph
{
    public static bool Reaches(object from, object target)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            object current = stack.Pop();
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            Type type = current.GetType();
            if (!type.IsValueType && !visited.Add(current))
            {
                continue;
            }

            if (current is string || current is Type || current is MemberInfo || current is Thread
                || (type.Namespace ?? string.Empty).StartsWith("System.Reflection", StringComparison.Ordinal))
            {
                continue;
            }

            if (current is Array array)
            {
                Type element = type.GetElementType()!;
                if (element.IsPrimitive || element.IsEnum || element.IsPointer)
                {
                    continue;
                }

                foreach (object? item in array)
                {
                    if (item is not null)
                    {
                        stack.Push(item);
                    }
                }

                continue;
            }

            for (Type? t = type; t is not null; t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPointer || f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType.IsByRefLike)
                    {
                        continue;
                    }

                    object? value = f.GetValue(current);
                    if (value is not null)
                    {
                        stack.Push(value);
                    }
                }
            }
        }

        return false;
    }
}
