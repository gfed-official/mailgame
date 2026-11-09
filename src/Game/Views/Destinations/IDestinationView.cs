using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Views.Destinations;

/// <summary>
/// The attach point of a per-lot view (godot-host-shell story 009; mail-destinations 016's <c>MailboxView</c> implements it). The root of
/// the destination-type scene is injected (inert until then) and then bound to its lot once, at boot step 8. A scene whose root does not
/// implement it (the placeholder) is simply shown.
/// </summary>
/// <example><code>public void Bind(in LotBinding b) { _number.Text = b.Number.ToString(CultureInfo.InvariantCulture); }</code></example>
public interface IDestinationView : IInjectable<PresenterDeps>
{
    /// <summary>Binds the view to its lot.</summary>
    /// <param name="b">The lot.</param>
    void Bind(in LotBinding b);
}
