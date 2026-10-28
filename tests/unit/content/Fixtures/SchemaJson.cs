using System.Collections.Immutable;
using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Small def files for the schema and build stages (content-data story 018). Each def is a list of <c>key: json</c> pairs that an
/// edit can replace (same key), remove (null value) or extend (new key). <see cref="MinimalFiles"/> is the JSON form of a minimal
/// valid set: one container, one item, two mail kinds (two, because CV-MAIL-02 caps one share at 600 and a share map cannot repeat
/// a key), one destination and one mix. Every def sits on its own line, so the n-th def of a file starts on line n + 1.
/// </summary>
/// <example>
/// <code>
/// SchemaResult r = SchemaJson.Walk(DefType.Item, SchemaJson.File(SchemaJson.Plank(("weightClass", null))));
/// </code>
/// </example>
internal static class SchemaJson
{
    private static readonly (string Key, string Value)[] _plank =
    {
        ("id", "\"plank\""), ("name", "\"item.plank\""), ("category", "\"material\""), ("grid", "[1, 1]"), ("maxStack", "20"),
        ("weightClass", "\"light\""), ("carryByHand", "true"),
    };

    private static readonly (string Key, string Value)[] _cursor =
    {
        ("id", "\"cursor\""), ("layout", "\"slots\""), ("grid", "[1, 1]"), ("view", "\"auto\""), ("handCarried", "true"),
        ("acceptsInsert", "true"),
    };

    private static readonly (string Key, string Value)[] _kind =
    {
        ("id", "\"letter\""), ("name", "\"mail.letter\""), ("grid", "[1, 1]"), ("maxStack", "20"), ("baseValue", "8"),
        ("weightClass", "\"light\""), ("carryByHand", "true"), ("beltLanes", "1"), ("beltLengthMetres", "0.5"),
        ("acceptedBy", "[\"house\"]"), ("complaintOnMisdeliveryPoints", "5"), ("unlockShift", "1"),
    };

    private static readonly (string Key, string Value)[] _house =
    {
        ("id", "\"house\""), ("accepts", "[\"postcard\", \"letter\"]"), ("insertRate", "2"), ("manualInsertHoldSeconds", "0.4"),
        ("maxAutomatedFeeders", "1"),
        ("catchVolume", "{\"widthMetres\": 1, \"heightMetres\": 0.8, \"depthMetres\": 1, \"centreHeightMetres\": 1, \"yawDeg\": 0}"),
    };

    private static readonly (string Key, string Value)[] _mix =
    {
        ("id", "\"default\""), ("shifts", "[{\"shift\": 1, \"shares\": {\"postcard\": 0.4, \"letter\": 0.6}}]"),
        ("batchIntervalSeconds", "15"), ("batchJitterSeconds", "3"), ("tailGuardSeconds", "9"), ("spawnOverhead", "1.6"),
        ("streetStreakRatio", "0.3"), ("raidSpawnRateRatio", "0.5"), ("distanceStepRatio", "0.25"), ("shiftStepRatio", "0.1"),
    };

    /// <summary>The item <c>plank</c> with <paramref name="edits"/> applied.</summary>
    public static string Plank(params (string Key, string? Value)[] edits) => Def(_plank, edits);

    /// <summary>The container <c>cursor</c> with <paramref name="edits"/> applied.</summary>
    public static string Cursor(params (string Key, string? Value)[] edits) => Def(_cursor, edits);

    /// <summary>The mail kind <c>letter</c> with <paramref name="edits"/> applied (edit <c>id</c> and <c>name</c> for another kind).</summary>
    public static string Kind(params (string Key, string? Value)[] edits) => Def(_kind, edits);

    /// <summary>The destination type <c>house</c> with <paramref name="edits"/> applied.</summary>
    public static string House(params (string Key, string? Value)[] edits) => Def(_house, edits);

    /// <summary>The mail mix <c>default</c> with <paramref name="edits"/> applied.</summary>
    public static string Mix(params (string Key, string? Value)[] edits) => Def(_mix, edits);

    /// <summary>A def file holding <paramref name="defs"/>, one per line from line 2.</summary>
    public static string File(params string[] defs) => "[\n  " + string.Join(",\n  ", defs) + "\n]\n";

    /// <summary>The conventional path of a type's file (CD2 layout).</summary>
    public static string PathOf(DefType type) => type switch
    {
        DefType.Item => "items/core.jsonc",
        DefType.Container => "items/containers.jsonc",
        DefType.MailKind => "mail/kinds.jsonc",
        DefType.DestinationType => "mail/destinations.jsonc",
        _ => "mail/mix.jsonc",
    };

    /// <summary>Runs stages 1 and 2 over one file of <paramref name="type"/>.</summary>
    public static SchemaResult Walk(DefType type, string text, string? path = null) =>
        new SchemaStage().Walk(SyntaxWalker.Walk(path ?? PathOf(type), Encoding.UTF8.GetBytes(text), TopLevelShape.Array), type);

    /// <summary>Walks one file against an ad-hoc table (for a row the shipped tables cannot have, such as one with no unit).</summary>
    public static SchemaResult Walk(SchemaTable table, string text) =>
        SchemaWalker.Walk(SyntaxWalker.Walk("defs/fixture.jsonc", Encoding.UTF8.GetBytes(text), TopLevelShape.Array), table);

    /// <summary>The minimal valid set as five walked files; <paramref name="replace"/> swaps a type's file text.</summary>
    public static ImmutableArray<SchemaResult> MinimalFiles(params (DefType Type, string Text)[] replace)
    {
        var texts = new Dictionary<DefType, string>
        {
            [DefType.Container] = File(Cursor()),
            [DefType.Item] = File(Plank()),
            [DefType.MailKind] = File(Kind(("id", "\"postcard\""), ("name", "\"mail.postcard\"")), Kind()),
            [DefType.DestinationType] = File(House()),
            [DefType.MailMix] = File(Mix()),
        };
        foreach ((DefType type, string text) in replace)
        {
            texts[type] = text;
        }

        return texts.Select(t => Walk(t.Key, t.Value)).ToImmutableArray();
    }

    private static string Def((string Key, string Value)[] fields, (string Key, string? Value)[] edits)
    {
        var pairs = fields.Select(f => (f.Key, (string?)f.Value)).ToList();
        foreach ((string key, string? value) in edits)
        {
            int i = pairs.FindIndex(p => p.Key == key);
            if (i < 0)
            {
                pairs.Add((key, value));
            }
            else
            {
                pairs[i] = (key, value);
            }
        }

        return "{" + string.Join(", ", pairs.Where(p => p.Item2 is not null).Select(p => "\"" + p.Key + "\": " + p.Item2)) + "}";
    }
}
