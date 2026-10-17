using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content;

/// <summary>One kind's share of a shift's spawn mix, in permille (a shift's shares sum to exactly 1000, CV-MAIL-02).</summary>
/// <param name="Kind">The <see cref="MailKindDef"/> index.</param>
/// <param name="SharePm">Share in permille.</param>
/// <example>
/// <code>
/// var share = new MailShare(letterIndex, 600);
/// </code>
/// </example>
public readonly record struct MailShare(DefIndex Kind, int SharePm) : IDefLeaf;
