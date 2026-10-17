using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The identity and shape of one stat source pushed through <see cref="IStatWriter"/>: a perk stack, a stamp or a run event.
/// <b>Identity</b> is <c>(Kind, Id, StackIndex, Owner)</c>; <see cref="MaxStacks"/>, <see cref="Lifetime"/> and
/// <see cref="Flags"/> describe the source and are not part of the identity. <see cref="MaxStacks"/> is carried here because
/// no PerkDef exists at M-1a (it is read from the def at M1). The writer stamps the identity and the lifetime onto every
/// <see cref="Modifier"/> it stores, so the modifiers passed to <see cref="IStatWriter.Push"/> need not repeat them.
/// </summary>
/// <param name="Kind">Perk, Stamp or RunEvent.</param>
/// <param name="Id">The def ordinal of the perk, stamp or event.</param>
/// <param name="StackIndex">Which stack of the source this is (0 for the first); must be below <paramref name="MaxStacks"/>.</param>
/// <param name="Owner">The team or one player slot; <c>default</c> is refused.</param>
/// <param name="MaxStacks">The source's stack limit; a push whose <paramref name="StackIndex"/> is at or above it is refused.</param>
/// <param name="Lifetime">Run or Shift: a tag only. The pushing system removes Shift sources at Payday; the sheet never expires one.</param>
/// <param name="Flags">The rule flags this source holds while live (none by default).</param>
/// <example>
/// <code>
/// var src = new ModifierSource(SourceKind.Perk, DefIndex.FromValue(3), 0, ModifierOwner.ForSlot(slot), 3, ModifierLifetime.Run);
/// writer.Push(in src, [legs]);
/// writer.Remove(in src);
/// </code>
/// </example>
public readonly record struct ModifierSource(
    SourceKind Kind,
    DefIndex Id,
    byte StackIndex,
    ModifierOwner Owner,
    byte MaxStacks,
    ModifierLifetime Lifetime,
    RuleFlagSet Flags = default);
