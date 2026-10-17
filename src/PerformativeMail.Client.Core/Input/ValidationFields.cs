namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The field names a <see cref="ValidationEntry"/> reports, as constants tied to the <see cref="ActionDef"/> members (so a
/// rename breaks the build, not a string compare). <see cref="Def"/> is for a failure of the definition as a whole.
/// </summary>
/// <example><code>bool bad = report.Has("jump", ValidationFields.Grammar);</code></example>
public static class ValidationFields
{
    /// <summary>The definition as a whole (for example a null entry).</summary>
    public const string Def = "Def";

    /// <summary><see cref="ActionDef.Id"/>.</summary>
    public const string Id = nameof(ActionDef.Id);

    /// <summary><see cref="ActionDef.StringId"/>.</summary>
    public const string StringId = nameof(ActionDef.StringId);

    /// <summary><see cref="ActionDef.Grammar"/>.</summary>
    public const string Grammar = nameof(ActionDef.Grammar);

    /// <summary><see cref="ActionDef.LiveIn"/>.</summary>
    public const string LiveIn = nameof(ActionDef.LiveIn);

    /// <summary><see cref="ActionDef.Delivery"/>.</summary>
    public const string Delivery = nameof(ActionDef.Delivery);

    /// <summary><see cref="ActionDef.FrameBit"/>.</summary>
    public const string FrameBit = nameof(ActionDef.FrameBit);

    /// <summary><see cref="ActionDef.TargetDriven"/>.</summary>
    public const string TargetDriven = nameof(ActionDef.TargetDriven);

    /// <summary><see cref="ActionDef.CommitOnRelease"/>.</summary>
    public const string CommitOnRelease = nameof(ActionDef.CommitOnRelease);

    /// <summary><see cref="ActionDef.KbmPrimary"/>.</summary>
    public const string KbmPrimary = nameof(ActionDef.KbmPrimary);

    /// <summary><see cref="ActionDef.KbmSecondary"/>.</summary>
    public const string KbmSecondary = nameof(ActionDef.KbmSecondary);

    /// <summary><see cref="ActionDef.PadPrimary"/>.</summary>
    public const string PadPrimary = nameof(ActionDef.PadPrimary);

    /// <summary><see cref="ActionDef.PadSecondary"/>.</summary>
    public const string PadSecondary = nameof(ActionDef.PadSecondary);

    /// <summary><see cref="ActionDef.Reserved"/>.</summary>
    public const string Reserved = nameof(ActionDef.Reserved);

    /// <summary><see cref="ActionDef.Required"/>.</summary>
    public const string Required = nameof(ActionDef.Required);

    /// <summary><see cref="ActionDef.OwnerGdd"/>.</summary>
    public const string OwnerGdd = nameof(ActionDef.OwnerGdd);

    /// <summary><see cref="ActionDef.Aliases"/>.</summary>
    public const string Aliases = nameof(ActionDef.Aliases);

    /// <summary>The bindings of an action as a whole: conflict, shape and pad-path findings (story 006).</summary>
    public const string Bindings = "Bindings";
}
