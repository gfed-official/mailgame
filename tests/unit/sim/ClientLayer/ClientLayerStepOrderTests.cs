using System.Reflection;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests;

/// <summary>
/// Covers story 023 AC-2 (step order, context, freeze) and AC-3 (the <see cref="IHostClock"/> seam and no
/// <c>SimWorld</c> in <c>ClientLayer</c>; SC-ARCH-1 re-asserts in story 028), plus the 0 B step loop.
/// </summary>
public class ClientLayerStepOrderTests
{
    private sealed class FakeClock : IHostClock
    {
        public uint EstimatedHostTick { get; set; }

        public bool IsValid { get; set; }
    }

    private sealed class Stub : IClientStep
    {
        private readonly string _name;
        private readonly List<string> _log;

        public Stub(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public void Step(in ClientStepContext ctx) => _log.Add(_name);
    }

    private sealed class CtxRecorder : IClientStep
    {
        public readonly List<(bool First, uint Tick, IHostClock Clock, uint FrameTick)> Seen = new();

        public void Step(in ClientStepContext ctx) => Seen.Add((ctx.IsFirstTickInBatch, ctx.ClientTick, ctx.HostClock, ctx.Input.Tick));
    }

    private sealed class NoopStep : IClientStep
    {
        public int Calls;

        public void Step(in ClientStepContext ctx) => Calls++;
    }

    [Fact]
    public void Step_RunsEveryStepInClientStepIdOrder_PreviewsInBindOrder()
    {
        var log = new List<string>();
        var builder = new ClientLayerBuilder(new FakeClock());
        // Bound out of order on purpose: run order comes from the id, not from the bind order.
        builder.Bind(ClientStepId.Previews, new Stub("Previews-A", log));
        builder.Bind(ClientStepId.CarrierStep, new Stub("CarrierStep", log));
        builder.Bind(ClientStepId.Previews, new Stub("Previews-B", log));
        builder.Bind(ClientStepId.InputLatch, new Stub("InputLatch", log));
        builder.Bind(ClientStepId.MirrorDrain, new Stub("MirrorDrain", log));
        ClientLayerSut layer = builder.Build();

        layer.Step(true);

        Assert.Equal(new[] { "MirrorDrain", "InputLatch", "CarrierStep", "Previews-A", "Previews-B" }, log);
    }

    [Fact]
    public void ClientStepId_ValuesAreTheRunOrder()
    {
        Assert.Equal(new[] { ClientStepId.MirrorDrain, ClientStepId.InputLatch, ClientStepId.CarrierStep, ClientStepId.Previews }, Enum.GetValues<ClientStepId>());
        Assert.Equal(0, (int)ClientStepId.MirrorDrain);
        Assert.Equal(3, (int)ClientStepId.Previews);
    }

    [Fact]
    public void Step_UnboundIds_AreNoOps()
    {
        ClientLayerSut layer = new ClientLayerBuilder(new FakeClock()).Build();

        layer.Step(true);
        layer.Step(false);

        Assert.Equal(2u, layer.ClientTick);
    }

    [Fact]
    public void BoundStepAtInputLatch_RunsAfterTheBuiltInLatchEmitted_AndSeesItsFrame()
    {
        var builder = new ClientLayerBuilder(new FakeClock());
        var rec = new CtxRecorder();
        builder.Bind(ClientStepId.InputLatch, rec);
        builder.Bind(ClientStepId.MirrorDrain, rec);
        ClientLayerSut layer = builder.Build();

        layer.Step(true);
        layer.Step(false);

        // MirrorDrain runs before the latch: it still sees the previous frame (default on the first tick).
        Assert.Equal(new uint[] { 0, 0, 0, 1 }, rec.Seen.Select(s => s.FrameTick).ToArray());
    }

    [Fact]
    public void Context_CarriesBatchFlagClientTickAndHostClock()
    {
        var clock = new FakeClock { EstimatedHostTick = 40, IsValid = true };
        var builder = new ClientLayerBuilder(clock);
        var rec = new CtxRecorder();
        builder.Bind(ClientStepId.CarrierStep, rec);
        ClientLayerSut layer = builder.Build();

        layer.Step(true);
        layer.Step(false);
        layer.Step(true);

        Assert.Equal(3, rec.Seen.Count);
        Assert.Equal((true, 0u), (rec.Seen[0].First, rec.Seen[0].Tick));
        Assert.Equal((false, 1u), (rec.Seen[1].First, rec.Seen[1].Tick));
        Assert.Equal((true, 2u), (rec.Seen[2].First, rec.Seen[2].Tick));
        Assert.All(rec.Seen, s => Assert.Same(clock, s.Clock));
        Assert.Equal(3u, layer.ClientTick);
    }

    [Fact]
    public void Bind_AfterBuild_Throws_BindAfterFreeze()
    {
        var builder = new ClientLayerBuilder(new FakeClock());
        builder.Build();

        SimBuildException e = Assert.Throws<SimBuildException>(() => builder.Bind(ClientStepId.Previews, new NoopStep()));
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, e.Code);
    }

