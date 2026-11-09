namespace PerformativeMail.Client.Core.Ui;

/// <summary>How a money amount is signed.</summary>
public enum MoneySign : byte
{
    /// <summary>A change: always signed, "+86¢", "−4¢", "0¢".</summary>
    Delta = 0,

    /// <summary>A balance: signed only when negative.</summary>
    Balance = 1,
}

/// <summary>The one place a money amount becomes text (H2, TR-hud-012). No view writes a currency literal.</summary>
public interface IMoneyFormatter
{
    /// <summary>Formats <paramref name="cents"/>.</summary>
    /// <param name="cents">Integer cents.</param>
    /// <param name="sign">The sign style.</param>
    /// <returns>The text.</returns>
    string Format(long cents, MoneySign sign);
}

/// <summary>English cents: <c>{sign}{integer}¢</c>, minus U+2212, no grouping.</summary>
/// <example><code>new EnMoneyFormatter().Format(-4, MoneySign.Delta);   // "−4¢"</code></example>
public sealed class EnMoneyFormatter : IMoneyFormatter
{
    /// <inheritdoc />
    public string Format(long cents, MoneySign sign)
    {
        string prefix = cents < 0 ? "−" : (sign == MoneySign.Delta && cents > 0 ? "+" : string.Empty);
        long magnitude = cents < 0 ? -cents : cents;
        return prefix + magnitude + "¢";
    }
}
