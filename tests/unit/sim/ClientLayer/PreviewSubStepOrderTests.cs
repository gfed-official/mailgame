using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests;

/// <summary>
/// Player Character story 014 (ADR-0016 section 2, Key Interfaces): <see cref="ClientLayerBuilder.BindPreview"/> runs the
/// <see cref="PreviewSubStep"/> rows in enum order, each in bind order, then the plain <c>Previews</c> binds (Mail Lead planning
/// default D4, pending Evan, TD-164), with the same guards as <see cref="ClientLayerBuilder.Bind"/>.
/// </summary>
public class PreviewSubStepOrderTests
{
    private sealed class Clock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => false;
    }

    private sealed class Stub(string name, List<string> log) : IClientStep
    {
        public void Step(in ClientStepContext ctx) => log.Add(name);
    }

    [Fact]
    public void Step_RunsSubStepsInEnumOrder_ThenPlainPreviewBinds()
    {
        // Arrange: bound out of order on purpose.
        var log = new List<string>();
        var builder = new ClientLayerBuilder(new Clock());
        builder.Bind(ClientStepId.Previews, new Stub("Plain", log));
        builder.BindPreview(PreviewSubStep.HoldAdvance, new Stub("HoldAdvance", log));
        builder.BindPreview(PreviewSubStep.EdgeResolve, new Stub("EdgeResolve", log));
        builder.BindPreview(PreviewSubStep.ThrowPreview, new Stub("ThrowPreview", log));
        builder.BindPreview(PreviewSubStep.Focus, new Stub("Focus-A", log));
        builder.BindPreview(PreviewSubStep.DeliverPreview, new Stub("DeliverPreview", log));
        builder.BindPreview(PreviewSubStep.Focus, new Stub("Focus-B", log));
        builder.Bind(ClientStepId.CarrierStep, new Stub("CarrierStep", log));
        ClientLayerSut layer = builder.Build();

        // Act
        layer.Step(true);

        // Assert
        Assert.Equal(new[] { "CarrierStep", "Focus-A", "Focus-B", "DeliverPreview", "ThrowPreview", "EdgeResolve", "HoldAdvance", "Plain" }, log);
    }

    [Fact]
    public void BindPreview_Guards_AfterBuild_NullStep_UndefinedSubStep()
    {
        // Arrange
        var builder = new ClientLayerBuilder(new Clock());
        var step = new Stub("x", new List<string>());

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => builder.BindPreview(PreviewSubStep.Focus, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.BindPreview((PreviewSubStep)5, step));
        builder.Build();
        SimBuildException e = Assert.Throws<SimBuildException>(() => builder.BindPreview(PreviewSubStep.Focus, step));
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, e.Code);
    }
}
