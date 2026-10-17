namespace PerformativeMail.Sim.Content.Hashing;

/// <summary>
/// The canonical encoder rejected a value it cannot represent (content-data F6): a list with more than 65 535 entries or a
/// string over 65 535 UTF-8 bytes (<see cref="CanonicalContentWriter.RangeRuleId"/>, CV-GEN-12), or a def id over 255 bytes
/// (CV-GEN-02). Encode-time rejection is an exception rather than a <c>ContentDiagnostic</c> because the encoder runs after
/// validation, on an already-accepted set, where this is a programming or loader-contract error; the loader (story 018)
/// reports the same rule id from its own range checks before a set can reach the encoder.
/// </summary>
/// <example>
/// <code>
/// try { writer.WriteCount(70_000); }
/// catch (ContentEncodingException e) { string rule = e.RuleId;   // "CV-GEN-12" }
/// </code>
/// </example>
public sealed class ContentEncodingException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="ruleId">The catalogue rule id the failure maps to.</param>
    /// <param name="message">What was too large.</param>
    /// <example><code>throw new ContentEncodingException("CV-GEN-12", "list too long");</code></example>
    public ContentEncodingException(string ruleId, string message)
        : base(ruleId + ": " + message)
    {
        RuleId = ruleId;
    }

    /// <summary>The content-rule id this failure maps to (for example <c>CV-GEN-12</c>).</summary>
    public string RuleId { get; }
}
