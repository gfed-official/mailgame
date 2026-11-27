using System;

namespace PerformativeMail.Client
{
    /// <summary>
    /// The run's state in the top left corner (Evan, 2026-11-27: "smaller elements in the top left"; design choice 168):
    /// from the top, the shift's line ("Shift 2 / 5   Delivery 3:12"), the clock's thin bar, the gold, the quota's line
    /// and its thin bar, on one small plate inside the safe area. Between runs the quota rows are gone and the plate
    /// closes up under the gold. The hint is centred on the top row, between the plate and the right edge.
    /// </summary>
    public static class TopCorner
    {
        /// <summary>How wide the corner's lines are, px; the plate reaches <see cref="PlatePad"/> past them each side.</summary>
        public const float Width = 250f, PlatePad = 10f;
        public const int PhaseSize = 14, WalletSize = 18, QuotaSize = 12, EarnPopSize = 15;
        /// <summary>The hint's largest and smallest font size, px: it shrinks to fit its room on the top row.</summary>
        public const int HintSize = 18, HintMinSize = 13;
        /// <summary>The gap between the plate (or the trackers) and the hint's words, px.</summary>
        public const float HintGap = 12f;

        /// <summary>Each row's top and foot, px down from the safe margin.</summary>
        public static readonly (float Top, float Bottom) Phase = (-6f, 14f), Clock = (16f, 19f), Wallet = (21f, 46f), Quota = (48f, 67f), QuotaBar = (70f, 74f);

        /// <summary>The plate's top and foot, px down from the safe margin, with the quota rows or without (between runs).</summary>
        public static (float Top, float Bottom) Plate(bool quota) => (-12f, quota ? QuotaBar.Bottom + 6f : Wallet.Bottom + 6f);

        /// <summary>The hint's row on top, px down from the safe margin.</summary>
        public static readonly (float Top, float Bottom) Hint = (-4f, 24f);

        /// <summary>The plate's right edge on screen, px.</summary>
        public static float PlateRight(float margin) => margin + Width + PlatePad;

        /// <summary>
        /// How wide the centred hint may be on a screen <paramref name="width"/> across: clear of the plate on the left
        /// and of <paramref name="right"/> (the screen's safe edge, or the trackers while they stand from the top right).
        /// </summary>
        public static float HintRoom(float width, float margin, float right)
        {
            float mid = width * 0.5f;
            float half = Math.Min(mid - PlateRight(margin) - HintGap, right - HintGap - mid);
            return Math.Max(0f, 2f * half);
        }

        /// <summary>The largest size from <see cref="HintSize"/> down to <see cref="HintMinSize"/> whose words fit <paramref name="room"/>; -1 if none does.</summary>
        public static int HintFit(Func<int, float> wordsWidth, float room)
        {
            for (int size = HintSize; size >= HintMinSize; size--)
                if (wordsWidth(size) <= room) return size;
            return -1;
        }
    }
}
