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
        /// <summary>Where the raiders come in and leave: the east end of the first street ("from the east").</summary>
        public static void Gate(WorldMap map, out float x, out float z)
        {
            x = map.WidthM - 1.5f;
            z = map.Streets.Count > 0 ? map.Streets[0].Rect.CenterZ : map.DepthM * 0.5f;
        }

        /// <summary>True if an alarm of this kind on the tile is in range of the raiders' way in (doc 11: "+15 s per Alarm Post in range").</summary>
        public static bool AlarmInRange(WorldMap map, BuildingDef def, int tx, int tz)
        {
            if (def.Behaviour != BuildingBehaviour.Alarm) return false;
            Gate(map, out float gx, out float gz);
            float dx = BuildRules.TileCentre(tx) - gx, dz = BuildRules.TileCentre(tz) - gz;
            return dx * dx + dz * dz <= def.RevealRadius * def.RevealRadius;
        }

        /// <summary>Seconds of warning the Alarm Posts that stand add, all of them together.</summary>
        public static double AlarmBonusSeconds(WorldMap map, ConstructField built)
        {
            double bonus = 0;
            foreach (var c in built.All)
                if (AlarmInRange(map, c.Def, c.Tx, c.Tz)) bonus += c.Def.WarningBonusSeconds;
            return bonus;
        }

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
