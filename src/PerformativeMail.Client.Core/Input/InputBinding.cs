using System.Text;

namespace PerformativeMail.Client.Core.Input;

/// <summary>The shape of an <see cref="InputBinding"/>. <c>0</c> is "missing" so that <c>default(InputBinding)</c> fails validation.</summary>
/// <example><code>bool chord = binding.Kind == BindingKind.Chord;</code></example>
public enum BindingKind : byte
{
    /// <summary>Not set (<c>default</c>). A validation failure.</summary>
    Missing = 0,

    /// <summary>Deliberately bound to nothing.</summary>
    Unbound = 1,

    /// <summary>One input.</summary>
    Single = 2,

    /// <summary>A modifier held while a key or button is pressed (Shift + left mouse button).</summary>
    Chord = 3,

    /// <summary>Four digital inputs forming a 2D direction (WASD, arrow keys, D-pad).</summary>
    Composite4 = 4,

    /// <summary>Two analogue axes forming a 2D vector (a stick's X and Y).</summary>
    AxisPair = 5,

    /// <summary>A signed-step pair: one input steps backwards, the other forwards (hotbar wheel, D-pad left / right).</summary>
    StepPair = 6,
}

/// <summary>
/// One default binding slot of an <see cref="ActionDef"/> (ADR-0012 section 2): a single input, a modifier chord, a 2D
/// composite (four digital inputs or a stick's X / Y pair), an explicit <see cref="Unbound"/>, or the <c>default</c> value
/// which means "missing" and fails validation. A binding may also carry the NumpadEnter alias flag: the numpad Enter key acts
/// as the main Enter wherever this binding uses <see cref="PhysicalInput.KeyEnter"/>, without taking up a slot of its own. A
/// binding may also be flagged as a <b>Back-ladder step</b> (GDD R5, input-controls story 006): the binding is the same
/// physical input as Back and is reached through Back's ordered handlers, so the conflict validator and
/// <see cref="ChordResolver"/> count it as Back (R4.1 "Back counted as one action"), never as a second live action.
/// Immutable value type; thread-safe.
/// </summary>
/// <example>
/// <code>
/// InputBinding jump = InputBinding.Single(PhysicalInput.KeySpace);
/// InputBinding quick = InputBinding.Chord(PhysicalInput.KeyShift, PhysicalInput.MouseLeft);
/// InputBinding wasd = InputBinding.Composite4(PhysicalInput.KeyW, PhysicalInput.KeyS, PhysicalInput.KeyA, PhysicalInput.KeyD);
/// InputBinding ls = InputBinding.AxisPair(PhysicalInput.PadLeftStickX, PhysicalInput.PadLeftStickY);
/// InputBinding wheel = InputBinding.StepPair(PhysicalInput.WheelUp, PhysicalInput.WheelDown);
/// InputBinding enter = InputBinding.Single(PhysicalInput.KeyEnter).WithNumpadEnterAlias();
/// InputBinding closeB = InputBinding.Single(PhysicalInput.PadB).WithBackLadderStep();
/// InputBinding none = InputBinding.Unbound;
/// </code>
/// </example>
public readonly struct InputBinding : IEquatable<InputBinding>
{
    private readonly PhysicalInput _a;
    private readonly PhysicalInput _b;
    private readonly PhysicalInput _c;
    private readonly PhysicalInput _d;
    private readonly byte _flags;

    private const byte NumpadEnterFlag = 1;
    private const byte BackLadderStepFlag = 2;

    private InputBinding(BindingKind kind, PhysicalInput a, PhysicalInput b, PhysicalInput c, PhysicalInput d, byte flags)
    {
        Kind = kind;
        _a = a;
        _b = b;
        _c = c;
        _d = d;
        _flags = flags;
    }

    /// <summary>The explicit "bound to nothing" binding.</summary>
    public static InputBinding Unbound => new(BindingKind.Unbound, PhysicalInput.None, PhysicalInput.None, PhysicalInput.None, PhysicalInput.None, 0);

    /// <summary>The shape of this binding.</summary>
    public BindingKind Kind { get; }

    /// <summary>True when the numpad Enter key also triggers this binding (the alias of <see cref="PhysicalInput.KeyEnter"/>).</summary>
    public bool AcceptsNumpadEnter => (_flags & NumpadEnterFlag) != 0;

    /// <summary>
    /// True when this binding is a step of the Back ladder (GDD R5): the same input as one of Back's bindings on the same
    /// device, handled by Back's ordered handlers. Counted as Back by the conflict validator and <see cref="ChordResolver"/>.
    /// </summary>
    public bool IsBackLadderStep => (_flags & BackLadderStepFlag) != 0;

    /// <summary>True for <c>default</c>: the slot was never filled in.</summary>
    public bool IsMissing => Kind == BindingKind.Missing;

    /// <summary>True for the explicit <see cref="Unbound"/>.</summary>
    public bool IsUnbound => Kind == BindingKind.Unbound;

    /// <summary>The input of a <see cref="BindingKind.Single"/> binding.</summary>
    public PhysicalInput Input => _a;

    /// <summary>The held modifier of a <see cref="BindingKind.Chord"/>.</summary>
    public PhysicalInput Modifier => _a;

    /// <summary>The key or button pressed while the modifier is held, of a <see cref="BindingKind.Chord"/>.</summary>
    public PhysicalInput Key => _b;

    /// <summary>The up input of a <see cref="BindingKind.Composite4"/>.</summary>
    public PhysicalInput Up => _a;

    /// <summary>The down input of a <see cref="BindingKind.Composite4"/>.</summary>
    public PhysicalInput Down => _b;

    /// <summary>The left input of a <see cref="BindingKind.Composite4"/>.</summary>
    public PhysicalInput Left => _c;

    /// <summary>The right input of a <see cref="BindingKind.Composite4"/>.</summary>
    public PhysicalInput Right => _d;

    /// <summary>The X axis of a <see cref="BindingKind.AxisPair"/>.</summary>
    public PhysicalInput AxisX => _a;

    /// <summary>The Y axis of a <see cref="BindingKind.AxisPair"/>.</summary>
    public PhysicalInput AxisY => _b;

    /// <summary>The input that steps backwards (-1) in a <see cref="BindingKind.StepPair"/>.</summary>
    public PhysicalInput Previous => _a;

    /// <summary>The input that steps forwards (+1) in a <see cref="BindingKind.StepPair"/>.</summary>
    public PhysicalInput Next => _b;

    /// <summary>How many physical inputs this binding names: 1 single, 2 chord or axis pair, 4 composite, 0 otherwise.</summary>
    public int InputCount => Kind switch
    {
        BindingKind.Single => 1,
        BindingKind.Chord or BindingKind.AxisPair or BindingKind.StepPair => 2,
        BindingKind.Composite4 => 4,
        _ => 0,
    };

    /// <summary>A single-input binding.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The binding.</returns>
    /// <example><code>var e = InputBinding.Single(PhysicalInput.KeyE);</code></example>
    public static InputBinding Single(PhysicalInput input) => new(BindingKind.Single, input, PhysicalInput.None, PhysicalInput.None, PhysicalInput.None, 0);

    /// <summary>A modifier chord: <paramref name="modifier"/> held while <paramref name="key"/> is pressed.</summary>
    /// <param name="modifier">The held modifier.</param>
    /// <param name="key">The key or button.</param>
    /// <returns>The binding.</returns>
    /// <example><code>var c = InputBinding.Chord(PhysicalInput.KeyShift, PhysicalInput.MouseLeft);</code></example>
    public static InputBinding Chord(PhysicalInput modifier, PhysicalInput key) => new(BindingKind.Chord, modifier, key, PhysicalInput.None, PhysicalInput.None, 0);

    /// <summary>A four-input 2D composite.</summary>
    /// <param name="up">Up.</param>
    /// <param name="down">Down.</param>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>The binding.</returns>
    /// <example><code>var wasd = InputBinding.Composite4(PhysicalInput.KeyW, PhysicalInput.KeyS, PhysicalInput.KeyA, PhysicalInput.KeyD);</code></example>
    public static InputBinding Composite4(PhysicalInput up, PhysicalInput down, PhysicalInput left, PhysicalInput right) => new(BindingKind.Composite4, up, down, left, right, 0);

    /// <summary>A stick-style 2D vector from two analogue axes.</summary>
    /// <param name="x">The X axis.</param>
    /// <param name="y">The Y axis.</param>
    /// <returns>The binding.</returns>
    /// <example><code>var ls = InputBinding.AxisPair(PhysicalInput.PadLeftStickX, PhysicalInput.PadLeftStickY);</code></example>
    public static InputBinding AxisPair(PhysicalInput x, PhysicalInput y) => new(BindingKind.AxisPair, x, y, PhysicalInput.None, PhysicalInput.None, 0);

    /// <summary>
    /// The signed-step binding: <paramref name="previous"/> steps by -1 and <paramref name="next"/> by +1. One slot carries both
    /// directions, because the frame delivers a single signed step count, not two actions.
    /// </summary>
    /// <param name="previous">The input that steps backwards.</param>
    /// <param name="next">The input that steps forwards.</param>
    /// <returns>The binding.</returns>
    /// <example><code>var wheel = InputBinding.StepPair(PhysicalInput.WheelUp, PhysicalInput.WheelDown);</code></example>
    public static InputBinding StepPair(PhysicalInput previous, PhysicalInput next) => new(BindingKind.StepPair, previous, next, PhysicalInput.None, PhysicalInput.None, 0);

    /// <summary>A copy of this binding that the numpad Enter key also triggers. Valid only where the binding uses <see cref="PhysicalInput.KeyEnter"/>.</summary>
    /// <returns>The aliased binding.</returns>
    /// <example><code>var enter = InputBinding.Single(PhysicalInput.KeyEnter).WithNumpadEnterAlias();</code></example>
    public InputBinding WithNumpadEnterAlias() => new(Kind, _a, _b, _c, _d, (byte)(_flags | NumpadEnterFlag));

    /// <summary>
    /// A copy of this binding flagged as a Back-ladder step (GDD R5): the press is Back's, consumed by the first applicable
    /// handler. Valid only on a <see cref="BindingKind.Single"/> binding whose input is one of Back's bindings on the same device
    /// (the validator reports <c>LADDER_STEP</c> otherwise).
    /// </summary>
    /// <returns>The flagged binding.</returns>
    /// <example><code>var closeB = InputBinding.Single(PhysicalInput.PadB).WithBackLadderStep();</code></example>
    public InputBinding WithBackLadderStep() => new(Kind, _a, _b, _c, _d, (byte)(_flags | BackLadderStepFlag));

    /// <summary>The physical input at position <paramref name="index"/> (0 to <see cref="InputCount"/> - 1), in declaration order.</summary>
    /// <param name="index">The position.</param>
    /// <returns>The input.</returns>
    /// <example><code>for (int i = 0; i &lt; b.InputCount; i++) { PhysicalInput p = b.InputAt(i); }</code></example>
    public PhysicalInput InputAt(int index)
    {
        if ((uint)index >= (uint)InputCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index switch { 0 => _a, 1 => _b, 2 => _c, _ => _d };
    }

    /// <summary>True when <paramref name="input"/> is one of this binding's inputs.</summary>
    /// <param name="input">The input.</param>
    /// <returns>Whether it is named by this binding.</returns>
    /// <example><code>bool usesEsc = binding.Contains(PhysicalInput.KeyEscape);</code></example>
    public bool Contains(PhysicalInput input)
    {
        int n = InputCount;
        for (int i = 0; i < n; i++)
        {
            if (InputAt(i) == input)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the shape is complete: a bound kind, every input a named <see cref="PhysicalInput"/>, no input repeated within
    /// the binding. <see cref="Unbound"/> is well formed; <c>default</c> is not.
    /// </summary>
    /// <returns>Whether the binding is well formed.</returns>
    /// <example><code>if (!binding.IsWellFormed()) { /* reject */ }</code></example>
    public bool IsWellFormed()
    {
        if (Kind == BindingKind.Unbound)
        {
            return true;
        }

        int n = InputCount;
        if (n == 0)
        {
            return false;
        }

        for (int i = 0; i < n; i++)
        {
            PhysicalInput p = InputAt(i);
            if (p == PhysicalInput.None || !PhysicalInputNames.TryGetName(p, out _))
            {
                return false;
            }

            for (int j = 0; j < i; j++)
            {
                if (InputAt(j) == p)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(InputBinding other) =>
        Kind == other.Kind && _a == other._a && _b == other._b && _c == other._c && _d == other._d
        && _flags == other._flags;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InputBinding other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int h = (int)Kind;
            h = (h * 31) + (ushort)_a;
            h = (h * 31) + (ushort)_b;
            h = (h * 31) + (ushort)_c;
            h = (h * 31) + (ushort)_d;
            return (h * 31) + _flags;
        }
    }

    /// <summary>Value equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(InputBinding left, InputBinding right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(InputBinding left, InputBinding right) => !left.Equals(right);

    /// <summary>A readable form such as <c>Key.Shift+Mouse.Left</c> or <c>Key.W/Key.S/Key.A/Key.D</c>, for logs and test messages. Allocates.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        if (Kind == BindingKind.Missing)
        {
            return "Missing";
        }

        if (Kind == BindingKind.Unbound)
        {
            return "Unbound";
        }

        var sb = new StringBuilder();
        string separator = Kind == BindingKind.Chord ? "+" : Kind == BindingKind.Composite4 ? "/" : ",";
        int n = InputCount;
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }

            PhysicalInput p = InputAt(i);
            sb.Append(PhysicalInputNames.TryGetName(p, out string name) ? name : $"0x{(ushort)p:X4}");
        }

        if (AcceptsNumpadEnter)
        {
            sb.Append(" (+NumpadEnter)");
        }

        if (IsBackLadderStep)
        {
            sb.Append(" (Back step)");
        }

        return sb.ToString();
    }
}
