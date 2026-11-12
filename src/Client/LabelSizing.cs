using System;

namespace PerformativeMail.Client
{
    /// <summary>
    /// How big a world-space label may get on screen. A label has a real size in the world, so from
    /// far away it shrinks like everything else; walking up to it must not let it fill the view.
    /// </summary>
    public static class LabelSizing
    {
        /// <summary>Share of the screen height that <paramref name="worldHeight"/> metres cover at <paramref name="distance"/> metres.</summary>
        public static float ScreenFraction(float worldHeight, float distance, float fovYRadians)
        {
            float visible = 2f * MathF.Max(distance, 0.01f) * MathF.Tan(fovYRadians * 0.5f);
            return worldHeight / visible;
        }

        /// <summary>
        /// The scale (0..1] to draw a label of <paramref name="worldHeight"/> metres with so that it
        /// covers at most <paramref name="maxScreenFraction"/> of the screen height: 1 from far away,
        /// smaller the closer the camera comes.
        /// </summary>
        public static float Scale(float worldHeight, float distance, float fovYRadians, float maxScreenFraction)
        {
            if (worldHeight <= 0f || maxScreenFraction <= 0f) return 1f;
            float fraction = ScreenFraction(worldHeight, distance, fovYRadians);
            return fraction <= maxScreenFraction ? 1f : maxScreenFraction / fraction;
        }
    }
}
