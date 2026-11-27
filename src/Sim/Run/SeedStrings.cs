using System;
using System.Collections.Generic;
using System.Globalization;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// The shareable seed string of doc 01 §5.3, "PM1-SMALL-7F3A9C21-CM.DR": the version, the world's tag, the seed in
    /// hex and the run's Postage Stamps by their codes, in the content's order. Written at Results and read back in
    /// the Lobby, so a run can be played again elsewhere.
    /// </summary>
    public static class SeedStrings
    {
        public const string Version = "PM1";

        /// <summary>"SMALL" for the Small Island, else the map id's first word: "TEST" for test_street.</summary>
        public static string Tag(string mapId) => mapId == WorldGenerator.SmallIsland ? "SMALL" : mapId.Split('_')[0].ToUpperInvariant();

        /// <summary>"PM1-TEST-00000001", and "-CM.RH" after it when stamps are on.</summary>
        public static string Format(ContentDb content, string mapId, uint seed, int stamps)
        {
            string text = $"{Version}-{Tag(mapId)}-{seed.ToString("X8", CultureInfo.InvariantCulture)}";
            var codes = new List<string>();
            foreach (var s in content.Stamps)
                if ((stamps & (1 << s.Index)) != 0) codes.Add(s.Code);
            return codes.Count == 0 ? text : text + "-" + string.Join(".", codes);
        }

        /// <summary>
        /// Reads a seed string back: a world the Lobby offers (<see cref="SimWorld.WorldChoices"/>), a seed of 1–8 hex
        /// digits, stamps by their codes. Case and the blanks around it do not matter. False with what is wrong.
        /// </summary>
        public static bool TryParse(ContentDb content, string? text, out int worldChoice, out uint seed, out byte stamps, out string error)
        {
            worldChoice = -1;
            seed = 0;
            stamps = 0;
            string[] parts = (text ?? "").Trim().ToUpperInvariant().Split('-');
            if (parts.Length < 3 || parts.Length > 4 || parts[0] != Version)
            {
                error = $"not a seed string (\"{Version}-SMALL-7F3A9C21\")";
                return false;
            }
            for (int i = 0; i < SimWorld.WorldChoices.Count; i++)
                if (Tag(SimWorld.WorldChoices[i]) == parts[1]) worldChoice = i;
            if (worldChoice < 0)
            {
                error = $"no world '{parts[1]}'";
                return false;
            }
            if (parts[2].Length < 1 || parts[2].Length > 8 || !uint.TryParse(parts[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out seed) || seed == 0)
            {
                error = $"'{parts[2]}' is no seed";
                return false;
            }
            if (parts.Length == 4)
                foreach (string code in parts[3].Split('.'))
                {
                    StampDef? stamp = null;
                    foreach (var s in content.Stamps)
                        if (s.Code == code) stamp = s;
                    if (stamp == null || (stamps & (1 << stamp.Index)) != 0)
                    {
                        error = $"no stamp '{code}'";
                        return false;
                    }
                    stamps |= (byte)(1 << stamp.Index);
                }
            error = "";
            return true;
        }
    }
}
