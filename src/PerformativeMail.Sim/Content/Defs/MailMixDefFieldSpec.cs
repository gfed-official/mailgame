using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Content.Defs;

/// <summary>
/// The <see cref="MailMixDef"/> field table (content-data CD10, story 018), in canonical encode order (note: <c>spawnOverhead</c>
/// precedes the four ratios, as in <c>DefCanonical.WriteCanonical(MailMixDef)</c>). <c>shifts</c> is a list of
/// <c>{shift, shares}</c> objects whose <c>shares</c> map a mail kind id to a Ratio authored as <c>0.60</c> and stored as permille
/// (0 to 1000; the sum is CV-MAIL-02). The batch timings are durations. Defaults are neutral. Built fresh on each call.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpec&gt; rows = MailMixDefFieldSpec.Rows;   // id, shifts, batchIntervalSeconds, ...
/// </code>
/// </example>
public static class MailMixDefFieldSpec
{
    /// <summary>The rows, in canonical order; a new array on each call.</summary>
    public static ImmutableArray<FieldSpec> Rows => ImmutableArray.Create(
        FieldSpec.Id(),
        FieldSpec.ObjectList("shifts", ShiftRows),
        FieldSpec.Number("batchIntervalSeconds", FieldUnit.Seconds, min: 0, isDuration: true),
        FieldSpec.Number("batchJitterSeconds", FieldUnit.Seconds, min: 0, isDuration: true),
        FieldSpec.Number("tailGuardSeconds", FieldUnit.Seconds, min: 0, isDuration: true),
        FieldSpec.Number("openingRampBatches", FieldUnit.Count, required: false, min: 0, defaultValue: 0),
        FieldSpec.Number("spawnOverhead", FieldUnit.Ratio, min: 0),
        FieldSpec.Number("streetStreakRatio", FieldUnit.Ratio, min: 0),
        FieldSpec.Number("raidSpawnRateRatio", FieldUnit.Ratio, min: 0),
        FieldSpec.Number("distanceStepRatio", FieldUnit.Ratio, min: 0),
        FieldSpec.Number("shiftStepRatio", FieldUnit.Ratio, min: 0));

    private static ImmutableArray<FieldSpec> ShiftRows => ImmutableArray.Create(
        FieldSpec.Number("shift", FieldUnit.Count, min: 0),
        FieldSpec.RefMap("shares", nameof(MailKindDef), FieldUnit.Ratio, min: 0, max: 1000));
}
