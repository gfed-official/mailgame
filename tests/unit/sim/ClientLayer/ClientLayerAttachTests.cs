using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests;

/// <summary>
/// godot-host-shell story 005 AC-2 (ADR-0016 section 1): <see cref="ClientLayerSut.Attach"/> runs once, before the first
/// <see cref="ClientLayerSut.Step"/>; a second call, a call after a step and a null port are refused, and the ports reach the steps
/// through <see cref="ClientStepContext.Ports"/>.
/// </summary>
public class ClientLayerAttachTests
{
    public enum AttachCase
    {
        FirstCallBeforeAnyStep,
        SecondCall,
        AfterFirstStep,
        NullPort,
    }

    [Theory]
    [InlineData(AttachCase.FirstCallBeforeAnyStep)]
    [InlineData(AttachCase.SecondCall)]
    [InlineData(AttachCase.AfterFirstStep)]
    [InlineData(AttachCase.NullPort)]
    public void Attach_OnceBeforeTheFirstStep_ElseRefused(AttachCase c)
    {
        // Arrange
        var seen = new List<bool>();
        var builder = new ClientLayerBuilder(new Ports());
        builder.Bind(ClientStepId.CarrierStep, new Reader(seen));
        ClientLayerSut layer = builder.Build();
        ClientPorts ports = Full();
        if (c == AttachCase.SecondCall)
        {
            layer.Attach(in ports);
        }
        else if (c == AttachCase.AfterFirstStep)
        {
            layer.Step(true);
        }
        else if (c == AttachCase.NullPort)
        {
            ports = ports with { LocalPlayer = null! };
        }

        // Act
        Exception? refused = Record.Exception(() => layer.Attach(in ports));
        layer.Step(true);

        // Assert
        switch (c)
        {
            case AttachCase.FirstCallBeforeAnyStep:
            case AttachCase.SecondCall:
                Assert.Equal(c == AttachCase.SecondCall, refused is InvalidOperationException);
                Assert.True(layer.IsAttached);
                Assert.Equal(new[] { true }, seen);
                break;
            case AttachCase.AfterFirstStep:
                Assert.IsType<InvalidOperationException>(refused);
                Assert.False(layer.IsAttached);
                Assert.Equal(new[] { false, false }, seen);
                break;
            default:
                Assert.IsType<ArgumentException>(refused);
                Assert.False(layer.IsAttached);
                break;
        }
    }

    private static ClientPorts Full()
    {
        var p = new Ports();
        return new ClientPorts { Requests = p, HostClock = p, TransformSamples = p, CarrierLoad = p, LocalPlayer = p };
    }

    // The step sees the attached ports (true) or nothing (false).
    private sealed class Reader(List<bool> seen) : IClientStep
    {
        public void Step(in ClientStepContext ctx) => seen.Add(ctx.IsAttached && ctx.Ports.LocalPlayer is not null);
    }

    private sealed class Ports : IRequestSink, IHostClock, ITransformSampleSink, ICarrierLoadSource, ILocalPlayer
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => true;

        public int SpeedPermille => 1000;

        public int WeightPoints => 0;

        public PlayerSlot Slot => PlayerSlot.FromValue(0);

        public EntityId Id => EntityId.None;

        public bool IsKnown => true;

        public bool TrySend(in Op op) => true;

        public bool TryWrite(in TransformSample s) => true;
    }
}
