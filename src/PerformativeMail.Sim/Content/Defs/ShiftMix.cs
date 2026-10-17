using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content;

/// <summary>The spawn mix of one shift: the shift number and the per-kind shares.</summary>
/// <param name="Shift">The shift number.</param>
/// <param name="Shares">The shares, one per kind (fixed order as authored).</param>
/// <example>
/// <code>
/// var shift1 = new ShiftMix(1, ImmutableArray.Create(new MailShare(letterIndex, 600), new MailShare(smallIndex, 400)));
/// </code>
/// </example>
public readonly record struct ShiftMix(int Shift, ImmutableArray<MailShare> Shares) : IDefLeaf;
