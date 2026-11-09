using System.Globalization;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Playtest launch flags (WP-G), read from the user args after <c>--</c> like <c>--seed=</c>, in debug builds only: a release build
/// gets <see cref="None"/> whatever the args say. <c>--playtest-delivery-seconds=&lt;n&gt;</c> sets the shift stub's Delivery length and scales its spawn budget with it
/// (clamped to <see cref="MinDeliverySeconds"/>…<see cref="MaxDeliverySeconds"/>), <c>--playtest-no-timer</c> sets it to
/// <see cref="MaxDeliverySeconds"/>, <c>--playtest-prep-seconds=&lt;n&gt;</c> sets the Prep length, and <c>--playtest-at-mailbox</c>
/// asks the host shell's autopilot to take the first Intake batch and park the carrier at a carried letter's mailbox (Prep then
/// defaults to <see cref="AtMailboxPrepSeconds"/>); <c>--playtest-park-next</c> does the same and, whenever the selected letter is for
/// another house (after a throw or a delivery staged the next one, or a hotbar key), walks the carrier to that letter's mailbox again
/// (WP-I). They only change the composition profile and the client's own input; no Sim rule changes. An unparsable value is ignored (logged by the caller through <see cref="Describe"/>).
/// </summary>
/// <example><code>godot --path . -- --playtest-no-timer --playtest-park-next</code></example>
public sealed record PlaytestArgs
{
    /// <summary>The Delivery length flag's prefix.</summary>
    public const string DeliverySecondsPrefix = "--playtest-delivery-seconds=";

    /// <summary>The Prep length flag's prefix.</summary>
    public const string PrepSecondsPrefix = "--playtest-prep-seconds=";

    /// <summary>The longest Delivery flag.</summary>
    public const string NoTimerArg = "--playtest-no-timer";

    /// <summary>The start-at-a-mailbox flag.</summary>
    public const string AtMailboxArg = "--playtest-at-mailbox";

    /// <summary>The start-at-a-mailbox flag that re-parks at each next letter's mailbox.</summary>
    public const string ParkNextArg = "--playtest-park-next";

    /// <summary>Shortest Delivery, s (above the mail mix's 9 s tail guard).</summary>
    public const int MinDeliverySeconds = 30;

    /// <summary>Longest Delivery, s: under Mail's 128-batch cap at the built-in 15 s batch interval (1920 s).</summary>
    public const int MaxDeliverySeconds = 1800;

    /// <summary>Longest Prep, s.</summary>
    public const int MaxPrepSeconds = 600;

    /// <summary>Prep with <c>--playtest-at-mailbox</c> and no Prep flag, s: Delivery (and the first batch) starts almost at once.</summary>
    public const int AtMailboxPrepSeconds = 2;

    /// <summary>No flags (release builds, or none given).</summary>
    public static PlaytestArgs None { get; } = new();

    /// <summary>The Delivery length override, s; null keeps the profile's.</summary>
    public int? DeliverySeconds { get; init; }

    /// <summary>The Prep length override, s; null keeps the profile's.</summary>
    public int? PrepSeconds { get; init; }

    /// <summary>Whether the autopilot takes the first batch and parks the carrier at a mailbox.</summary>
    public bool AtMailbox { get; init; }

    /// <summary>Whether the autopilot stays on and re-parks at the selected letter's mailbox (implies <see cref="AtMailbox"/>).</summary>
    public bool ParkNext { get; init; }

    /// <summary>True when any flag is set.</summary>
    public bool Any => DeliverySeconds is not null || PrepSeconds is not null || AtMailbox;

    /// <summary>Reads the flags from <paramref name="userArgs"/>; <see cref="None"/> unless <paramref name="debugBuild"/>.</summary>
    /// <param name="userArgs">The user args (<c>OS.GetCmdlineUserArgs()</c>).</param>
    /// <param name="debugBuild"><c>OS.IsDebugBuild()</c>.</param>
    /// <returns>The flags.</returns>
    public static PlaytestArgs Parse(IReadOnlyList<string> userArgs, bool debugBuild)
    {
        ArgumentNullException.ThrowIfNull(userArgs);
        if (!debugBuild)
        {
            return None;
        }

        int? delivery = null;
        int? prep = null;
        bool atMailbox = false;
        bool parkNext = false;
        foreach (string arg in userArgs)
        {
            if (arg == NoTimerArg)
            {
                delivery = MaxDeliverySeconds;
            }
            else if (arg == AtMailboxArg)
            {
                atMailbox = true;
            }
            else if (arg == ParkNextArg)
            {
                atMailbox = true;
                parkNext = true;
            }
            else if (TryValue(arg, DeliverySecondsPrefix, out int d))
            {
                delivery = Math.Clamp(d, MinDeliverySeconds, MaxDeliverySeconds);
            }
            else if (TryValue(arg, PrepSecondsPrefix, out int p))
            {
                prep = Math.Clamp(p, 0, MaxPrepSeconds);
            }
        }

        return new PlaytestArgs
        {
            DeliverySeconds = delivery,
            PrepSeconds = prep ?? (atMailbox ? AtMailboxPrepSeconds : null),
            AtMailbox = atMailbox,
            ParkNext = parkNext,
        };
    }

    /// <summary>The profile with the Prep and Delivery overrides applied; <paramref name="profile"/> itself when there are none.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The profile to compose.</returns>
    public SimProfile Apply(SimProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (DeliverySeconds is null && PrepSeconds is null)
        {
            return profile;
        }

        ShiftStubSettings stub = profile.ShiftStub;
        int delivery = DeliverySeconds ?? stub.DeliverySeconds;
        return profile.WithShiftStub(stub with
        {
            DeliverySeconds = delivery,

            // The spawn budget scales with the Delivery length, so each batch carries about as much mail as in the default shift.
            SpawnBudgetCents = stub.DeliverySeconds > 0 ? stub.SpawnBudgetCents * delivery / stub.DeliverySeconds : stub.SpawnBudgetCents,

            // At least one tick of Prep: the stub hands Mail the shift profile during Prep.
            PrepTicks = PrepSeconds is { } s ? Math.Max(1, s * TickRate.Hz) : stub.PrepTicks,
        });
    }

    /// <summary>One log line's worth of the flags.</summary>
    /// <returns>The description.</returns>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"delivery_s={(DeliverySeconds?.ToString(CultureInfo.InvariantCulture) ?? "default")} prep_s={(PrepSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default")} at_mailbox={AtMailbox} park_next={ParkNext}");

    private static bool TryValue(string arg, string prefix, out int value)
    {
        value = 0;
        return arg.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(arg.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
