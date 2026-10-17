using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// What the stage 4 to 5 runner needs from a built content set, and nothing more. It is the seam that keeps the
/// rule mechanism independent of the def records: story 008's <c>ContentSet</c> (or an adapter around it) implements
/// it, and rules downcast through <see cref="RuleSpec{TInput}"/>'s type argument. Implementations are immutable and
/// must list defs in a deterministic order.
/// </summary>
/// <example>
/// <code>
/// sealed class TinySet : IValidationInput
/// {
///     public ImmutableArray&lt;DefRecord&gt; Defs { get; init; }
///     public ImmutableArray&lt;FailedInput&gt; PriorFailures =&gt; ImmutableArray&lt;FailedInput&gt;.Empty;
/// }
/// </code>
/// </example>
public interface IValidationInput
{
    /// <summary>Every def, with its location and its <c>suppress</c> entries.</summary>
    ImmutableArray<DefRecord> Defs { get; }

    /// <summary>Inputs that failed stages 1 to 3 (known to the loader), so rules that declare them are skipped and counted.</summary>
    ImmutableArray<FailedInput> PriorFailures { get; }
}

/// <summary>One def as the runner sees it: its address, where it is written and its <c>suppress</c> entries.</summary>
/// <param name="Def">The def's type and id.</param>
/// <param name="Location">Where the def starts (<c>0:0</c> for built-in content).</param>
/// <param name="Suppress">The def's <c>suppress</c> entries (empty when none).</param>
/// <example>
/// <code>
/// var rec = new DefRecord(new DefRef(DefType.Item, "stamp"), SourceLocation.BuiltIn("builtin/items/stamp"), ImmutableArray&lt;SuppressEntry&gt;.Empty);
/// </code>
/// </example>
public sealed record DefRecord(DefRef Def, SourceLocation Location, ImmutableArray<SuppressEntry> Suppress);

/// <summary>
/// An input (a field such as <c>item.maxStack</c>, or a file) that failed an earlier stage. A rule whose declared
/// inputs include <see cref="Input"/> is skipped for <see cref="Def"/>, or entirely when <see cref="Def"/> is
/// <see cref="DefRef.None"/>, and the skipped (rule, def) pair is counted with this blocking field and file.
/// </summary>
/// <param name="Input">The input key as rules declare it (the blocking field).</param>
/// <param name="Def">The def the failure applies to, or <see cref="DefRef.None"/> for the whole file or set.</param>
/// <param name="File">The blocking file path; empty when not file-based.</param>
/// <example>
/// <code>
/// var f = new FailedInput("item.maxStack", DefRef.None, "items/core.jsonc");   // stage-1 syntax error in that file
/// </code>
/// </example>
public sealed record FailedInput(string Input, DefRef Def, string File);
