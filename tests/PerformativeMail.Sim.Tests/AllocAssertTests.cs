namespace PerformativeMail.Sim.Tests;

/// <summary>Self-tests for <see cref="AllocAssert"/>: a gate that never fails is not a gate.</summary>
[Collection(NonParallelCollection.Name)]
public class AllocAssertTests
{
    private static object? _sink;

    [Fact]
    public void Zero_PerCallAllocation_FailsWithMessageNamingTheWindow()
    {
        // Arrange: one 24+ B object per run is a real, repeatable allocation.
        Action window = static () => _sink = new object();

        // Act
        Exception? thrown = Record.Exception(() => AllocAssert.Zero("per-call new object()", window));

        // Assert
        Assert.NotNull(thrown);
        Assert.Contains("per-call new object()", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"every one of {AllocAssert.Attempts} measured windows", thrown.Message, StringComparison.Ordinal);
        GC.KeepAlive(_sink);
    }

    [Fact]
    public void Measure_PerCallAllocation_RunsAllAttemptsAndReportsBytes()
    {
        // Arrange
        Action window = static () => _sink = new byte[100];

        // Act
        (long minBytes, int runs) = AllocAssert.Measure(window, warmUpRuns: 1);

        // Assert
        Assert.True(minBytes >= 100, $"expected >= 100 B, measured {minBytes} B");
        Assert.Equal(1 + AllocAssert.Attempts, runs);
    }

    [Fact]
    public void Zero_AllocationFreeWindow_PassesAndReportsRuns()
    {
        // Arrange
        long counter = 0;
        Action window = () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                counter += i;
            }
        };

        // Act
        int runs = AllocAssert.Zero("integer loop", window);

        // Assert: at least the warm-up runs plus one measured window, at most warm-up plus all attempts.
        Assert.InRange(runs, AllocAssert.DefaultWarmUpRuns + 1, AllocAssert.DefaultWarmUpRuns + AllocAssert.Attempts);
        Assert.Equal(runs * 499_500L, counter);
    }
}
