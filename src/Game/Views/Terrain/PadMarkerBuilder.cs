using Godot;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Views.Terrain;

/// <summary>
/// The spawn-pad markers from <see cref="IWorldQuery.PostOffice"/> (godot-host-shell story 010, ADR-0026 section 7): a flat disc of
/// <see cref="PostOfficeRecord.PadRingRadiusMm"/> at <see cref="PostOfficeRecord.PadCentre"/>, one small marker per pad slot at
/// <see cref="PadSlots.Position"/>, a door marker at <see cref="PostOfficeRecord.DoorAnchor"/> and the Intake console, an orange box from
/// the ground up to <see cref="PostOfficeRecord.IntakeAnchor"/> (WP-I: the Intake had no mesh, so players walked to the orange door
/// marker 2 m west of it and got no [E] prompt; the door marker is now neutral). Derived from the tables only; no colliders. Visual only.
/// </summary>
/// <example><code>if (PadMarkerBuilder.Build(world) is { } pad) { terrain.AddChild(pad); }</code></example>
public static class PadMarkerBuilder
{
    // Lifts the flat markers just above the ground so they do not z-fight with it.
    private const float LiftM = 0.02f;

    /// <summary>Builds the <c>Pad</c> node (<c>Ring</c>, <c>Slot_&lt;k&gt;</c>, <c>Door</c>, <c>Intake</c>); null for a world without a Post Office.</summary>
    /// <param name="world">The world tables.</param>
    /// <returns>The pad node, or null.</returns>
    public static Node3D? Build(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        PostOfficeRecord po = world.PostOffice;
        if (po == default)
        {
            return null;
        }

        var pad = new Node3D { Name = "Pad" };
        float ring = po.PadRingRadiusMm / 1000f;
        pad.AddChild(Marker("Ring", po.PadCentre, new CylinderMesh { TopRadius = ring, BottomRadius = ring, Height = 0.01f }, new Color(0.85f, 0.80f, 0.30f)));
        for (int k = 0; k < po.PadSlots; k++)
        {
            pad.AddChild(Marker($"Slot_{k}", PadSlots.Position(world, in po, k), new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 0.02f }, new Color(0.95f, 0.95f, 0.95f)));
        }

        pad.AddChild(Marker("Door", po.DoorAnchor, new BoxMesh { Size = new Vector3(0.3f, 0.05f, 0.3f) }, new Color(0.70f, 0.70f, 0.70f)));

        // The console stands on the door's ground level; the anchor is its top. 0.6 m wide: walked into the wall behind it, the anchor
        // stays inside the 800 mm near-focus radius (it has no collider; a Sim solid would change the world tables).
        float height = Math.Max(0.1f, (po.IntakeAnchor.Y - po.DoorAnchor.Y) / 1000f);
        MeshInstance3D intake = Marker(
            "Intake", po.IntakeAnchor, new BoxMesh { Size = new Vector3(0.6f, height, 0.6f) }, new Color(0.95f, 0.55f, 0.15f));
        intake.Position = new Vector3(intake.Position.X, (po.DoorAnchor.Y / 1000f) + (height / 2f), intake.Position.Z);
        pad.AddChild(intake);
        return pad;
    }

    private static MeshInstance3D Marker(string name, Vec3Mm at, Mesh mesh, Color color) => new()
    {
        Name = name,
        Position = new Vector3(at.X / 1000f, (at.Y / 1000f) + LiftM, at.Z / 1000f),
        Mesh = mesh,
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
    };
}
