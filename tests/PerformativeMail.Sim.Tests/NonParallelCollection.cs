namespace PerformativeMail.Sim.Tests;

/// <summary>
/// Name of the non-parallel xUnit collection that [PERF] and 0-allocation tests join (ADR-0001
/// section 7): nothing else runs while a member executes, so allocation and timing measurements
/// are not disturbed by other tests. Apply with <c>[Collection(NonParallelCollection.Name)]</c>.
/// </summary>
public static class NonParallelCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "NonParallel";
}

/// <summary>Defines the <see cref="NonParallelCollection.Name"/> collection with parallelisation disabled.</summary>
[CollectionDefinition(NonParallelCollection.Name, DisableParallelization = true)]
public sealed class NonParallelCollectionDefinition
{
}
