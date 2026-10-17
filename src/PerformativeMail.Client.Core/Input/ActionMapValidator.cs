using System.Collections.Immutable;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The field checks of the action map (H-R1-1, run by <see cref="ActionMap.TryLoad"/> and at boot on the built-in map): unique
/// non-zero <see cref="ActionDef.Id"/>, unique non-empty string id, alias collisions (two actions, an alias equal to another
/// action's id, a repeated alias), exactly one <see cref=ValidationFields.Grammar/>, a non-empty <see cref="ActionDef.LiveIn"/> that matches at least one reachable state, a defined
/// <see cref=ValidationFields.Delivery/>, the frame-bit rules, four well-formed default bindings on the right device (or an explicit
/// <see cref="InputBinding.Unbound"/>), reserved inputs that are actually bound, required actions bound, and a non-empty
/// owner GDD. Every failure names the action. The reachable-state pass (story 006: conflicts, binding shapes, pad paths) is
/// <see cref="Validate(ActionMap, ReachableSet)"/>; <see cref="ValidateForBoot(IReadOnlyList{ActionDef}, out ActionMap)"/>
/// runs both. Pure; thread-safe.
/// <para>
/// Frame bit rules: a <see cref="Delivery.Frame"/> action carries a distinct <see cref="FrameAction"/> bit unless its grammar
/// is <see cref="Grammar.Axis"/> (axes travel in dedicated frame fields and carry none); an
/// <see cref="Delivery.Immediate"/> action carries none.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ValidationReport report = ActionMapValidator.Validate(defs);
/// foreach (ValidationEntry e in report.Entries) { Console.WriteLine($"{e.Action}.{e.Field}: {e.Message}"); }
/// </code>
/// </example>
public static partial class ActionMapValidator
{
    /// <summary>Validates every def and returns the report; allocates, run at load or in tests.</summary>
    /// <param name="defs">The definitions.</param>
    /// <returns>A report with one entry per failure; empty when valid.</returns>
    /// <example><code>bool ok = ActionMapValidator.Validate(defs).IsValid;</code></example>
    public static ValidationReport Validate(IReadOnlyList<ActionDef?> defs)
    {
        var report = new ValidationReport();
        var idCounts = new Dictionary<ushort, int>();
        var nameOwners = new Dictionary<string, List<ActionDef>>(StringComparer.Ordinal);
        var aliasOwners = new Dictionary<string, List<ActionDef>>(StringComparer.Ordinal);
        var bitOwners = new Dictionary<FrameAction, int>();

        foreach (ActionDef? d in defs)
        {
            if (d is null)
            {
                report.Add("(null)", ValidationFields.Def, "an action definition is null");
                continue;
            }

            idCounts[d.Id] = idCounts.GetValueOrDefault(d.Id) + 1;
            if (!string.IsNullOrEmpty(d.StringId))
            {
                Owners(nameOwners, d.StringId).Add(d);
            }

            if (!d.Aliases.IsDefaultOrEmpty)
            {
                foreach (string alias in d.Aliases)
                {
                    if (!string.IsNullOrEmpty(alias))
                    {
                        Owners(aliasOwners, alias).Add(d);
                    }
                }
            }

            if (d.Delivery == Delivery.Frame && d.FrameBit is { } bit)
            {
                bitOwners[bit] = bitOwners.GetValueOrDefault(bit) + 1;
            }
        }

        foreach (ActionDef? d in defs)
        {
            if (d is null)
            {
                continue;
            }

            string name = string.IsNullOrEmpty(d.StringId) ? $"#{d.Id}" : d.StringId;
            CheckIds(report, d, name, idCounts, nameOwners);
            CheckAliases(report, d, name, nameOwners, aliasOwners);
            CheckGrammarAndPredicate(report, d, name);
            CheckDelivery(report, d, name, bitOwners);
            CheckBindings(report, d, name);
            if (string.IsNullOrWhiteSpace(d.OwnerGdd))
            {
                report.Add(name, ValidationFields.OwnerGdd, "the owning GDD is missing");
            }
        }

        return report;
    }

