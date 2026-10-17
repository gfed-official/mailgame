namespace PerformativeMail.Sim.Core;

/// <summary>
/// The Sim Core owned objects whose state section 1 of the canonical dump encodes. They are separate objects created by the
/// composition root (they need the world's tick reader, so they are built against a forwarding reader before
/// <see cref="SimWorldBuilder.Freeze"/>); a source left null is dumped exactly as a fresh, untouched one would be.
/// </summary>
/// <param name="Ids">The entity id allocator, or null (all class counters 0).</param>
/// <param name="Mail">The mail id allocator, or null (counter 0).</param>
/// <param name="Rng">The RNG registry, or null (no live streams).</param>
/// <param name="Slots">The player slot table, or null (eight free, never-freed slots).</param>
/// <example><code>var sources = new SimCoreStateSources(ids, mail, rng, slots);</code></example>
internal sealed record SimCoreStateSources(EntityIds? Ids, MailIds? Mail, RngRegistry? Rng, SlotTable? Slots);
