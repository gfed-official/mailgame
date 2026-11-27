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

        /// <summary>How far the chevron reaches back from its tip, and to either side, px.</summary>
        public const float ChevronBack = 20f, ChevronHalfWidth = 12f;

        /// <summary>
        /// Playtest f32ea2e: a marker on the top edge, for a construct straight ahead or behind, had its
        /// words over the shift's plate. A chevron that would stand on a box the HUD keeps clear goes down
        /// to just under it. The whole chevron is held off, not its tip: pointing up, its tip stood just
        /// over "PROTECTED in the yard" and its body on the words (the e36633c playtest).
        /// </summary>
        /// <param name="angle">The way it points, in screen radians (0 right, π/2 down).</param>
        public static float TipClear(float x, float y, float angle, IReadOnlyList<ScreenBox>? keep, float gap = 4f)
        {
            if (keep == null) return y;
            Extent(angle, out float x0, out float y0, out float x1, out float y1);
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                foreach (var b in keep)
                    if (new ScreenBox(x + x0, y + y0, x + x1, y + y1).Overlaps(b)) { y = b.Y1 + gap - y0; moved = true; }
                if (!moved) break;
            }
            return y;
        }

        /// <summary>The box the chevron pointing <paramref name="angle"/> covers, about its tip.</summary>
        public static void Extent(float angle, out float x0, out float y0, out float x1, out float y1)
        {
            float dx = MathF.Cos(angle), dy = MathF.Sin(angle);
            float bx = -dx * ChevronBack, by = -dy * ChevronBack, sx = -dy * ChevronHalfWidth, sy = dx * ChevronHalfWidth;
            x0 = MathF.Min(0f, MathF.Min(bx + sx, bx - sx)) - 2f;
            x1 = MathF.Max(0f, MathF.Max(bx + sx, bx - sx)) + 2f;
            y0 = MathF.Min(0f, MathF.Min(by + sy, by - sy)) - 2f;
            y1 = MathF.Max(0f, MathF.Max(by + sy, by - sy)) + 2f;
        }

        /// <summary>
        /// Where the marker's words go about its tip, a box of <paramref name="w"/> by <paramref name="h"/> (the f40acb7
        /// playtest: the chevron and "Post Office under attack" were each cleared of the HUD on their own and drifted
        /// apart). Over the construct the words stand over the chevron; on the edge they stand beside it, level with
        /// its middle, on the side towards the middle of a screen <paramref name="width"/> wide.
        /// </summary>
        public static ScreenBox WordsAbout(float tipX, float angle, bool over, float w, float h, float width, float gap = 6f)
        {
            Extent(angle, out float x0, out float y0, out float x1, out float y1);
            if (over) return new ScreenBox(-w * 0.5f, y0 - gap - h, w * 0.5f, y0 - gap);
            float my = (y0 + y1) * 0.5f;
            bool left = tipX > width * 0.5f;
            // Kept on the screen: beside a chevron near the edge the other way.
            if (left && tipX + x0 - gap - w < 8f) left = false;
            else if (!left && tipX + x1 + gap + w > width - 8f) left = true;
            float wx = left ? x0 - gap - w : x1 + gap;
            return new ScreenBox(wx, my - h * 0.5f, wx + w, my + h * 0.5f);
        }

        /// <summary>
        /// The tip's height for the chevron and its words as one, <paramref name="group"/> about the tip: moved down
        /// under every box the HUD keeps clear it would touch, or, where that runs off a screen <paramref name="height"/>
        /// high, up over them; kept on the screen either way. The two never part.
        /// </summary>
        public static float GroupClear(float x, float y, in ScreenBox group, IReadOnlyList<ScreenBox>? keep, float height, float gap = 4f)
        {
            float lo = 8f - group.Y0, hi = height - 8f - group.Y1;
            y = MathF.Max(lo, MathF.Min(hi, y));
            if (keep == null) return y;
            float down = Shift(x, y, group, keep, gap, 1f);
            if (down <= hi && Clear(x, down, group, keep)) return down;
            float up = Shift(x, y, group, keep, gap, -1f);
            if (up >= lo && Clear(x, up, group, keep)) return up;
            return MathF.Max(lo, MathF.Min(hi, down));
        }

        private static float Shift(float x, float y, in ScreenBox g, IReadOnlyList<ScreenBox> keep, float gap, float way)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                bool moved = false;
                foreach (var b in keep)
                    if (new ScreenBox(x + g.X0, y + g.Y0, x + g.X1, y + g.Y1).Overlaps(b))
                    {
                        y = way > 0f ? b.Y1 + gap - g.Y0 : b.Y0 - gap - g.Y1;
                        moved = true;
                    }
                if (!moved) break;
            }
            return y;
        }

        private static bool Clear(float x, float y, in ScreenBox g, IReadOnlyList<ScreenBox> keep)
        {
            var at = new ScreenBox(x + g.X0, y + g.Y0, x + g.X1, y + g.Y1);
            foreach (var b in keep)
                if (at.Overlaps(b)) return false;
            return true;
        }

        /// <summary>The smallest box holding both.</summary>
        public static ScreenBox Union(in ScreenBox a, in ScreenBox b) =>
            new ScreenBox(MathF.Min(a.X0, b.X0), MathF.Min(a.Y0, b.Y0), MathF.Max(a.X1, b.X1), MathF.Max(a.Y1, b.Y1));

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
