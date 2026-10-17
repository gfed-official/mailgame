namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The Sim-side read port for client input settings the verb owners need as integers (Input and Controls GDD;
/// ADR-0012). Player Character and Manual Delivery take <c>holdSettingMs</c> as an argument; the Client.Core settings
/// implement this port. Reads must not allocate.
/// </summary>
/// <example><code>int holdMs = settings.HoldSettingMs;   // 400 by default</code></example>
public interface IClientInputSettings
{
    /// <summary>The hold-to-activate time in milliseconds.</summary>
    int HoldSettingMs { get; }
}

/// <summary>The default settings: <see cref="IClientInputSettings.HoldSettingMs"/> is 400.</summary>
/// <example><code>IClientInputSettings s = new DefaultClientInputSettings();</code></example>
public sealed class DefaultClientInputSettings : IClientInputSettings
{
    /// <summary>The default hold-to-activate time in milliseconds.</summary>
    public const int DefaultHoldSettingMs = 400;

    /// <inheritdoc />
    public int HoldSettingMs => DefaultHoldSettingMs;
}
