using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// Where the raid comes from and what the Alarm Posts (doc 05 §4) do about it: the rules the
    /// server schedules by and a client marks the raiders and words the build prompt by.
    /// </summary>
    public static class RaidRules
    {
        /// <summary>The first way in: the east end of the first street ("from the east"), open from shift 1.</summary>
        public static void Gate(WorldMap map, out float x, out float z)
        {
            var edge = map.SpawnEdges[0];
            x = edge.X;
            z = edge.Z;
        }

        /// <summary>
        /// True if an alarm of this kind on the tile is in range of a way in open by the shift
        /// (doc 11: "+15 s per Alarm Post in range"; design choice 92).
        /// </summary>
        public static bool AlarmInRange(WorldMap map, BuildingDef def, int tx, int tz, int shift)
        {
            if (def.Behaviour != BuildingBehaviour.Alarm) return false;
            float r2 = def.RevealRadius * def.RevealRadius;
            foreach (var edge in map.SpawnEdges)
            {
                if (!edge.OpenAt(shift)) continue;
                float dx = BuildRules.TileCentre(tx) - edge.X, dz = BuildRules.TileCentre(tz) - edge.Z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        /// <summary>Seconds of warning the Alarm Posts that stand add, all of them together.</summary>
        public static double AlarmBonusSeconds(WorldMap map, ConstructField built, int shift)
        {
            double bonus = 0;
            foreach (var c in built.All)
                if (AlarmInRange(map, c.Def, c.Tx, c.Tz, shift)) bonus += c.Def.WarningBonusSeconds;
            return bonus;
        }

        /// <summary>
        /// Doc 05 §3.2: "1 edge for pulses 1–3, 2 edges for 4–6": which of the raid's two ways in
        /// the <paramref name="nth"/> raider of pulse <paramref name="pulse"/> (from 0, of
        /// <paramref name="pulses"/>) comes by — the second half of the pulses take turns.
        /// </summary>
        public static int PulseEdge(int pulse, int pulses, int nth, int first, int second) =>
            pulse * 2 < pulses || second < 0 || nth % 2 == 0 ? first : second;

        /// <summary>The warning before a raid: the perks' and the posts', no longer than the cap.</summary>
        public static double WarningSeconds(WaveDef waves, double withPerks, double alarmBonus)
        {
            double seconds = withPerks + alarmBonus;
            return waves.MaxWarningSeconds > 0 ? Math.Min(seconds, Math.Max(waves.MaxWarningSeconds, withPerks)) : seconds;
        }

        /// <summary>True if a raider at the point is within the reveal radius of an Alarm Post: marked for everybody.</summary>
        public static bool Revealed(ConstructField built, float x, float z)
        {
            foreach (var c in built.All)
            {
                if (c.Def.Behaviour != BuildingBehaviour.Alarm) continue;
                float dx = x - c.X, dz = z - c.Z;
                if (dx * dx + dz * dz <= c.Def.RevealRadius * c.Def.RevealRadius) return true;
            }
            return false;
        }
    }
}
