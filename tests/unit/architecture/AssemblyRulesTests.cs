using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Sim;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Assembly-boundary guards for the Sim and for PerformativeMail.Client.Core (ADR-0001 section 8,
/// TR-sim-042 assembly half), checked from compiled metadata.
/// <para>
/// "Framework assembly" means inbox: the reference resolves to a file in the running shared
/// framework directory. A runtime NuGet package named <c>System.*</c> lands in the application
/// directory instead, so it is rejected (tech-debt TD-002). A second, independent check reads the
/// test project's <c>deps.json</c> and requires that the Sim has no dependency at all and
/// Client.Core depends on the Sim only; that also catches a package that is referenced but whose
/// types are never used (which the compiler would omit from the metadata references).
/// </para>
/// </summary>
[Trait("Id", "SC-ARCH-1")]
public class AssemblyRulesTests
{
    private const string SimName = "PerformativeMail.Sim";
    private const string ClientCoreName = "PerformativeMail.Client.Core";

    private static readonly Assembly _simAssemblyUnderTest = typeof(SimAssembly).Assembly;
    private static readonly Assembly _clientCoreAssemblyUnderTest = typeof(LoopbackTransport).Assembly;

    private static AssemblyName[] ReferencesOf(Assembly assembly) => assembly.GetReferencedAssemblies();

    private static string[] ReferencedNames(Assembly assembly) =>
        ReferencesOf(assembly)
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

    // ----------------------------------------------------------------- Sim

    [Fact]
    public void SimSource_ContainsNoUsingGodot()
    {
        // Arrange: SC-ARCH-1 row (a), the source half of the CI grep guard (ADR-0001 section 8), run inside the suite.
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PerformativeMail.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        string simSource = Path.Combine(root.FullName, "src", SimName);

        // Act
        string[] offenders = Directory.EnumerateFiles(simSource, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadLines(f).Any(line => line.Contains("using Godot", StringComparison.Ordinal)))
            .ToArray();

        // Assert
        Assert.Empty(offenders);
    }

    [Fact]
    public void Sim_References_NoGodotAssembly()
    {
        // Arrange
        string[] names = ReferencedNames(_simAssemblyUnderTest);

        // Act
        string[] godotReferences = names
            .Where(name => name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Assert
        Assert.Empty(godotReferences);
    }

    [Fact]
    public void Sim_References_NotClientCore()
    {
        // Arrange
        string[] names = ReferencedNames(_simAssemblyUnderTest);

        // Act
        bool referencesClientCore = names.Contains(ClientCoreName, StringComparer.Ordinal);

        // Assert
        Assert.False(referencesClientCore);
    }

    [Fact]
    public void Sim_References_FrameworkAssembliesOnly()
    {
        // Arrange
        AssemblyName[] references = ReferencesOf(_simAssemblyUnderTest);

        // Act
        string[] nonFramework = references
            .Where(reference => !IsInboxAssembly(reference))
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        // Assert
        Assert.True(
            nonFramework.Length == 0,
            "Sim references non-framework assemblies: " + string.Join(", ", nonFramework));
    }

    [Fact]
    public void Sim_DepsJson_HasNoDependencies()
    {
        // Arrange
        string[] dependencies = DependenciesOf(SimName);

        // Act - read from the test project's deps.json

        // Assert
        Assert.True(
            dependencies.Length == 0,
            "Sim has runtime dependencies (a runtime package?): " + string.Join(", ", dependencies));
    }

    // ----------------------------------------------------------- Client.Core

    [Fact]
    public void ClientCore_References_NoGodotAssembly()
    {
        // Arrange
        string[] names = ReferencedNames(_clientCoreAssemblyUnderTest);

        // Act
        string[] godotReferences = names
            .Where(name => name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Assert
        Assert.Empty(godotReferences);
    }

    [Fact]
    public void ClientCore_References_SimAndFrameworkAssembliesOnly()
    {
        // Arrange
        AssemblyName[] references = ReferencesOf(_clientCoreAssemblyUnderTest);

        // Act
        string[] unexpected = references
            .Where(reference => reference.Name != SimName && !IsInboxAssembly(reference))
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        // Assert
        Assert.True(
            unexpected.Length == 0,
            "Client.Core references assemblies other than the Sim and the framework: " + string.Join(", ", unexpected));
    }

    [Fact]
    public void ClientCore_DepsJson_DependsOnTheSimOnly()
    {
        // Arrange
        string[] dependencies = DependenciesOf(ClientCoreName);

        // Act - read from the test project's deps.json

        // Assert - the exact set, so it is also the non-vacuous proof that the Sim reference exists:
        // the compiler drops an unused reference from metadata (Client.Core uses no Sim type in a
        // Release build, where SimAssert calls are compiled out), so the metadata rule above cannot.
        Assert.Equal(new[] { SimName }, dependencies);
    }

    // ------------------------------------------------- classifier (TD-002)

    [Theory]
    [InlineData("System.Runtime", true)]
    [InlineData("System.Private.CoreLib", true)]
    [InlineData("System.Collections", true)]
    [InlineData("netstandard", true)]
    [InlineData("mscorlib", true)]
    [InlineData("Microsoft.Win32.Primitives", true)]
    [InlineData("System.Reactive", false)]
    [InlineData("System.Linq.Async", false)]
    [InlineData("Godot.SourceGenerators", false)]
    [InlineData("Newtonsoft.Json", false)]
    [InlineData("Microsoft.Extensions.Logging", false)]
    [InlineData("xunit.v3.assert", false)]
    [InlineData("PerformativeMail.Sim", false)]
    [InlineData("PerformativeMail.Client.Core", false)]
    public void IsInboxAssembly_Name_ClassifiedAsExpected(string name, bool expected)
    {
        // Arrange - name from the theory data; System.Reactive / System.Linq.Async stand in for
        // a runtime package that merely borrows a System.* name (TD-002)
        var assemblyName = new AssemblyName(name);

        // Act
        bool actual = IsInboxAssembly(assemblyName);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// True when the reference resolves to a file inside the running shared-framework directory.
    /// An unresolvable reference, or one that resolves into the application directory (a NuGet
    /// package, a project reference), is not inbox, whatever its name.
    /// </summary>
    private static bool IsInboxAssembly(AssemblyName name)
    {
        Assembly loaded;
        try
        {
            loaded = Assembly.Load(name);
        }
        catch (Exception e) when (e is IOException or BadImageFormatException)
        {
            return false;
        }

        string location = loaded.Location;
        if (string.IsNullOrEmpty(location))
        {
            return false;
        }

        string assemblyDirectory = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(location) ?? string.Empty);
        string frameworkDirectory = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
        return string.Equals(assemblyDirectory, frameworkDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The names listed under <c>dependencies</c> for <paramref name="projectName"/> in the test
    /// project's deps.json (runtime packages and project references of that project). Fails when
    /// the project has no entry, so a missing file cannot read as "no dependencies".
    /// </summary>
    private static string[] DependenciesOf(string projectName)
    {
        string testAssemblyName = typeof(AssemblyRulesTests).Assembly.GetName().Name ?? string.Empty;
        string path = Path.Combine(AppContext.BaseDirectory, testAssemblyName + ".deps.json");
        Assert.True(File.Exists(path), "deps.json not found next to the test assembly: " + path);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement targets = document.RootElement.GetProperty("targets");
        foreach (JsonProperty target in targets.EnumerateObject())
        {
            foreach (JsonProperty library in target.Value.EnumerateObject())
            {
                int slash = library.Name.IndexOf('/');
                string libraryName = slash < 0 ? library.Name : library.Name.Substring(0, slash);
                if (!string.Equals(libraryName, projectName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!library.Value.TryGetProperty("dependencies", out JsonElement dependencies))
                {
                    return Array.Empty<string>();
                }

                return dependencies.EnumerateObject().Select(dependency => dependency.Name).ToArray();
            }
        }

        Assert.Fail("deps.json has no entry for " + projectName);
        return Array.Empty<string>();
    }
}