    [Fact]
    public void Build_Twice_Throws_BindAfterFreeze()
    {
        var builder = new ClientLayerBuilder(new FakeClock());
        builder.Build();

        SimBuildException e = Assert.Throws<SimBuildException>(() => builder.Build());
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, e.Code);
    }

    [Fact]
    public void Bind_NullStep_UndefinedId_AndNullClock_Throw()
    {
        var builder = new ClientLayerBuilder(new FakeClock());

        Assert.Throws<ArgumentNullException>(() => builder.Bind(ClientStepId.Previews, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Bind((ClientStepId)4, new NoopStep()));
        Assert.Throws<ArgumentNullException>(() => new ClientLayerBuilder(null!));
    }

    [Fact]
    public void Layer_ExposesTheClockCountersAndLatchItWasBuiltWith()
    {
        var clock = new FakeClock();
        var counters = new SimCounters();
        ClientLayerSut layer = new ClientLayerBuilder(clock, counters).Build();

        Assert.Same(clock, layer.HostClock);
        Assert.Same(counters, layer.Counters);
        Assert.NotNull(layer.InputLatch);
        Assert.Equal(0u, layer.ClientTick);
        Assert.NotNull(new ClientLayerBuilder(clock).Build().Counters);
    }

    // ---- AC-3 seams ----

    [Fact]
    public void IHostClock_DeclaresExactlyEstimatedHostTickAndIsValid()
    {
        Type t = typeof(IHostClock);
        Assert.True(t.IsInterface);
        Assert.Equal(typeof(IHostClock).Assembly, typeof(SimWorld).Assembly);
        Assert.Equal("PerformativeMail.Sim.Core", t.Namespace);

        string[] members = t.GetMembers().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "EstimatedHostTick", "IsValid", "get_EstimatedHostTick", "get_IsValid" }, members);
        PropertyInfo tick = t.GetProperty("EstimatedHostTick")!;
        Assert.Equal(typeof(uint), tick.PropertyType);
        Assert.False(tick.CanWrite);
        PropertyInfo valid = t.GetProperty("IsValid")!;
        Assert.Equal(typeof(bool), valid.PropertyType);
        Assert.False(valid.CanWrite);
    }

    [Fact]
    public void ClientLayer_ConstructorTakesAnIHostClock()
    {
        ConstructorInfo[] ctors = typeof(ClientLayerSut).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.All(ctors, c => Assert.Contains(c.GetParameters(), p => p.ParameterType == typeof(IHostClock)));
        Assert.Contains(typeof(ClientLayerBuilder).GetConstructors(), c => c.GetParameters().Any(p => p.ParameterType == typeof(IHostClock)));
    }

    [Fact]
    public void ClientLayerNamespace_ReferencesNoSimWorldType()
    {
        const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Type[] clientTypes = typeof(ClientLayerSut).Assembly.GetTypes()
            .Where(t => t.Namespace == "PerformativeMail.Sim.ClientLayer")
            .ToArray();
        Assert.NotEmpty(clientTypes);

        var offenders = new List<string>();
        foreach (Type t in clientTypes)
        {
            foreach (FieldInfo f in t.GetFields(AllMembers))
            {
                Check(f.FieldType, $"{t.Name}.{f.Name} (field)");
            }

            foreach (PropertyInfo p in t.GetProperties(AllMembers))
            {
                Check(p.PropertyType, $"{t.Name}.{p.Name} (property)");
            }

            foreach (MethodBase m in t.GetConstructors(AllMembers).Cast<MethodBase>().Concat(t.GetMethods(AllMembers)))
            {
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Check(p.ParameterType, $"{t.Name}.{m.Name}({p.Name})");
                }

                if (m is MethodInfo mi)
                {
                    Check(mi.ReturnType, $"{t.Name}.{m.Name} (return)");
                }
            }
        }

        Assert.Empty(offenders);

        void Check(Type type, string where)
        {
            while (type.HasElementType)
            {
                type = type.GetElementType()!;
            }

            if (type == typeof(SimWorld) || type == typeof(SimWorldBuilder) || type.Name.Contains("SimWorld", StringComparison.Ordinal))
            {
                offenders.Add(where);
            }

            foreach (Type arg in type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes)
            {
                Check(arg, where);
            }
        }
    }

    // ---- Guardrail: the step loop allocates 0 B ----

    [Collection(NonParallelCollection.Name)]
    public class StepLoopAllocationTests
    {
        [Fact]
        public void Step_WithBoundStepsAndEdges_Allocates0B()
        {
            var builder = new ClientLayerBuilder(new FakeClock());
            var steps = new NoopStep[4];
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i] = new NoopStep();
                builder.Bind((ClientStepId)i, steps[i]);
            }

            builder.Bind(ClientStepId.Previews, new NoopStep());
            ClientLayerSut layer = builder.Build();
            var edge = new InputEdge(FrameAction.Jump, EdgeKind.Press, 1, 1);
            var state = new InputState { HeldBits = 3, MoveX = 2, MoveY = 2, LookYaw = 1, LookPitch = 1, RouterStateId = 1 };
            IInputLatchWriter writer = layer.InputWriter;

            int runs = AllocAssert.Zero("ClientLayer.Step", () =>
            {
                for (int tick = 0; tick < 100; tick++)
                {
                    writer.PushEdge(in edge);
                    writer.SetState(in state);
                    layer.Step(tick % 3 == 0);
                }
            });

            Assert.Equal(100 * runs, (int)layer.ClientTick);
            Assert.Equal(runs * 100, steps[0].Calls);
        }
    }
}
