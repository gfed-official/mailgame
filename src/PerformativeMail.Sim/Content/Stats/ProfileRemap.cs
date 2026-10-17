using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The account identity a personal pick is keyed by for the whole run (GDD CD15, <c>profileId</c>). Its wire type is not fixed
/// by any ADR yet (UNVERIFIED); an opaque 64-bit value is assumed here and the Networking epic may replace the type behind
/// it. Immutable.
/// </summary>
/// <param name="Value">The opaque id.</param>
/// <example><code>var p = new ProfileId(0xA11CE);</code></example>
public readonly record struct ProfileId(ulong Value);

/// <summary>One current <c>profileId</c> to slot mapping. An entry whose slot is <see cref="PlayerSlot.None"/> means "present in the roster, no slot now" and maps nowhere.</summary>
/// <param name="Profile">The profile.</param>
/// <param name="Slot">The slot the profile currently occupies, or <see cref="PlayerSlot.None"/> while it is absent.</param>
/// <example><code>var e = new ProfileSlotEntry(new ProfileId(7), PlayerSlot.FromValue(2));</code></example>
public readonly record struct ProfileSlotEntry(ProfileId Profile, PlayerSlot Slot);

/// <summary>A personal modifier pick keyed by profile. The <see cref="Modifier.OwnerSlot"/> inside is ignored: the remap overwrites it.</summary>
/// <param name="Profile">Who picked it.</param>
/// <param name="Modifier">The modifier (its owner slot is replaced by the profile's current slot).</param>
/// <example><code>var pick = new ProfilePick(new ProfileId(7), longLegsStack0);</code></example>
public readonly record struct ProfilePick(ProfileId Profile, Modifier Modifier);

/// <summary>A personal rule flag held by one live source of a profile.</summary>
/// <param name="Profile">Who holds the flag.</param>
/// <param name="Flag">The flag.</param>
/// <example><code>var f = new ProfileRuleFlag(new ProfileId(7), RuleFlag.PostalSprint);</code></example>
public readonly record struct ProfileRuleFlag(ProfileId Profile, RuleFlag Flag);

/// <summary>
/// One rule-flag grant: a live source owned by <see cref="Owner"/> holds <see cref="Flag"/>. The sheet counts grants per flag
/// and sheet; a flag is on while the count is above 0. An unowned (<c>default</c>) owner is invalid and skipped.
/// </summary>
/// <param name="Owner">The team or one slot.</param>
/// <param name="Flag">The flag.</param>
/// <example><code>var g = new RuleFlagGrant(ModifierOwner.Team, RuleFlag.RushHour);</code></example>
public readonly record struct RuleFlagGrant(ModifierOwner Owner, RuleFlag Flag);
