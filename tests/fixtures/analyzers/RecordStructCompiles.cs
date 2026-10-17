// expect: compiles
// SC-ARCH-2 fixture (recorded result, ADR-0006 section 3): a readonly record struct declared in the Sim.
// Synthesised record members have no syntax, so they are expected NOT to trip RS0030 / PMSIM001 even
// though their bodies use EqualityComparer<T>.Default and call GetHashCode. Review-enforced gap.
namespace PerformativeMail.Sim.ArchFixtures;

internal readonly record struct RecordStructCompiles(uint Value);
