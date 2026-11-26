using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// The Repair Hammer (doc 05 §4 "Repairs 50 HP/s to a construct; consumes materials at 25% of
    /// build cost per full HP bar"; §5 "the PO … can be repaired with the hammer"; design choice 139):
    /// what a carrier holding it mends, and what that takes, as the server decides and a client
    /// words its line by.
    /// </summary>
    public static class RepairRules
    {
        /// <summary>How far from the carrier, to the near side of what is mended, the hammer reaches, m.</summary>
        public const float Reach = 2.5f;
        /// <summary>Half the arc ahead of the carrier the hammer is held over, radians (50°).</summary>
        public const float HalfArc = 0.87f;
        /// <summary>The HP given back at a time (every 0.2 s at 50 HP/s): one event and one payment each.</summary>
        public const int Chunk = 10;

        /// <summary>Doc 01 §1: what is built is mended in Prep and in Delivery, the raid included.</summary>
        public static bool IsOpen(RunPhase phase) => phase == RunPhase.Prep || phase == RunPhase.Delivery;

        /// <summary>The tool in the hand if it mends; null otherwise.</summary>
        public static ItemDef? Hammer(ContentDb content, GridContainer hotbar, int slot) =>
            HarvestRules.Held(content, hotbar, slot) is { Repairs: true } held ? held : null;

        /// <summary>
        /// What the hammer is held at, from (<paramref name="x"/>, <paramref name="z"/>) facing
        /// <paramref name="yaw"/> (radians): the nearest hurt construct within its reach and arc, or
        /// failing one, the Post Office (<paramref name="postOffice"/>) when it is hurt and its wall
        /// within reach ahead. Null and false: nothing to mend.
        /// </summary>
        public static Construct? Target(WorldMap map, ConstructField constructs, float x, float z, float yaw, bool postOfficeHurt, out bool postOffice)
        {
            postOffice = false;
            float fx = -MathF.Sin(yaw), fz = -MathF.Cos(yaw);
            Construct? best = null;
            float bestDist = float.MaxValue;
            foreach (var c in constructs.All)
            {
                if (c.Hp >= c.Def.Hp) continue;
                float dx = c.CentreX - x, dz = c.CentreZ - z, dist = MathF.Sqrt(dx * dx + dz * dz);
                float near = MathF.Max(0f, dist - c.Def.Radius);
                if (near > Reach || near >= bestDist) continue;
                if (dist > c.Def.Radius)
                {
                    float angle = MathF.Acos(Math.Clamp((dx * fx + dz * fz) / dist, -1f, 1f));
                    if (angle > HalfArc + MathF.Asin(Math.Min(1f, c.Def.Radius / dist))) continue;
                }
                best = c;
                bestDist = near;
            }
            if (best != null || !postOfficeHurt) return best;
            float d = PostOfficeDistance(map, x, z, out float px, out float pz);
            if (d > Reach) return null;
            if (d > 1e-3f && ((px - x) * fx + (pz - z) * fz) / d < MathF.Cos(HalfArc)) return null;
            postOffice = true;
            return null;
        }

        /// <summary>How far a point is from the Post Office's walls (the map's solid rects of it), and the nearest point of them.</summary>
        public static float PostOfficeDistance(WorldMap map, float x, float z, out float nearX, out float nearZ)
        {
            var po = map.PostOffice;
            const float tile = SimConstants.TileSize;
            float best = float.MaxValue;
            nearX = po.Rect.CenterX; nearZ = po.Rect.CenterZ;
            int n = Math.Max(1, po.Solid.Count);
            for (int i = 0; i < n; i++)
            {
                var r = po.Solid.Count > 0 ? po.Solid[i] : po.Rect;
                float cx = Math.Clamp(x, r.X * tile, (r.X + r.W) * tile), cz = Math.Clamp(z, r.Y * tile, (r.Y + r.H) * tile);
                float d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (d >= best) continue;
                best = d;
                nearX = cx; nearZ = cz;
            }
            return MathF.Sqrt(best);
        }

        /// <summary>
        /// What a full bar of the construct's HP takes of one input of its recipe: the hammer's part
        /// (a quarter) of the count. A construct with no recipe is mended for nothing.
        /// </summary>
        public static float PerFullBar(RecipeInput input, ItemDef hammer) => input.Count * hammer.RepairCostRatio;

        /// <summary>The Post Office is mended as if built of balance.postOfficeRepairLogs Logs: a quarter of them a full bar.</summary>
        public const string PostOfficeItem = "log";

        /// <summary>
        /// The first thing the pockets have none of that mending the construct (or the Post Office)
        /// takes; null if nothing is missing. (The server also keeps the part of a unit already paid.)
        /// </summary>
        public static ItemDef? Missing(ContentDb content, Construct? c, bool postOffice, ItemDef hammer, GridContainer hotbar, GridContainer inventory)
        {
            if (hammer.RepairCostRatio <= 0f) return null;
            if (postOffice)
            {
                var log = content.Item(PostOfficeItem);
                return content.Balance.GetFloat("postOfficeRepairLogs") > 0f && BuildRules.Have(hotbar, inventory, log) == 0 ? log : null;
            }
            if (c?.Def.Recipe == null) return null;
            foreach (var input in c.Def.Recipe.Inputs)
                if (input.Count > 0 && BuildRules.Have(hotbar, inventory, input.Item) == 0) return input.Item;
            return null;
        }
    }
}
