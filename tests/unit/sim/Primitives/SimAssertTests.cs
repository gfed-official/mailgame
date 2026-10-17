using System.Diagnostics;
using System.Reflection;
using PerformativeMail.Sim.Primitives;
using SimBuild = PerformativeMail.Sim.Sim;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers <see cref="SimAssert"/>, <see cref="SimAssertException"/> and
/// <c>Sim.AssertsEnabled</c> (TR-sim-040, assert half). Which expectation applies is
/// chosen by <see cref="TestBuildExpectation.SimAssertsExpected"/>, derived from
/// $(Optimize)/$(SimAsserts) - never from the configuration name. The test assembly
/// does NOT define SIM_ASSERT (the [Conditional] attribute binds at the call site and
/// call sites belong inside the Sim); asserts are exercised through the Sim-internal
/// SimAssertProbe, whose call sites are compiled with the Sim's own symbols.
/// </summary>
public class SimAssertTests
{
    private int _sideEffects;

    private bool CountedTrue()
    {
        _sideEffects++;
        return true;
    }

    private bool CountedFalse()
    {
        _sideEffects++;
        return false;
    }

    [Fact]
    public void AssertsEnabled_MatchesBuildExpectation()
    {
        // Arrange
        bool expected = TestBuildExpectation.SimAssertsExpected;

        // Act
        bool actual = SimBuild.AssertsEnabled;

        // Assert (true in Debug / ReleaseAssert, false in Release)
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void That_FalseCondition_ThrowsWithCodeOnlyWhenAssertsEnabled()
    {
        // Arrange
        const ushort Code = 1234;
        SimAssertException? thrown = null;

        // Act
        try
        {
            SimAssertProbe.That(() => false, Code);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(Code, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
        }
    }

    [Fact]
    public void Fail_Called_ThrowsWithCodeOnlyWhenAssertsEnabled()
    {
        // Arrange
        const ushort Code = 4321;
        SimAssertException? thrown = null;

        // Act
        try
        {
            SimAssertProbe.Fail(Code);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(Code, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
        }
    }

    [Fact]
    public void That_TrueCondition_DoesNotThrow()
    {
        // Arrange
        Exception? thrown = null;

        // Act
        try
        {
            SimAssertProbe.That(() => true, 7);
        }
        catch (Exception e)
        {
            thrown = e;
        }

        // Assert
        Assert.Null(thrown);
    }

    [Fact]
    public void That_ArgumentWithSideEffect_EvaluatedOnlyWhenAssertsEnabled()
    {
        // Arrange
        _sideEffects = 0;
        int expectedEvaluations = TestBuildExpectation.SimAssertsExpected ? 1 : 0;

        // Act
        SimAssertProbe.That(CountedTrue, 9);

        // Assert (compiled-out call site must leave the side effect unobserved)
        Assert.Equal(expectedEvaluations, _sideEffects);
    }

    [Fact]
    public void That_FalseArgumentWithSideEffect_ThrowsOnlyWhenAssertsEnabledAndEvaluatesOnce()
    {
        // Arrange
        _sideEffects = 0;
        bool threw = false;

        // Act
        try
        {
            SimAssertProbe.That(CountedFalse, 11);
        }
        catch (SimAssertException)
        {
            threw = true;
        }

        // Assert
        Assert.Equal(TestBuildExpectation.SimAssertsExpected, threw);
        Assert.Equal(TestBuildExpectation.SimAssertsExpected ? 1 : 0, _sideEffects);
    }

    [Theory]
    [InlineData(nameof(SimAssert.That))]
    [InlineData(nameof(SimAssert.Fail))]
    public void AssertMethod_Metadata_IsConditionalOnSimAssert(string methodName)
    {
        // Arrange
        MethodInfo method = typeof(SimAssert).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == methodName);

        // Act
        string[] conditions = method.GetCustomAttributes<ConditionalAttribute>()
            .Select(a => a.ConditionString)
            .ToArray();

        // Assert
        Assert.Equal(new[] { "SIM_ASSERT" }, conditions);
    }

    [Fact]
    public void AssertMethods_InvokedByReflection_ThrowRegardlessOfBuild()
    {
        // Arrange: [Conditional] strips call sites only; the method bodies always exist.
        MethodInfo that = typeof(SimAssert).GetMethod(nameof(SimAssert.That))!;
        MethodInfo fail = typeof(SimAssert).GetMethod(nameof(SimAssert.Fail))!;

        // Act
        TargetInvocationException thatEx = Assert.Throws<TargetInvocationException>(
            () => that.Invoke(null, new object[] { false, (ushort)5 }));
        TargetInvocationException failEx = Assert.Throws<TargetInvocationException>(
            () => fail.Invoke(null, new object[] { (ushort)6 }));

        // Assert
        Assert.Equal(5, Assert.IsType<SimAssertException>(thatEx.InnerException).Code);
        Assert.Equal(6, Assert.IsType<SimAssertException>(failEx.InnerException).Code);
    }

    [Fact]
    public void SimAssertException_Constructed_CarriesCodeAndMessage()
    {
        // Arrange
        const ushort Code = ushort.MaxValue;

        // Act
        var exception = new SimAssertException(Code);

        // Assert
        Assert.Equal(Code, exception.Code);
        Assert.Contains("65535", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<Exception>(exception);
    }
}
