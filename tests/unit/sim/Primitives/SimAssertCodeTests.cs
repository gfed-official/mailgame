using System.Reflection;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers <see cref="SimAssertCode"/>: codes are append-only and identify one check
/// forever, so no two constants may share a value. Branches that append codes in
/// parallel and merge in either order would otherwise collide silently.
/// </summary>
public class SimAssertCodeTests
{
    [Fact]
    public void SimAssertCode_AllConstantValues_AreDistinct()
    {
        // Arrange
        FieldInfo[] constants = typeof(SimAssertCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .ToArray();

        // Act
        string[] duplicates = constants
            .GroupBy(f => Convert.ToInt64(f.GetRawConstantValue()))
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(f => f.Name))}")
            .ToArray();

        // Assert
        Assert.NotEmpty(constants);
        Assert.True(duplicates.Length == 0, "SimAssertCode values shared by more than one constant: " + string.Join("; ", duplicates));
    }
}
