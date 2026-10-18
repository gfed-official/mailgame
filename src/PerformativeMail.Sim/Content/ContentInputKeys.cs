namespace PerformativeMail.Sim.Content;

/// <summary>
/// The input keys of the five def tables, as rules declare them and as the pipeline reports a table that failed stage 3
/// (CV-GEN-09, too many defs). A rule that declares a table key is skipped, and counted, when that table failed.
/// The spelling is the def folder name. Constants only.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;string&gt; inputs = ImmutableArray.Create(ContentInputKeys.Containers, ContentInputKeys.MailKinds);
/// </code>
/// </example>
public static class ContentInputKeys
{
    /// <summary>The ContainerDef table.</summary>
    public const string Containers = "containers";

    /// <summary>The ItemDef table.</summary>
    public const string Items = "items";

    /// <summary>The MailKindDef table.</summary>
    public const string MailKinds = "mail_kinds";

    /// <summary>The DestinationTypeDef table.</summary>
    public const string DestinationTypes = "destination_types";

    /// <summary>The MailMixDef table.</summary>
    public const string MailMixes = "mail_mixes";

    /// <summary>The <c>inventory</c> balance file (Grid Inventory), as rules that read an <c>inventory.*</c> key declare it.</summary>
    public const string InventoryBalance = "inventory";

    /// <summary>The <c>mail</c> balance file (Mail and Destinations), as rules that read a <c>mail.*</c> key declare it.</summary>
    public const string MailBalance = "mail";

    /// <summary>The <c>player</c> balance file (Player Character), as rules that read a <c>player.*</c> key declare it.</summary>
    public const string PlayerBalance = "player";

    /// <summary>The <c>throw</c> balance file (Throwing and Catching), as rules that read a <c>throw.*</c> key declare it.</summary>
    public const string ThrowBalance = "throw";

    /// <summary>
    /// The interaction verb declarations (<c>ContentValidationInput.VerbDeclarations</c>), as CV-PLR-13 (PCM-P10-1) declares its
    /// input and names it in its findings' <c>InputKey</c>. No def table carries a verb field at M-1a, so no stage 3 failure
    /// reports this key yet; the spelling is the future field name.
    /// </summary>
    public const string Verbs = "verbs";
}
