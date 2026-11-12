using System;
using PerformativeMail.Client;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>World labels keep their size from far away and are capped on screen up close.</summary>
    public class LabelSizingTests
    {
        private const float Fov = 75f * MathF.PI / 180f;
        private const float Cap = 0.05f;

        [Fact]
        public void FarAwayALabelKeepsItsWorldSize()
        {
            // A 1.6 m house number 60 m away covers under 2 % of the screen: no need to shrink it.
            Assert.True(LabelSizing.ScreenFraction(1.6f, 60f, Fov) < Cap);
            Assert.Equal(1f, LabelSizing.Scale(1.6f, 60f, Fov, Cap));
        }

        [Theory]
        [InlineData(1.6f, 8f)]
        [InlineData(1.6f, 2f)]
        [InlineData(0.56f, 1f)]
        [InlineData(0.72f, 0.2f)]
        [InlineData(0.72f, 0f)]
        public void UpCloseALabelIsShrunkToTheCap(float worldHeight, float distance)
        {
            float scale = LabelSizing.Scale(worldHeight, distance, Fov, Cap);
            Assert.InRange(scale, 0.0001f, 1f);
            Assert.True(scale < 1f);
            Assert.Equal(Cap, LabelSizing.ScreenFraction(worldHeight * scale, distance, Fov), 4);
        }

        [Fact]
        public void TheScaleNeverGrowsALabelAndShrinksSmoothly()
        {
            float last = 0f;
            for (float d = 0.1f; d < 100f; d += 0.1f)
            {
                float scale = LabelSizing.Scale(1.6f, d, Fov, Cap);
                Assert.InRange(scale, last, 1f);
                last = scale;
            }
            Assert.Equal(1f, last);
        }
    }
}
