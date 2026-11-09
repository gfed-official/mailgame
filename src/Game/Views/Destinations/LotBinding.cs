using Godot;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Destinations;

/// <summary>What a destination view is bound to (godot-host-shell story 009): one lot of <see cref="IWorldQuery.Lots"/>.</summary>
/// <param name="Destination">The lot's destination entity.</param>
/// <param name="Ordinal">The lot index (Mail's <c>DestinationOrdinal</c>).</param>
/// <param name="AnchorM">The catch anchor in metres (<c>LotRecord.Anchor / 1000</c>).</param>
/// <param name="Face">The direction the lot faces.</param>
/// <param name="Number">The house number shown.</param>
public readonly record struct LotBinding(EntityId Destination, int Ordinal, Vector3 AnchorM, FaceDir Face, ushort Number);
