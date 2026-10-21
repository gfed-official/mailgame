namespace PerformativeMail.Game.Boot;

/// <summary>
/// An extension slot of the boot order (step 3 persistence, step 4 input): runs the registered
/// <see cref="IBootContributor"/>s whose <see cref="IBootContributor.Step"/> is this step, in registration order, logging
/// <c>BOOT &lt;n&gt; &lt;name&gt;: contributor &lt;Type&gt;</c> before each, or <c>BOOT &lt;n&gt; &lt;name&gt;: no contributor</c>
/// when none is registered (the slot then passes).
/// </summary>
/// <example><code>IBootStep input = new ContributorSlotStep(BootPlan.InputStep, "input", BootPlan.CreateContributors());</code></example>
public sealed class ContributorSlotStep : IBootStep
{
    private readonly IBootContributor[] _contributors;

    /// <summary>Creates the slot.</summary>
    /// <param name="number">The step number.</param>
    /// <param name="name">The step name.</param>
    /// <param name="contributors">All registered contributors; only those for <paramref name="number"/> run here.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contributors"/> or one of its entries is null.</exception>
    public ContributorSlotStep(int number, string name, IReadOnlyList<IBootContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        Number = number;
        Name = name;
        var mine = new List<IBootContributor>();
        foreach (IBootContributor c in contributors)
        {
            ArgumentNullException.ThrowIfNull(c, nameof(contributors));
            if (c.Step == number)
            {
                mine.Add(c);
            }
        }

        _contributors = [.. mine];
    }

    /// <inheritdoc />
    public int Number { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The contributors this slot runs, in order.</summary>
    public IReadOnlyList<IBootContributor> Contributors => _contributors;

    /// <inheritdoc />
    public BootStepResult Run(BootContext ctx)
    {
        if (_contributors.Length == 0)
        {
            ctx.Log.Write($"BOOT {Number} {Name}: no contributor");
            return BootStepResult.Ok;
        }

        foreach (IBootContributor c in _contributors)
        {
            ctx.Log.Write($"BOOT {Number} {Name}: contributor {c.GetType().Name}");
            c.Run(ctx);
        }

        return BootStepResult.Ok;
    }
}
