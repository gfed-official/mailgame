using System;
using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// A health bar over something in the world is drawn under the HUD, and the top plate lets the view through: a
    /// raider up close had its bar across "Shift 4 / 5 DELIVERY" (the e5be644 playtest's N10). A bar whose quad
    /// would fall on a box the HUD keeps clear is not drawn that frame.
    /// </summary>
    public static class WorldBarClear
    {
        /// <summary>False if the bar (<paramref name="width"/> × <paramref name="height"/> m, facing the camera, centred on <paramref name="centre"/>) lies on any of <paramref name="keepClear"/>.</summary>
        public static bool Clear(Camera3D camera, Vector3 centre, float width, float height, IReadOnlyList<ScreenBox> keepClear)
        {
            if (keepClear.Count == 0 || camera.IsPositionBehind(centre)) return true;
            var basis = camera.GlobalBasis;
            Vector3 right = basis.X * (width * 0.5f), up = basis.Y * (height * 0.5f);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var corner in new[] { centre - right - up, centre + right - up, centre - right + up, centre + right + up })
            {
                var p = camera.UnprojectPosition(corner);
                x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X);
                y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y);
            }
            var box = new ScreenBox(x0, y0, x1, y1);
            for (int i = 0; i < keepClear.Count; i++)
                if (box.Overlaps(keepClear[i])) return false;
            return true;
        }
    }
}
