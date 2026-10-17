namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// One clamp bound of a <see cref="StatDef"/>: a code constant (correctness bound), a balance key named by its
/// <c>owner.key</c> string (design clamp, CD12 "Which clamp bounds are data"), or the stat's own base (a max only).
/// A key is held by NAME, not as a <c>BalanceKey</c> handle: handles are stamped per registry instance and the
/// <see cref="StatDefs"/> table is immutable code shared by every registry. The name is resolved against a
/// <c>BalanceTable</c>'s registry by <see cref="StatDef.ResolveBounds"/>. Immutable.
/// </summary>
/// <example>
/// <code>
/// StatBound code = StatBound.Constant(255);                      // MailMaxStack max
/// StatBound data = StatBound.Balance("throw.rangeMaxPermille");  // ThrowRangePermille max
/// </code>
/// </example>
public readonly record struct StatBound
{
    private readonly string? _key;

    private StatBound(StatBoundKind kind, int value, string? key)
    {
        Kind = kind;
        Value = value;
        _key = key;
    }

    /// <summary>Which source this bound uses.</summary>
    public StatBoundKind Kind { get; }

    /// <summary>The constant, when <see cref="Kind"/> is <see cref="StatBoundKind.Constant"/>; otherwise 0.</summary>
    public int Value { get; }

    /// <summary>The <c>owner.key</c> balance key name, when <see cref="Kind"/> is <see cref="StatBoundKind.Balance"/>; otherwise empty.</summary>
    public string Key => _key ?? string.Empty;

    /// <summary>A code constant bound.</summary>
    /// <param name="value">The bound.</param>
    public static StatBound Constant(int value) => new(StatBoundKind.Constant, value, null);

    /// <summary>A balance-key bound.</summary>
    /// <param name="key">The full <c>owner.key</c> name (<c>throw.rangeMinPermille</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or empty.</exception>
    public static StatBound Balance(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A balance bound needs a key name.", nameof(key));
        }

        return new StatBound(StatBoundKind.Balance, 0, key);
    }

    /// <summary>The stat's own base value as a bound (a max only).</summary>
    public static StatBound Base() => new(StatBoundKind.Base, 0, null);
}

/// <summary>
/// Where a <see cref="StatDef"/>'s base value comes from: a constant, a balance key by name, or (needing a content set,
/// resolved at StatSheet build, story 014) the container's grid rows or the mail kind's <c>maxStack</c>. A small
/// discriminated leaf; immutable.
/// </summary>
/// <example>
/// <code>
/// BaseSource neutral = BaseSource.Constant(1000);                 // a Mul stat's base
/// BaseSource tuned = BaseSource.Balance("mail.lateValueRatio");   // LateValueRatioPm
/// </code>
/// </example>
public readonly record struct BaseSource
{
    private readonly string? _key;

    private BaseSource(BaseSourceKind kind, int value, string? key)
    {
        Kind = kind;
        Value = value;
        _key = key;
    }

    /// <summary>Which source this is.</summary>
    public BaseSourceKind Kind { get; }

    /// <summary>The constant, when <see cref="Kind"/> is <see cref="BaseSourceKind.Constant"/>; otherwise 0.</summary>
    public int Value { get; }

    /// <summary>The <c>owner.key</c> name, when <see cref="Kind"/> is <see cref="BaseSourceKind.Balance"/>; otherwise empty.</summary>
    public string Key => _key ?? string.Empty;

    /// <summary>A code constant base.</summary>
    /// <param name="value">The base.</param>
    public static BaseSource Constant(int value) => new(BaseSourceKind.Constant, value, null);

    /// <summary>A balance-key base.</summary>
    /// <param name="key">The full <c>owner.key</c> name (<c>player.respawnMs</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or empty.</exception>
    public static BaseSource Balance(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A balance base needs a key name.", nameof(key));
        }

        return new BaseSource(BaseSourceKind.Balance, 0, key);
    }

    /// <summary>The qualifier container's grid rows.</summary>
    public static BaseSource ContainerRows() => new(BaseSourceKind.ContainerRows, 0, null);

    /// <summary>The qualifier mail kind's <c>maxStack</c>.</summary>
    public static BaseSource MailKindMaxStack() => new(BaseSourceKind.MailKindMaxStack, 0, null);
}
