using System;
using System.Collections.Generic;

namespace PerformativeMail.Client
{
    /// <summary>A rectangle on the screen, in the HUD's units.</summary>
    public readonly struct ScreenBox
    {
        public readonly float X0, Y0, X1, Y1;

        public ScreenBox(float x0, float y0, float x1, float y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public static ScreenBox Around(float centreX, float centreY, float width, float height) =>
            new ScreenBox(centreX - width * 0.5f, centreY - height * 0.5f, centreX + width * 0.5f, centreY + height * 0.5f);

        public bool Overlaps(in ScreenBox other) => X0 < other.X1 && other.X0 < X1 && Y0 < other.Y1 && other.Y0 < Y1;
    }

    /// <summary>One world label as the screen sees it this frame.</summary>
    public sealed class LabelCandidate
    {
        /// <summary>Where its text would be drawn.</summary>
        public ScreenBox Box;
        /// <summary>Metres from the camera.</summary>
        public float Distance;
        /// <summary>A higher rank is placed first: a carrier's name before an address, an address before a house number.</summary>
        public int Rank;
        /// <summary>0..1 from <see cref="LabelDeclutter.DistanceFade"/>; at 0 the label takes no room.</summary>
        public float Fade = 1f;
        /// <summary>Set by <see cref="LabelDeclutter.Resolve"/>.</summary>
        public bool Shown;
        /// <summary>Whose candidate this is.</summary>
        public object? Owner;
    }

    /// <summary>
    /// Which world labels are drawn (playtest nits 14 and 15). From far away the labels of a whole
    /// street fall on the same few pixels, and any of them can drift under the HUD: a label thins out
    /// with distance, gives way to a nearer one it would cover, and never lies on the HUD's own text.
    /// </summary>
    public static class LabelDeclutter
    {
        /// <summary>1 up to <paramref name="fadeStart"/> metres, 0 from <paramref name="fadeEnd"/> on; a label without an end is read from anywhere.</summary>
        public static float DistanceFade(float distance, float fadeStart, float fadeEnd)
        {
            if (fadeEnd <= 0f) return 1f;
            if (distance <= fadeStart) return 1f;
            if (distance >= fadeEnd) return 0f;
            return (fadeEnd - distance) / (fadeEnd - fadeStart);
        }

        /// <summary>
        /// Sets <see cref="LabelCandidate.Shown"/>: by rank, then nearest first, a label is shown
        /// unless it has faded out, lies on one of the <paramref name="keepClear"/> boxes (the HUD)
        /// or on a label already shown. Reorders <paramref name="labels"/>.
        /// </summary>
        public static void Resolve(List<LabelCandidate> labels, IReadOnlyList<ScreenBox> keepClear)
        {
            labels.Sort(Order);
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                label.Shown = label.Fade > 0f && !Covered(label, labels, i, keepClear);
            }
        }

        private static readonly Comparison<LabelCandidate> Order = (a, b) =>
            a.Rank != b.Rank ? b.Rank.CompareTo(a.Rank) : a.Distance.CompareTo(b.Distance);

        private static bool Covered(LabelCandidate label, List<LabelCandidate> placed, int count, IReadOnlyList<ScreenBox> keepClear)
        {
            for (int i = 0; i < keepClear.Count; i++)
                if (label.Box.Overlaps(keepClear[i])) return true;
            for (int i = 0; i < count; i++)
                if (placed[i].Shown && label.Box.Overlaps(placed[i].Box)) return true;
            return false;
        }
    }
}
