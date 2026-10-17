using System.Numerics;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The set of keyboard modifiers held at a press (GDD R10: chords are a modifier plus one input, keyboard-and-mouse only).
/// Shift is the only M-1a modifier; Ctrl and Alt append bits when a story needs them. Values are append-only.
/// </summary>
/// <example>
/// <code>
/// ModifierMask held = ModifierMasks.FromInput(PhysicalInput.KeyShift);   // ModifierMask.Shift
/// ushort action = ChordResolver.Resolve(held, PhysicalInput.KeyEnter, stateId, map);
/// </code>
/// </example>
[Flags]
public enum ModifierMask : byte
{
    /// <summary>No modifier held.</summary>
    None = 0,

    /// <summary>Shift (either side), <see cref="PhysicalInput.KeyShift"/>.</summary>
    Shift = 1,
}

/// <summary>
/// Helpers over <see cref="ModifierMask"/>: which physical inputs are modifiers, a mask's specificity (its bit count, the
/// "most specific chord" measure) and its text form. Pure, 0 B (except <see cref="ToText"/>); thread-safe.
/// </summary>
/// <example>
/// <code>
/// bool mod = ModifierMasks.IsModifier(PhysicalInput.KeyShift);           // true
/// int spec = ModifierMasks.Specificity(ModifierMask.Shift);              // 1
/// PhysicalInput key = ModifierMasks.KeyOf(ModifierMask.Shift);           // KeyShift
/// </code>
/// </example>
public static class ModifierMasks
{
    /// <summary>Every modifier bit defined at M-1a.</summary>
    public const ModifierMask All = ModifierMask.Shift;

    /// <summary>The modifier bit of <paramref name="input"/>, or <see cref="ModifierMask.None"/> when it is not a modifier.</summary>
    /// <param name="input">The physical input.</param>
    /// <returns>The single bit, or None.</returns>
    /// <example><code>ModifierMask m = ModifierMasks.FromInput(PhysicalInput.KeyShift);</code></example>
    public static ModifierMask FromInput(PhysicalInput input) => input == PhysicalInput.KeyShift ? ModifierMask.Shift : ModifierMask.None;

    /// <summary>True when <paramref name="input"/> is a chord modifier.</summary>
    /// <param name="input">The physical input.</param>
    /// <returns>Whether it is a modifier.</returns>
    /// <example><code>bool mod = ModifierMasks.IsModifier(PhysicalInput.KeyE);   // false</code></example>
    public static bool IsModifier(PhysicalInput input) => FromInput(input) != ModifierMask.None;

    /// <summary>The physical input of a single modifier bit; <see cref="PhysicalInput.None"/> for anything else.</summary>
    /// <param name="single">One modifier bit.</param>
    /// <returns>The input.</returns>
    /// <example><code>PhysicalInput shift = ModifierMasks.KeyOf(ModifierMask.Shift);</code></example>
    public static PhysicalInput KeyOf(ModifierMask single) => single == ModifierMask.Shift ? PhysicalInput.KeyShift : PhysicalInput.None;

    /// <summary>How specific a chord is: the number of modifiers it needs. A plain binding is 0.</summary>
    /// <param name="mask">The mask.</param>
    /// <returns>The bit count.</returns>
    /// <example><code>int spec = ModifierMasks.Specificity(ModifierMask.None);   // 0</code></example>
    public static int Specificity(ModifierMask mask) => BitOperations.PopCount((uint)mask);

    /// <summary>Text form for reports, for example <c>Key.Shift+</c>; empty for None. Allocates.</summary>
    /// <param name="mask">The mask.</param>
    /// <returns>The prefix text.</returns>
    /// <example><code>string text = ModifierMasks.ToText(ModifierMask.Shift) + "Key.Enter";</code></example>
    public static string ToText(ModifierMask mask) => (mask & ModifierMask.Shift) != 0 ? "Key.Shift+" : string.Empty;
}
