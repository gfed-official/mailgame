using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Content.Defs;

/// <summary>
/// The <see cref="DestinationTypeDef"/> field table (content-data CD10, story 018), in canonical encode order. <c>insertRate</c>
/// is authored in items per second and stored x1000 (<see cref="FieldUnit.PerSecond"/>, <c>InsertRateMilliPerSec</c>:
/// <c>2</c> is stored 2000); <c>manualInsertHoldSeconds</c> is a duration (<c>0</c> = no Deliver anchor);
/// <c>catchVolume</c> is a required nested object whose dimensions are Metres (stored mm) and <c>yawDeg</c> Deg (stored
/// milli-degrees). Defaults are neutral. Built fresh on each call.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpec&gt; rows = DestinationTypeDefFieldSpec.Rows;   // id, accepts, insertRate, ...
/// </code>
/// </example>
public static class DestinationTypeDefFieldSpec
{
    /// <summary>The rows, in canonical order; a new array on each call.</summary>
    public static ImmutableArray<FieldSpec> Rows => ImmutableArray.Create(
        FieldSpec.Id(),
        FieldSpec.RefList("accepts", nameof(MailKindDef)),
        FieldSpec.Number("insertRate", FieldUnit.PerSecond, min: 0),
        FieldSpec.Number("manualInsertHoldSeconds", FieldUnit.Seconds, min: 0, isDuration: true),
        FieldSpec.Number("maxAutomatedFeeders", FieldUnit.Count, min: 0),
        FieldSpec.Bool("requiresUnit", defaultValue: false),
        FieldSpec.Object("catchVolume", CatchVolumeRows));

    private static ImmutableArray<FieldSpec> CatchVolumeRows => ImmutableArray.Create(
        FieldSpec.Number("widthMetres", FieldUnit.Metres, min: 0),
        FieldSpec.Number("heightMetres", FieldUnit.Metres, min: 0),
        FieldSpec.Number("depthMetres", FieldUnit.Metres, min: 0),
        FieldSpec.Number("centreHeightMetres", FieldUnit.Metres),
        FieldSpec.Number("yawDeg", FieldUnit.Deg));
}
