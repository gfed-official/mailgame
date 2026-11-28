using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>
    /// Where the map writes each street's name: once per street, off the names already written. The
    /// ec62063 playtest's N21: in the Large Island's start district "Slate Street" (down) cut through
    /// "Mackerel Mews" and "Larch Lane" ran under "Slate Street" and "Puffin Place", each in the middle
    /// of its longest stretch.
    /// </summary>
    public static class StreetLabels
    {
        public const float Thickness = 16f;

        public readonly struct Label
        {
            public readonly string Name;
            /// <summary>The middle of the name, in the map's pixels.</summary>
            public readonly float X, Y;
            public readonly bool AlongX;
            /// <summary>The name's font size, px.</summary>
            public readonly int Size;
            public readonly ScreenBox Box;

            public Label(string name, float x, float y, bool alongX, int size, ScreenBox box)
            {
                Size = size;
                Name = name;
                X = x;
                Y = y;
                AlongX = alongX;
                Box = box;
            }
        }

        /// <summary>Along a stretch, where its name may stand: the middle first, then out towards its ends.</summary>
        private static readonly float[] Along = { 0.5f, 0.4f, 0.6f, 0.3f, 0.7f, 0.2f, 0.8f, 0.1f, 0.9f, 0f, 1f };

        /// <summary>
        /// The names of <paramref name="map"/>'s streets, a pixel at (<paramref name="originX"/>, <paramref name="originY"/>)
        /// + metres × <paramref name="scale"/>; <paramref name="nameWidth"/> is a name's length in pixels at a font size. The
        /// longest streets choose first; each takes the first place on its stretches (longest first) clear of the names
        /// before it — on the street, then beside it, then smaller — else the place that covers them least. Then a name that
        /// still lies on another chooses again among all the others, a few times over.
        /// </summary>
        public static List<Label> Place(WorldMap map, float originX, float originY, float scale, Func<string, int, float> nameWidth)
        {
            var byName = new Dictionary<string, List<StreetDef>>();
            foreach (var s in map.Streets)
            {
                if (!byName.TryGetValue(s.Name, out var list)) byName[s.Name] = list = new List<StreetDef>();
                list.Add(s);
            }
            static int Length(StreetDef s) => Math.Max(s.Rect.W, s.Rect.H);
            var choices = byName.OrderByDescending(p => p.Value.Max(Length)).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => Candidates(p.Key, p.Value.OrderByDescending(Length), originX, originY, scale, nameWidth)).ToList();
            var placed = new List<Label>();
            foreach (var candidates in choices)
                if (candidates.Count > 0) placed.Add(Best(candidates, placed, -1));
            for (int round = 0; round < 4; round++)
            {
                bool moved = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    if (Cover(placed[i].Box, placed, i) <= 0f) continue;
                    var again = Best(choices.First(c => c[0].Name == placed[i].Name), placed, i);
                    if (Cover(again.Box, placed, i) >= Cover(placed[i].Box, placed, i)) continue;
                    placed[i] = again;
                    moved = true;
                }
                if (!moved) break;
            }
            return placed;
        }

        /// <summary>Where a name may stand, the likeliest first.</summary>
        private static List<Label> Candidates(string name, IEnumerable<StreetDef> stretches, float originX, float originY, float scale, Func<string, int, float> nameWidth)
        {
            var list = new List<Label>();
            var ordered = stretches.ToList();
            foreach (int size in Sizes)
            {
                float width = nameWidth(name, size);
                foreach (float side in Sides)
                    foreach (var s in ordered)
                    {
                        RoutingGraph.CentreLine(s, out float x0, out float z0, out float x1, out float z1);
                        float ax = originX + x0 * scale, ay = originY + z0 * scale, bx = originX + x1 * scale, by = originY + z1 * scale;
                        bool alongX = s.Rect.W >= s.Rect.H;
                        float length = alongX ? bx - ax : by - ay;
                        // On its stretch where the stretch is long enough for it; else its middle somewhere on it.
                        foreach (float t in Along)
                        {
                            float d = length > width ? width * 0.5f + (length - width) * t : length * t;
                            float x = alongX ? ax + d : ax + side, y = alongX ? ay + side : ay + d;
                            var box = alongX ? ScreenBox.Around(x, y, width, Thickness) : ScreenBox.Around(x, y, Thickness, width);
                            list.Add(new Label(name, x, y, alongX, size, box));
                        }
                    }
            }
            return list;
        }

        /// <summary>The first candidate clear of the placed names (but the <paramref name="skip"/>th), else the least covering.</summary>
        private static Label Best(List<Label> candidates, List<Label> placed, int skip)
        {
            Label best = candidates[0];
            float bestCover = float.MaxValue;
            foreach (var c in candidates)
            {
                float cover = Cover(c.Box, placed, skip);
                if (cover <= 0f) return c;
                if (cover < bestCover)
                {
                    bestCover = cover;
                    best = c;
                }
            }
            return best;
        }

        private static float Cover(in ScreenBox box, List<Label> placed, int skip)
        {
            float cover = 0f;
            for (int i = 0; i < placed.Count; i++)
                if (i != skip) cover += Overlap(box, placed[i].Box);
            return cover;
        }

        /// <summary>The name's sizes, px, and how far off its street's middle line it may stand.</summary>
        private static readonly int[] Sizes = { 12, 11, 10 };
        private static readonly float[] Sides = { 0f, Thickness - 2f, 2f - Thickness };

        private static float Overlap(in ScreenBox a, in ScreenBox b) =>
            Math.Max(0f, Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0)) * Math.Max(0f, Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0));
    }
}
