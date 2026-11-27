namespace PerformativeMail.Client
{
    /// <summary>
    /// Where the HUD's pieces go while a panel covers the middle of the screen (the f40acb7 playtest: the open
    /// Intake hid the Post Office's HP row and the "Close Intake [E]" prompt under it during a raid).
    /// </summary>
    public static class PanelDodge
    {
        /// <summary>
        /// Whether the tracker column (<paramref name="rows"/> rows of <paramref name="rowHeight"/>, <paramref name="gap"/>
        /// apart, between <paramref name="x0"/> and <paramref name="x1"/>) stands from the top right rather than from the
        /// bottom right: only when the panel is on it from the bottom and not from the top.
        /// </summary>
        public static bool TrackersFromTop(int rows, float x0, float x1, float height, float margin, float rowHeight, float gap, ScreenBox? panel)
        {
            if (panel is not { } p || rows == 0) return false;
            float tall = rows * rowHeight + (rows - 1) * gap;
            var bottom = new ScreenBox(x0, height - margin - tall, x1, height - margin);
            var top = new ScreenBox(x0, margin, x1, margin + tall);
            return bottom.Overlaps(p) && !top.Overlaps(p);
        }

        /// <summary>
        /// Where the prompt's row goes, <paramref name="tall"/> high, its words <paramref name="wide"/> across: across the
        /// screen at <paramref name="top"/> unless the panel is on it; then under the panel if it fits over
        /// <paramref name="floor"/>, else over it if that stays under <paramref name="ceiling"/>, else in the gutter left of
        /// it, level with its middle; failing all, under it, pressed up to the floor.
        /// </summary>
        public static ScreenBox Prompt(float top, float tall, float wide, ScreenBox? panel, float ceiling, float floor, float width, float margin, float gap = 8f)
        {
            if (panel is not { } p || top + tall <= p.Y0 || top >= p.Y1) return new ScreenBox(margin, top, width - margin, top + tall);
            float below = p.Y1 + gap;
            if (below + tall <= floor) return new ScreenBox(margin, below, width - margin, below + tall);
            float above = p.Y0 - gap - tall;
            if (above >= ceiling) return new ScreenBox(margin, above, width - margin, above + tall);
            if (p.X0 - gap - margin >= wide)
            {
                float mid = (p.Y0 + p.Y1) * 0.5f;
                return new ScreenBox(margin, mid - tall * 0.5f, p.X0 - gap, mid + tall * 0.5f);
            }
            float y = System.MathF.Min(below, floor - tall);
            return new ScreenBox(margin, y, width - margin, y + tall);
        }
    
        /// <summary>
        /// Where a control's edges fall along one axis once laid out: its anchors on the parent's
        /// <paramref name="parent"/> span plus its offsets, grown to <paramref name="min"/> towards
        /// <paramref name="grow"/> (−1 the start, 1 the end, 0 both ways) when that is short (Godot's own rule). The
        /// HUD asks it of a panel whose lines were made this frame, before the container has sized itself (the Payday
        /// card's first frame, when its last rect was still empty).
        /// </summary>
        public static (float Start, float End) LaidOut(float anchor0, float anchor1, float offset0, float offset1, float parent, float min, int grow)
        {
            float start = anchor0 * parent + offset0, end = anchor1 * parent + offset1;
            float lack = min - (end - start);
            if (lack <= 0f) return (start, end);
            if (grow < 0) return (start - lack, end);
            if (grow > 0) return (start, end + lack);
            return (start - lack * 0.5f, end + lack * 0.5f);
        }
    }
}
