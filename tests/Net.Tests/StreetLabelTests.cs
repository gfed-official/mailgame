using System;
using PerformativeMail.Client;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The ec62063 playtest's N21: on the Large Island's map the street names overprinted each other.</summary>
    public class StreetLabelTests
    {
        /// <summary>About a name's length at a size, a little generous.</summary>
        private static float Width(string name, int size) => name.Length * size * 0.52f + 6f;

        private static void AssertClear(WorldMap map, uint seed, float innerWidth, float innerHeight)
        {
            float scale = Math.Min(innerWidth / map.WidthM, innerHeight / map.DepthM);
            var labels = StreetLabels.Place(map, 74f, 216f, scale, Width);
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var s in map.Streets) names.Add(s.Name);
            Assert.Equal(names.Count, labels.Count);
            for (int i = 0; i < labels.Count; i++)
                for (int j = i + 1; j < labels.Count; j++)
                    Assert.False(labels[i].Box.Overlaps(labels[j].Box), $"{map.Id} #{seed}: \"{labels[i].Name}\" lies on \"{labels[j].Name}\"");
        }

        [Fact]
        public void TheLargeIslandsStreetNamesLieOffEachOtherAt1280x720()
        {
            // The map panel's inner rectangle at 1280×720 (MapPanel.PanelArea less its head).
            for (uint seed = 1; seed <= 8; seed++) AssertClear(LargeIslandGenerator.Generate(Shared.Content, seed), seed, 1132f, 279f);
        }

        [Fact]
        public void TheOtherWorldsStreetNamesLieOffEachOther()
        {
            for (uint seed = 1; seed <= 4; seed++) AssertClear(WorldGenerator.Generate(Shared.Content, seed), seed, 1132f, 279f);
        }

        [Fact]
        public void ANameStandsInTheMiddleOfItsLongestStretchWhenThatIsClear()
        {
            var map = WorldGenerator.Generate(Shared.Content, 1);
            var labels = StreetLabels.Place(map, 0f, 0f, 4f, Width);
            var first = labels[0];
            StreetDef? longest = null;
            foreach (var s in map.Streets)
                if (s.Name == first.Name && (longest == null || Math.Max(s.Rect.W, s.Rect.H) > Math.Max(longest.Rect.W, longest.Rect.H))) longest = s;
            RoutingGraph.CentreLine(longest!, out float x0, out float z0, out float x1, out float z1);
            Assert.Equal((x0 + x1) * 0.5f * 4f, first.X, 2);
            Assert.Equal((z0 + z1) * 0.5f * 4f, first.Y, 2);
        }
    }
}
