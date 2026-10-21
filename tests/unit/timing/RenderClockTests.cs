using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Sim.Tests.Unit.Timing;

/// <summary>
/// Covers <see cref="RenderClock"/> (ADR-0016 section 6, TR-hud-004; added by story godot-host-shell/003 because it was
/// absent): the integer <c>RenderSimTimeMs = ((long)HostTick * 1000 + AlphaPermille) / 30</c> formula, equality with Sim
/// Core F1 (<see cref="TickTime.TicksToMsFloor"/>) on tick boundaries, the alpha taken from a real <see cref="TickDriver"/>
/// fed integer timestamps, the <c>IsValid</c> passthrough, the pre-sample defaults and 0 B per sample.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class RenderClockTests
{
    private const long Freq = 10_000_000;

    private sealed class FakeHostClock : IHostClock
    {
        public uint EstimatedHostTick { get; set; }

        public bool IsValid { get; set; }
    }

    [Theory]
    [InlineData(0u, 0, 0L)]
    [InlineData(0u, 29, 0L)]
    [InlineData(0u, 30, 1L)]
    [InlineData(0u, 999, 33L)]
    [InlineData(1u, 0, 33L)]
    [InlineData(1u, 500, 50L)]
    [InlineData(3u, 0, 100L)]
    [InlineData(30u, 0, 1000L)]
    [InlineData(29u, 999, 999L)]
    [InlineData(uint.MaxValue, 999, ((long)uint.MaxValue * 1000 + 999) / 30)]
    public void Formula_IsIntegerFloor(uint tick, int alpha, long expectedMs)
    {
        // Arrange
        var host = new FakeHostClock { EstimatedHostTick = tick, IsValid = true };
        var driver = DriverAtAlpha(alpha);
        var clock = new RenderClock(host, driver);

        // Act
        clock.Sample();

        // Assert
        Assert.Equal(alpha, clock.AlphaPermille);
        Assert.Equal(expectedMs, clock.RenderSimTimeMs);
        Assert.Equal(expectedMs, RenderClock.ComputeRenderSimTimeMs(tick, alpha));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(29u)]
    [InlineData(30u)]
    [InlineData(31u)]
    [InlineData(9_000u)]
    [InlineData(2_147_483_647u)]
    public void TickBoundary_EqualsSimCoreF1DisplayTime(uint tick)
    {
        // Arrange: alpha 0 is a tick boundary.
        var host = new FakeHostClock { EstimatedHostTick = tick, IsValid = true };
        var clock = new RenderClock(host, DriverAtAlpha(0));

        // Act
        clock.Sample();

        // Assert
        Assert.Equal(TickTime.TicksToMsFloor(tick), clock.RenderSimTimeMs);
    }

    [Fact]
    public void Alpha_ComesFromARealDriver_FedIntegerTimestamps()
    {
        // Arrange: 1.5 tick periods after Start -> 1 tick runs, half a tick left over.
        var driver = new TickDriver(Freq, 4);
        driver.Start(0);
        int ticks = driver.Advance(Freq * 3 / 2 / TickRate.Hz);
        var host = new FakeHostClock { EstimatedHostTick = 7, IsValid = true };
        var clock = new RenderClock(host, driver);

        // Act
        clock.Sample();

        // Assert
        Assert.Equal(1, ticks);
        Assert.Equal(500, driver.AlphaPermille);
        Assert.Equal(500, clock.AlphaPermille);
        Assert.Equal((7L * 1000 + 500) / 30, clock.RenderSimTimeMs);
    }

    [Fact]
    public void PausedDriver_SamplesAlphaZero()
    {
        // Arrange
        var driver = DriverAtAlpha(500);
        driver.Pause();
        var clock = new RenderClock(new FakeHostClock { EstimatedHostTick = 3, IsValid = true }, driver);

        // Act
        clock.Sample();

        // Assert
        Assert.Equal(0, clock.AlphaPermille);
        Assert.Equal(100L, clock.RenderSimTimeMs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsValid_IsPassedThrough(bool valid)
    {
        // Arrange
        var clock = new RenderClock(new FakeHostClock { EstimatedHostTick = 5, IsValid = valid }, DriverAtAlpha(0));

        // Act
        clock.Sample();

        // Assert
        Assert.Equal(valid, clock.IsValid);
    }

    [Fact]
    public void BeforeFirstSample_AllValuesAreDefault()
    {
        // Arrange
        var clock = new RenderClock(new FakeHostClock { EstimatedHostTick = 42, IsValid = true }, DriverAtAlpha(500));

        // Assert
        Assert.Equal(0u, clock.HostTick);
        Assert.Equal(0, clock.AlphaPermille);
        Assert.Equal(0L, clock.RenderSimTimeMs);
        Assert.False(clock.IsValid);
    }

    [Fact]
    public void Sample_FreezesValuesUntilTheNextSample()
    {
        // Arrange
        var host = new FakeHostClock { EstimatedHostTick = 10, IsValid = true };
        var clock = new RenderClock(host, DriverAtAlpha(0));
        clock.Sample();

        // Act
        host.EstimatedHostTick = 11;
        host.IsValid = false;

        // Assert: unchanged until sampled again.
        Assert.Equal(10u, clock.HostTick);
        Assert.True(clock.IsValid);
        clock.Sample();
        Assert.Equal(11u, clock.HostTick);
        Assert.False(clock.IsValid);
    }

    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => new RenderClock(null!, DriverAtAlpha(0)));
        Assert.Throws<ArgumentNullException>(() => new RenderClock(new FakeHostClock(), null!));
    }

    [Fact]
    public void Sample_AllocatesZeroBytes()
    {
        // Arrange
        var host = new FakeHostClock { EstimatedHostTick = 1, IsValid = true };
        var clock = new RenderClock(host, DriverAtAlpha(250));

        // Act + Assert
        AllocAssert.Zero("RenderClock.Sample x 1000", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                clock.Sample();
            }
        });
        Assert.Equal(250, clock.AlphaPermille);
    }

    /// <summary>A running driver whose accumulator sits at exactly <paramref name="alpha"/> permille.</summary>
    private static TickDriver DriverAtAlpha(int alpha)
    {
        // acc30 = elapsed * 30 lands in [target, target + 29]; with Freq = 10^7 one permille is 10^4 units, so the
        // floor acc30 * 1000 / Freq is exactly alpha. No tick is due (alpha < 1000).
        var driver = new TickDriver(Freq, 4);
        driver.Start(0);
        long acc30Target = alpha * Freq / 1000;
        long elapsed = (acc30Target + TickRate.Hz - 1) / TickRate.Hz;
        driver.Advance(elapsed);
        Assert.Equal(alpha, driver.AlphaPermille);
        return driver;
    }
}