    private static List<ActionDef> Owners(Dictionary<string, List<ActionDef>> table, string key)
    {
        if (!table.TryGetValue(key, out List<ActionDef>? list))
        {
            list = [];
            table[key] = list;
        }

        return list;
    }

    private static void CheckIds(ValidationReport r, ActionDef d, string name, Dictionary<ushort, int> idCounts, Dictionary<string, List<ActionDef>> nameOwners)
    {
        if (d.Id == ActionId.None)
        {
            r.Add(name, ValidationFields.Id, "the numeric ActionId is 0 (unset); ids start at 1");
        }
        else if (idCounts[d.Id] > 1)
        {
            r.Add(name, ValidationFields.Id, $"the numeric ActionId {d.Id} is used by more than one action");
        }

        if (string.IsNullOrWhiteSpace(d.StringId))
        {
            r.Add(name, ValidationFields.StringId, "the string id is missing");
        }
        else if (nameOwners[d.StringId].Count > 1)
        {
            r.Add(name, ValidationFields.StringId, $"the string id '{d.StringId}' is used by more than one action");
        }
    }

    private static void CheckAliases(ValidationReport r, ActionDef d, string name, Dictionary<string, List<ActionDef>> nameOwners, Dictionary<string, List<ActionDef>> aliasOwners)
    {
        if (d.Aliases.IsDefaultOrEmpty)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string alias in d.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                r.Add(name, ValidationFields.Aliases, "an alias is empty");
                continue;
            }

            if (!seen.Add(alias))
            {
                r.Add(name, ValidationFields.Aliases, $"the alias '{alias}' is listed twice");
            }

            if (alias == d.StringId)
            {
                r.Add(name, ValidationFields.Aliases, $"the alias '{alias}' equals the action's own id");
            }
            else if (nameOwners.TryGetValue(alias, out List<ActionDef>? idOwners))
            {
                r.Add(name, ValidationFields.Aliases, $"the alias '{alias}' equals the id of action '{idOwners[0].StringId}'");
            }

