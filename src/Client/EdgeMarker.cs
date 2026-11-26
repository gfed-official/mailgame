using System;

namespace PerformativeMail.Client
{
    /// <summary>
    /// Where a marker for a point in the world goes on screen (doc 05 §5 "the HUD shows a compass
    /// marker for any construct under attack"): over the point where it is in view, else on the
    /// screen's edge (inset by a margin) in its direction from the middle — a point behind the
    /// camera projects mirrored, so its direction is turned round.
    /// </summary>
    public static class EdgeMarker
    {
        /// <returns>True if the point is in view and the marker stands over it.</returns>
        /// <param name="angle">The way the marker points, in screen radians (0 right, π/2 down).</param>
        public static bool Place(float px, float py, bool behind, float width, float height, float margin, out float x, out float y, out float angle)
        {
            float cx = width * 0.5f, cy = height * 0.5f;
            if (!behind && px >= margin && px <= width - margin && py >= margin && py <= height - margin)
            {
                x = px; y = py;
                angle = MathF.PI / 2f;
                return true;
            }
            float dx = px - cx, dy = py - cy;
            if (behind) { dx = -dx; dy = -dy; }
            // Straight behind: it is below, as far as the screen goes.
            if (MathF.Abs(dx) + MathF.Abs(dy) < 1e-3f) dy = 1f;
            float hw = MathF.Max(1f, cx - margin), hh = MathF.Max(1f, cy - margin);
            float k = MathF.Min(MathF.Abs(dx) < 1e-6f ? float.MaxValue : hw / MathF.Abs(dx), MathF.Abs(dy) < 1e-6f ? float.MaxValue : hh / MathF.Abs(dy));
            x = cx + dx * k;
            y = cy + dy * k;
            angle = MathF.Atan2(dy, dx);
            return false;
        }
    }
}
