using System.Collections.Immutable;
using System.Text;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// One validation finding: which action, which field, why, and (for story 006 checks) the stable <see cref="Code"/>, the
/// <see cref="Severity"/>, every action involved, the router state and the physical input. The 005 field checks fill only
/// the positional members (code <see cref="ValidationCodes.Field"/>, severity Error).
/// </summary>
/// <param name="Action">The string id of the (first) offending action, or <c>#n</c> (its numeric id) when it has none.</param>
/// <param name="Field">The field that failed, one of <see cref="ValidationFields"/>.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="OtherAction">The second action in a conflict, or null.</param>
/// <param name="StateId">The router state id in which a conflict occurs, or null.</param>
/// <example>
/// <code>
/// var e = new ValidationEntry("drop", ValidationFields.Bindings, "Key.E reaches 2 live actions")
/// {
///     Code = ValidationCodes.ConflictColive, Input = PhysicalInput.KeyE, Actions = ["interact", "drop"],
/// };
/// </code>
/// </example>
public readonly record struct ValidationEntry(string Action, string Field, string Message, string? OtherAction = null, ushort? StateId = null)
{
    /// <summary>Error (the default), Warning or Info.</summary>
    public ValidationSeverity Severity { get; init; }

    /// <summary>The stable code, one of <see cref="ValidationCodes"/>.</summary>
    public string Code { get; init; } = ValidationCodes.Field;

    /// <summary>The physical input concerned, or <see cref="PhysicalInput.None"/>.</summary>
    public PhysicalInput Input { get; init; }

    /// <summary>The modifiers held with <see cref="Input"/> (a chord), or None.</summary>
    public ModifierMask Modifiers { get; init; }

    /// <summary>Every action involved, ascending by numeric id; default when the entry concerns one action only.</summary>
    public ImmutableArray<string> Actions { get; init; }

    /// <summary>The interactable target type (verb-grammar checks), or null.</summary>
    public string? Target { get; init; }
}

/// <summary>
/// The result of validating an action map (<see cref="ActionMapValidator"/>) or a verb-grammar table: every finding in the
/// order found, also split by severity into <see cref="Errors"/>, <see cref="Warnings"/> and <see cref="Infos"/>. A report
/// with no errors is a valid map. <see cref="ToString"/> serialises it to text for logs. Not thread-safe while being filled;
/// read-only use afterwards is safe.
/// </summary>
/// <example>
/// <code>
/// ValidationReport report = ActionMapValidator.Validate(map, ReachableSet.Default);
/// if (!report.IsValid) { throw new InvalidOperationException(report.ToString()); }
/// foreach (ValidationEntry info in report.Infos) { log.Info(info.Message); }
/// bool namesJump = report.Has("jump", ValidationFields.Grammar);
/// </code>
/// </example>
public sealed class ValidationReport
{
    private readonly List<ValidationEntry> _entries = [];
    private readonly List<ValidationEntry> _errors = [];
    private readonly List<ValidationEntry> _warnings = [];
    private readonly List<ValidationEntry> _infos = [];

    /// <summary>Every finding of every severity, in the order found.</summary>
    public IReadOnlyList<ValidationEntry> Entries => _entries;

    /// <summary>The failures, in the order found.</summary>
    public IReadOnlyList<ValidationEntry> Errors => _errors;

    /// <summary>The warnings, in the order found.</summary>
    public IReadOnlyList<ValidationEntry> Warnings => _warnings;

    /// <summary>The information lines, in the order found.</summary>
    public IReadOnlyList<ValidationEntry> Infos => _infos;

    /// <summary>True when there are no errors (warnings and infos do not invalidate).</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>Adds a field failure (severity Error, code <see cref="ValidationCodes.Field"/>).</summary>
    /// <param name="actionId">The offending action's string id (or <c>#n</c>).</param>
    /// <param name="field">The failing field.</param>
    /// <param name="message">The explanation.</param>
    /// <param name="otherAction">The second action of a conflict, or null.</param>
    /// <param name="stateId">The router state id of a conflict, or null.</param>
    /// <example><code>report.Add("jump", ValidationFields.Grammar, "no grammar");</code></example>
    public void Add(string actionId, string field, string message, string? otherAction = null, ushort? stateId = null) =>
        Add(new ValidationEntry(actionId, field, message, otherAction, stateId));

    /// <summary>Adds an entry under its severity.</summary>
    /// <param name="entry">The entry.</param>
    /// <example><code>report.Add(new ValidationEntry("jump", ValidationFields.Bindings, "shared") { Severity = ValidationSeverity.Info });</code></example>
    public void Add(ValidationEntry entry)
    {
        _entries.Add(entry);
        switch (entry.Severity)
        {
            case ValidationSeverity.Warning:
                _warnings.Add(entry);
                break;
            case ValidationSeverity.Info:
                _infos.Add(entry);
                break;
            default:
                _errors.Add(entry);
                break;
        }
    }

    /// <summary>True when an entry names <paramref name="actionId"/>, optionally restricted to <paramref name="field"/>.</summary>
    /// <param name="actionId">The action's string id (or <c>#n</c>).</param>
    /// <param name="field">The field, or null for any field.</param>
    /// <returns>Whether such an entry exists.</returns>
    /// <example><code>Assert.True(report.Has("jump", "OwnerGdd"));</code></example>
    public bool Has(string actionId, string? field = null)
    {
        foreach (ValidationEntry e in _entries)
        {
            if (e.Action == actionId && (field is null || e.Field == field))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when an error carries <paramref name="code"/>, optionally naming <paramref name="actionId"/> among its actions.</summary>
    /// <param name="code">One of <see cref="ValidationCodes"/>.</param>
    /// <param name="actionId">An action that must be involved, or null.</param>
    /// <returns>Whether such an error exists.</returns>
    /// <example><code>bool blocked = report.HasError(ValidationCodes.PadPath, "jump");</code></example>
    public bool HasError(string code, string? actionId = null)
    {
        foreach (ValidationEntry e in _errors)
        {
            if (e.Code == code && (actionId is null || e.Action == actionId || (!e.Actions.IsDefault && e.Actions.Contains(actionId))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One line per entry: <c>SEVERITY CODE action.Field: message</c>, then <c>(with other)</c>, <c>[actions]</c>,
    /// <c>(input ...)</c> and <c>(state ...)</c> when present. Allocates.
    /// </summary>
    /// <returns>The text; empty for an empty report.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (ValidationEntry e in _entries)
        {
            sb.Append(e.Severity switch { ValidationSeverity.Warning => "WARNING", ValidationSeverity.Info => "INFO", _ => "ERROR" })
                .Append(' ').Append(e.Code).Append(' ')
                .Append(e.Action).Append('.').Append(e.Field).Append(": ").Append(e.Message);
            if (e.OtherAction is not null)
            {
                sb.Append(" (with ").Append(e.OtherAction).Append(')');
            }

            if (!e.Actions.IsDefaultOrEmpty)
            {
                sb.Append(" [").Append(string.Join(", ", e.Actions)).Append(']');
            }

            if (e.Input != PhysicalInput.None)
            {
                sb.Append(" (input ").Append(ModifierMasks.ToText(e.Modifiers))
                    .Append(PhysicalInputNames.TryGetName(e.Input, out string name) ? name : $"0x{(ushort)e.Input:X4}").Append(')');
            }

            if (e.Target is not null)
            {
                sb.Append(" (target ").Append(e.Target).Append(')');
            }

            if (e.StateId is { } state)
            {
                sb.Append(" (state ").Append(RouterState.ToDebugString(state)).Append(')');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
