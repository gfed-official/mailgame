namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The armed-catcher terminal port of <c>Throwing.Step</c> (ADR-0015 sections 5 and 7, <c>CAUGHT</c> / <c>FUMBLE</c>; throwing-catching
/// story 011 declares it, story 023 (M0, erratum E9) implements it over <c>IInventoryApi.TryCatch</c>). Nothing binds it at M-1a and the
/// step supplies no catcher spheres, so no flight can win on a catcher and the port is never invoked. The implementation owns the whole
/// terminal, as <see cref="IMailboxLanding"/> does. Phase 4 only; allocation-free.
/// </summary>
/// <example><code>if (c.Kind == ReceiverKind.ArmedCatcher) { catchLanding.Land(index, in c, tick); }</code></example>
internal interface ICatchLanding
{
    /// <summary>Resolves the live flight at <paramref name="index"/> on its catcher contact.</summary>
    /// <param name="index">The flight's position in the table.</param>
    /// <param name="contact">The winning contact (<see cref="Contact.ReceiverKey"/> is the catcher's entity id).</param>
    /// <param name="tick">The tick being stepped.</param>
    void Land(int index, in Contact contact, uint tick);
}
