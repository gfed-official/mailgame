using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Playtest f32ea2e: a marker on the top edge, for a construct straight ahead or behind, had its
        /// words over the shift's plate. A tip inside a box the HUD keeps clear goes down to just under it.
        /// </summary>
        public static float TipClear(float x, float y, IReadOnlyList<ScreenBox>? keep, float gap = 24f)
        {
            if (keep == null) return y;
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                foreach (var b in keep)
                    // The chevron reaches back up to a gap over its tip.
                    if (x >= b.X0 && x <= b.X1 && y >= b.Y0 && y < b.Y1 + gap) { y = b.Y1 + gap; moved = true; }
                if (!moved) break;
            }
            return y;
        }

        /// <summary>
        /// The top of the marker's words, a box of <paramref name="w"/> by <paramref name="h"/> at
        /// (<paramref name="x"/>, <paramref name="y"/>), moved down under any box the HUD keeps clear
        /// that it would sit on (and kept on a screen <paramref name="height"/> high).
        /// </summary>
        public static float WordsClear(float x, float y, float w, float h, IReadOnlyList<ScreenBox>? keep, float height, float gap = 6f)
        {
            if (keep == null) return y;
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                var words = new ScreenBox(x, y, x + w, y + h);
                foreach (var b in keep)
                    if (words.Overlaps(b) && b.Y1 + gap + h <= height) { y = b.Y1 + gap; words = new ScreenBox(x, y, x + w, y + h); moved = true; }
                if (!moved) break;
            }
            return y;
        }
    }
}
