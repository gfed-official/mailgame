namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The mailbox terminal port of <c>Throwing.Step</c> (ADR-0015 section 5 row <c>MAILBOX_*</c>; throwing-catching story 011 declares it,
/// story 012 implements it over <c>IMailApi.ResolveThrownLanding</c>): the step dispatches a flight whose winning contact is a mailbox
/// catch volume here and does nothing else with it. The implementation owns the whole terminal: exactly one <c>FlightResolved</c>
/// (raised before the domain events it causes), the units, and <see cref="FlightTable.Remove"/> of the flight. Until story 012,
/// <see cref="TerminalResolver"/>'s own drop-straight-down landing is bound (<see cref="ThrowingSystem"/> production wiring).
/// Called inside phase 4 only; allocation-free.
/// </summary>
/// <example><code>if (c.Kind == ReceiverKind.Mailbox) { mailbox.Land(index, in c, tick); }</code></example>
internal interface IMailboxLanding
{
    /// <summary>Resolves the live flight at <paramref name="index"/> on its mailbox contact.</summary>
    /// <param name="index">The flight's position in the table.</param>
    /// <param name="contact">The winning contact (<see cref="Contact.ReceiverKey"/> is the destination's entity id).</param>
    /// <param name="tick">The tick being stepped.</param>
    void Land(int index, in Contact contact, uint tick);
}
