namespace PerformativeMail.Sim.Content;

/// <summary>
/// Numeric codes passed to <see cref="Primitives.SimAssert"/> by content-data code. Assert codes are
/// allocated in blocks so they never collide: the Sim core owns codes below 1000
/// (<see cref="Primitives.SimAssertCode"/>), Client.Core owns 1000 to 1999, and content-data owns
/// 2000 to 2999 (this class). Append-only: a code is never renumbered or reused.
/// </summary>
/// <example>
/// <code>
/// SimAssert.That(!index.IsNone, ContentAssertCode.DefTableNoneIndex);
/// </code>
/// </example>
public static class ContentAssertCode
{
    /// <summary>A <see cref="DefTable{T}"/> was indexed with <c>DefIndex.None</c>.</summary>
    public const ushort DefTableNoneIndex = 2000;

    /// <summary>A <see cref="DefTable{T}"/> was built with more than 65 534 defs.</summary>
    public const ushort DefTableTooLarge = 2001;

    /// <summary><see cref="DefIdAssigner"/> was given duplicate ids (callers must reject duplicates, CV-GEN-01, first).</summary>
    public const ushort DefIdsNotUnique = 2002;

    /// <summary><see cref="DefIdAssigner"/> was given a null or empty id.</summary>
    public const ushort DefIdInvalid = 2003;

    /// <summary><see cref="Balance.BalanceTable.Get"/> was called with <see cref="Balance.BalanceKey.None"/>.</summary>
    public const ushort BalanceKeyNone = 2004;

    /// <summary><see cref="Balance.BalanceTable.Get"/> was called with a handle from a different <see cref="Balance.BalanceRegistry"/>.</summary>
    public const ushort BalanceKeyForeign = 2005;

    /// <summary><see cref="Stats.StatResolve.Resolve(Stats.StatDef, Stats.StatBounds, int, System.ReadOnlySpan{int}, System.ReadOnlySpan{int}, System.ReadOnlySpan{int}, int)"/> was given more than <see cref="Stats.StatResolve.MaxMulFactors"/> Mul factors (the Int128 proof's premise, F1).</summary>
    public const ushort StatMulFactorsExceeded = 2006;

    /// <summary><see cref="Stats.StatResolve"/> was given a Set for a stat whose <see cref="Stats.SetResolve"/> is <see cref="Stats.SetResolve.NotApplicable"/>.</summary>
    public const ushort StatSetWithoutResolve = 2007;

    /// <summary><see cref="Stats.StatResolve"/> was given a Mul outside 0 to 10 000 permille (the Int128 proof's range, CV-PRK-05).</summary>
    public const ushort StatMulOutOfRange = 2008;

    /// <summary><see cref="Stats.StatResolve"/> was given more than 65 535 Add values (the Int128 proof's modifier-count premise).</summary>
    public const ushort StatAddsTooMany = 2009;

    /// <summary><see cref="Stats.StatResolve"/> was given a clamp with min above max (CV-GEN-15 should have rejected the row).</summary>
    public const ushort StatBoundsInverted = 2010;

    /// <summary><see cref="Stats.StatResolve.Round"/> was given a <see cref="Stats.StatRounding"/> value that is not HalfUp, Floor or Ceil.</summary>
    public const ushort StatRoundingUnknown = 2011;

    /// <summary><see cref="Stats.IStatSheet"/> was read with a <see cref="Stat"/> that is not a defined member (Release returns 0).</summary>
    public const ushort StatSheetUnknownStat = 2012;

    /// <summary><see cref="Stats.IStatSheet"/> was read with a qualifier that is 0xFFFF or out of range for a qualified stat (Release returns clamp(min)).</summary>
    public const ushort StatSheetBadQualifier = 2013;

    /// <summary><see cref="Stats.IStatSheet"/> was read with a non-null slot that is <c>PlayerSlot.None</c> (Release reads the team-only value).</summary>
    public const ushort StatSheetBadSlot = 2014;

    /// <summary><see cref="Stats.IStatSheet"/> was read with a negative <c>transientAdd</c> (Release treats it as 0).</summary>
    public const ushort StatSheetNegativeTransient = 2015;

    /// <summary><see cref="Stats.IStatSheet"/> was read with a non-zero <c>transientAdd</c> on a stat whose ops exclude Add (Release treats it as 0).</summary>
    public const ushort StatSheetTransientNotAdd = 2016;

    /// <summary><see cref="Stats.StatSheet.Build"/> was given a modifier it cannot apply: unknown stat, wrong scope, an op the stat does not accept, or a bad qualifier (Release skips the modifier).</summary>
    public const ushort StatSheetBadModifier = 2017;

    /// <summary><see cref="Stats.IStatSheet"/> was read with a qualifier other than 0xFFFF on an unqualified stat (Release ignores the qualifier).</summary>
    public const ushort StatSheetQualifierOnUnqualified = 2018;

    /// <summary><see cref="Stats.StatSheet.Build"/> was given a rule-flag grant it cannot apply: an unowned (default) owner or a flag that is not a defined <see cref="RuleFlag"/> (Release skips the grant).</summary>
    public const ushort StatSheetBadFlagGrant = 2019;
}
