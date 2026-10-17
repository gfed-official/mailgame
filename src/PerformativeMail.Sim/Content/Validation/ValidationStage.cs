namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// The five load stages a rule can belong to (ADR-0010 section 4, GDD CD21). Stages 1 to 3 run inside the loader over
/// parse state; stages 4 and 5 run over a built content set through <see cref="ContentValidationRunner"/>.
/// Values are append-only and ordered: a lower stage runs first and its failures can block rules of a higher stage.
/// </summary>
/// <example>
/// <code>
/// bool runsInRunner = rule.Stage is ValidationStage.References or ValidationStage.WholeSet;
/// </code>
/// </example>
public enum ValidationStage : byte
{
    /// <summary>Stage 1: syntax and file shape.</summary>
    Syntax = 1,

    /// <summary>Stage 2: schema (fields, types, enums, id lint, units, ranges).</summary>
    Schema = 2,

    /// <summary>Stage 3: build (sort ids, assign <c>DefIndex</c>, convert units).</summary>
    Build = 3,

    /// <summary>Stage 4: references and cross-def rules.</summary>
    References = 4,

    /// <summary>Stage 5: whole-set rules (shares, worst-case stat bounds).</summary>
    WholeSet = 5,
}
