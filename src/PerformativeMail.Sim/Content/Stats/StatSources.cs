using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// The authoritative source list a sheet is built from (ADR-0010 section 7): the team modifiers plus one list per
/// <see cref="PlayerSlot"/>, each in the order given. A modifier whose <see cref="Modifier.OwnerSlot"/> is
/// <see cref="PlayerSlot.None"/> is a team modifier. Immutable once constructed (the input is copied), so safe from any
/// thread. Besides modifiers it carries the rule-flag grants (one per live source and flag). Personal picks that are keyed by
/// <c>profileId</c> are turned into a slot-keyed list by <see cref="FromProfilePicks"/>. This type does not validate:
/// <see cref="StatSheet.Build"/> does.
/// </summary>
/// <example>
/// <code>
/// var sources = new StatSources([longLegsStack0, longLegsStack1, teamPerk]);
/// ReadOnlySpan&lt;Modifier&gt; mine = sources.ForSlot(PlayerSlot.FromValue(2));
/// IStatSheet sheet = StatSheet.Build(content, sources);
/// </code>
/// </example>
public sealed class StatSources
{
    private readonly Modifier[] _team;
    private readonly Modifier[][] _slots;
    private readonly RuleFlagGrant[] _grants;

    /// <summary>Copies and splits <paramref name="modifiers"/> by owner; relative order is kept within each list. No rule flags.</summary>
    /// <param name="modifiers">Every live modifier, any order; team and personal mixed.</param>
    public StatSources(ReadOnlySpan<Modifier> modifiers)
        : this(modifiers, ReadOnlySpan<RuleFlagGrant>.Empty)
    {
    }

    /// <summary>Copies and splits <paramref name="modifiers"/> by owner and keeps the rule-flag grants.</summary>
    /// <param name="modifiers">Every live modifier, any order; team and personal mixed.</param>
    /// <param name="grants">One entry per (live source, rule flag it holds).</param>
    public StatSources(ReadOnlySpan<Modifier> modifiers, ReadOnlySpan<RuleFlagGrant> grants)
    {
        _grants = grants.ToArray();
        int[] counts = new int[PlayerSlot.Count + 1];
        for (int i = 0; i < modifiers.Length; i++)
        {
            counts[ListOf(modifiers[i].OwnerSlot)]++;
        }

        _team = counts[0] == 0 ? Array.Empty<Modifier>() : new Modifier[counts[0]];
        _slots = new Modifier[PlayerSlot.Count][];
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            _slots[s] = counts[s + 1] == 0 ? Array.Empty<Modifier>() : new Modifier[counts[s + 1]];
        }

        Array.Clear(counts);
        for (int i = 0; i < modifiers.Length; i++)
        {
            Modifier m = modifiers[i];
            int list = ListOf(m.OwnerSlot);
            Modifier[] target = list == 0 ? _team : _slots[list - 1];
            target[counts[list]++] = m;
        }

        Count = modifiers.Length;
    }

    /// <summary>The total number of modifiers (team plus every slot).</summary>
    public int Count { get; }

    /// <summary>The rule-flag grants, in input order.</summary>
    public ReadOnlySpan<RuleFlagGrant> Grants => _grants;

    /// <summary>The team modifiers, in input order.</summary>
    public ReadOnlySpan<Modifier> Team => _team;

    /// <summary>The modifiers owned by one slot, in input order; empty for a slot with no player or <see cref="PlayerSlot.None"/>.</summary>
    /// <param name="slot">The slot (0 to 7).</param>
    /// <returns>A view over the slot's list.</returns>
    /// <example>
    /// <code>
    /// int n = sources.ForSlot(PlayerSlot.FromValue(3)).Length;
    /// </code>
    /// </example>
    public ReadOnlySpan<Modifier> ForSlot(PlayerSlot slot) => slot.IsNone ? ReadOnlySpan<Modifier>.Empty : _slots[slot.Value];

    /// <summary>
    /// Builds a slot-keyed source list from a profile-keyed one (GDD CD15, CDD-STAT-13). Team modifiers and flags go to the
    /// team. Each personal pick goes to the slot its profile occupies <b>now</b>; a profile with no entry, or whose entry has
    /// no slot, is absent, and its picks and flags apply to <b>nobody</b>: they are dropped, never given to
    /// <see cref="PlayerSlot.None"/> (which means team). When a profile has several entries the first one with a slot wins.
    /// Pure and deterministic; allocates (rebuild path).
    /// </summary>
    /// <param name="teamModifiers">Team modifiers; their <see cref="Modifier.OwnerSlot"/> is forced to team.</param>
    /// <param name="personalPicks">Personal picks keyed by profile.</param>
    /// <param name="profileSlots">The current profile to slot map.</param>
    /// <param name="teamFlags">One entry per live team source and flag it holds.</param>
    /// <param name="personalFlags">One entry per live personal source and flag it holds, keyed by profile.</param>
    /// <returns>The slot-keyed source list.</returns>
    /// <example>
    /// <code>
    /// var map = new[] { new ProfileSlotEntry(p, PlayerSlot.FromValue(5)) };
    /// StatSources s = StatSources.FromProfilePicks([], [new ProfilePick(p, longLegs)], map);
    /// IStatSheet sheet = StatSheet.Build(content, s);   // the pick applies to slot 5; with an empty map it applies to nobody
    /// </code>
    /// </example>
    public static StatSources FromProfilePicks(
        ReadOnlySpan<Modifier> teamModifiers,
        ReadOnlySpan<ProfilePick> personalPicks,
        ReadOnlySpan<ProfileSlotEntry> profileSlots,
        ReadOnlySpan<RuleFlag> teamFlags = default,
        ReadOnlySpan<ProfileRuleFlag> personalFlags = default)
    {
        var mods = new Modifier[teamModifiers.Length + personalPicks.Length];
        int m = 0;
        for (int i = 0; i < teamModifiers.Length; i++)
        {
            mods[m++] = teamModifiers[i] with { OwnerSlot = PlayerSlot.None };
        }

        for (int i = 0; i < personalPicks.Length; i++)
        {
            PlayerSlot slot = SlotOf(profileSlots, personalPicks[i].Profile);
            if (!slot.IsNone)
            {
                mods[m++] = personalPicks[i].Modifier with { OwnerSlot = slot };
            }
        }

        var grants = new RuleFlagGrant[teamFlags.Length + personalFlags.Length];
        int g = 0;
        for (int i = 0; i < teamFlags.Length; i++)
        {
            grants[g++] = new RuleFlagGrant(ModifierOwner.Team, teamFlags[i]);
        }

        for (int i = 0; i < personalFlags.Length; i++)
        {
            PlayerSlot slot = SlotOf(profileSlots, personalFlags[i].Profile);
            if (!slot.IsNone)
            {
                grants[g++] = new RuleFlagGrant(ModifierOwner.ForSlot(slot), personalFlags[i].Flag);
            }
        }

        return new StatSources(mods.AsSpan(0, m), grants.AsSpan(0, g));
    }

    private static PlayerSlot SlotOf(ReadOnlySpan<ProfileSlotEntry> map, ProfileId profile)
    {
        for (int i = 0; i < map.Length; i++)
        {
            if (map[i].Profile == profile && !map[i].Slot.IsNone)
            {
                return map[i].Slot;
            }
        }

        return PlayerSlot.None;
    }

    // 0 = team, 1 + slot otherwise.
    private static int ListOf(PlayerSlot owner) => owner.IsNone ? 0 : owner.Value + 1;
}