            foreach (ActionDef other in aliasOwners[alias])
            {
                if (!ReferenceEquals(other, d))
                {
                    r.Add(name, ValidationFields.Aliases, $"the alias '{alias}' is also claimed by action '{DisplayName(other)}'");
                    break;
                }
            }
        }
    }

    private static string DisplayName(ActionDef d) => string.IsNullOrEmpty(d.StringId) ? $"#{d.Id}" : d.StringId;

    private static void CheckGrammarAndPredicate(ValidationReport r, ActionDef d, string name)
    {
        if (d.Grammar == Grammar.None)
        {
            r.Add(name, ValidationFields.Grammar, "no grammar is set; every action has exactly one");
        }
        else if (!Enum.IsDefined(d.Grammar))
        {
            r.Add(name, ValidationFields.Grammar, $"the grammar value {(byte)d.Grammar} is not defined");
        }

        if (d.LiveIn.IsDefaultOrEmpty)
        {
            r.Add(name, ValidationFields.LiveIn, "no live predicate; an action must be live somewhere");
        }
        else if (!LiveInSomeReachableState(d))
        {
            r.Add(name, ValidationFields.LiveIn, "the live predicates match no reachable router state, so the action could never fire");
        }

        if (d.TargetDriven && d.Grammar != Grammar.Hold)
        {
            r.Add(name, ValidationFields.TargetDriven, "TargetDriven is only valid with Grammar.Hold");
        }

        if (d.CommitOnRelease && (d.Grammar != Grammar.AimRelease || d.Delivery != Delivery.Frame))
        {
            r.Add(name, ValidationFields.CommitOnRelease, "CommitOnRelease is only valid with Grammar.AimRelease and Delivery.Frame");
        }
    }

    private static bool LiveInSomeReachableState(ActionDef d)
    {
        ReadOnlySpan<ushort> ids = ReachableSet.Default.Ids;
        for (int i = 0; i < ids.Length; i++)
        {
            if (d.IsLive(ids[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckDelivery(ValidationReport r, ActionDef d, string name, Dictionary<FrameAction, int> bitOwners)
    {
        switch (d.Delivery)
        {
            case Delivery.Frame:
                if (d.Grammar == Grammar.Axis)
                {
                    if (d.FrameBit is not null)
                    {
                        r.Add(name, ValidationFields.FrameBit, "an axis action travels in dedicated frame fields and must not carry a FrameAction bit");
                    }
                }
                else if (d.FrameBit is not { } bit)
                {
                    r.Add(name, ValidationFields.FrameBit, "a Frame action needs a FrameAction bit");
                }
                else if (!Enum.IsDefined(bit))
                {
                    r.Add(name, ValidationFields.FrameBit, $"the FrameAction value {(byte)bit} is not defined");
                }
                else if (bitOwners[bit] > 1)
                {
                    r.Add(name, ValidationFields.FrameBit, $"the FrameAction bit {bit} is used by more than one action");
                }

                break;
            case Delivery.Immediate:
                if (d.FrameBit is not null)
                {
                    r.Add(name, ValidationFields.FrameBit, "an Immediate action must not carry a FrameAction bit");
                }

                break;
            default:
                r.Add(name, ValidationFields.Delivery, "the delivery is missing or undefined (Frame or Immediate)");
                break;
        }
    }

    private static void CheckBindings(ValidationReport r, ActionDef d, string name)
    {
        CheckSlot(r, name, ValidationFields.KbmPrimary, d.KbmPrimary, pad: false);
        CheckSlot(r, name, ValidationFields.KbmSecondary, d.KbmSecondary, pad: false);
        CheckSlot(r, name, ValidationFields.PadPrimary, d.PadPrimary, pad: true);
        CheckSlot(r, name, ValidationFields.PadSecondary, d.PadSecondary, pad: true);

        if (!d.KbmPrimary.IsMissing && !d.KbmPrimary.IsUnbound && d.KbmPrimary == d.KbmSecondary)
        {
            r.Add(name, ValidationFields.KbmSecondary, "the secondary binding repeats the primary");
        }

        if (!d.PadPrimary.IsMissing && !d.PadPrimary.IsUnbound && d.PadPrimary == d.PadSecondary)
        {
            r.Add(name, ValidationFields.PadSecondary, "the secondary binding repeats the primary");
        }

        if (d.Required)
        {
            if (d.KbmPrimary.IsUnbound)
            {
                r.Add(name, ValidationFields.Required, "a required action must keep a keyboard-and-mouse binding (KbmPrimary is Unbound)");
            }

            if (d.PadPrimary.IsUnbound)
            {
                r.Add(name, ValidationFields.Required, "a required action must keep a gamepad binding (PadPrimary is Unbound)");
            }
        }

        if (!d.Reserved.IsDefaultOrEmpty)
        {
            foreach (PhysicalInput input in d.Reserved)
            {
                if (input == PhysicalInput.None
                    || !(d.KbmPrimary.Contains(input) || d.KbmSecondary.Contains(input) || d.PadPrimary.Contains(input) || d.PadSecondary.Contains(input)))
                {
                    r.Add(name, ValidationFields.Reserved, $"the reserved input {(PhysicalInputNames.TryGetName(input, out string n) ? n : "None")} is not in any default binding");
                }
            }
        }
    }

    private static void CheckSlot(ValidationReport r, string name, string field, InputBinding b, bool pad)
    {
        if (b.IsMissing)
        {
            r.Add(name, field, "the default binding is missing; bind it or use InputBinding.Unbound");
            return;
        }

        if (!b.IsWellFormed())
        {
            r.Add(name, field, $"the binding is malformed ({b})");
            return;
        }

        int n = b.InputCount;
        for (int i = 0; i < n; i++)
        {
            PhysicalInput p = b.InputAt(i);
            if (!(pad ? PhysicalInputs.IsGamepad(p) : PhysicalInputs.IsKeyboardMouse(p)))
            {
                r.Add(name, field, $"{p} is a {(pad ? "keyboard or mouse" : "gamepad")} input in a {(pad ? "gamepad" : "keyboard-and-mouse")} slot");
            }
        }

        if (b.AcceptsNumpadEnter && !b.Contains(PhysicalInput.KeyEnter))
        {
            r.Add(name, field, "the NumpadEnter alias needs Key.Enter in the binding");
        }
    }
}
