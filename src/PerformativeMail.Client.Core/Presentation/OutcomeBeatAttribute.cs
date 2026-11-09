namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// Marks an outcome-beat component (ADR-0016 section 5): hand beat, cha-ching, landing sfx, flag, wallet change and the like. A type
/// tagged with it receives its data only from <see cref="PresentationEventStream"/> cursors, never from the predictive published
/// states; manual-delivery 006's architecture test enforces that.
/// </summary>
/// <example><code>[OutcomeBeat] public sealed partial class MailboxFlagBeat : Node3D { /* reads an EventCursor */ }</code></example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OutcomeBeatAttribute : Attribute
{
}
