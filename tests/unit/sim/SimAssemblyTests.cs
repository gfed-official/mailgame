using System.Reflection;
using PerformativeMail.Sim;

namespace PerformativeMail.Sim.Tests.Unit.Sim;

/// <summary>
/// Smoke test for the test harness itself: proves the test project compiles
/// against, loads and can call into the PerformativeMail.Sim assembly. The
/// Godot-free guarantee lives in <c>AssemblyRulesTests</c>.
/// </summary>
public class SimAssemblyTests
{
    [Fact]
    public void SimAssembly_IsReferencedByTests_NameMatchesLoadedAssembly()
    {
        // Arrange
        Assembly simAssembly = typeof(SimAssembly).Assembly;

        // Act
        string? loadedName = simAssembly.GetName().Name;

        // Assert
        Assert.Equal(SimAssembly.Name, loadedName);
    }
}
