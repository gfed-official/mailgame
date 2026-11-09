namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The team <c>playerScale</c> (n^0.65, permille) Mail's complaint buckets are built with (TD-193 item 4). Carried as its own type so
/// the composition rules can name it as a Mail-only constructor input. The composition root fills it from the profile's shift stub
/// settings; Run Structure's real value replaces it at M1.
/// </summary>
/// <param name="Permille">The scale in permille; 1000 for one player.</param>
/// <example><code>var scale = new PlayerScale(1000);</code></example>
public sealed record PlayerScale(int Permille);
