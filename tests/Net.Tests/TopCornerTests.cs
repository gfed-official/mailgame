using PerformativeMail.Client;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>Evan, 2026-11-27: the top-centre plate becomes small elements in the top left (design choice 168).</summary>
    public class TopCornerTests
    {
        private const float Margin = 28f;

        [Fact]
        public void TheCornersRows_StandInOrder_NoneTouchingTheNext_OnTheirPlate()
        {
            var rows = new[] { TopCorner.Phase, TopCorner.Clock, TopCorner.Wallet, TopCorner.Quota, TopCorner.QuotaBar };
            for (int i = 0; i + 1 < rows.Length; i++)
                Assert.True(rows[i].Bottom + 2f <= rows[i + 1].Top, $"row {i} touches row {i + 1}");
            var plate = TopCorner.Plate(quota: true);
            Assert.True(plate.Top <= rows[0].Top && rows[^1].Bottom <= plate.Bottom);
            // Between runs the quota rows are gone and the plate closes up under the gold.
            var between = TopCorner.Plate(quota: false);
            Assert.True(TopCorner.Wallet.Bottom <= between.Bottom && between.Bottom < plate.Bottom - 20f);
            // Inside the safe area: no line nearer the top edge than 8 px.
            Assert.True(Margin + rows[0].Top >= 8f);
        }

        [Fact]
        public void TheCorner_TakesFarLessRoomThanTheOldTopPlate()
        {
            // The old plate: 448 px across the top centre and 139 px down, the hint under it to 173 px.
            float wide = TopCorner.PlateRight(Margin) - (Margin - TopCorner.PlatePad);
            float tall = TopCorner.Plate(quota: true).Bottom - TopCorner.Plate(quota: true).Top;
            Assert.True(wide <= 280f, $"the plate is {wide} px wide");
            Assert.True(wide * tall < 448f * 139f * 0.5f, $"the plate is {wide} × {tall}");
            Assert.True(Margin + TopCorner.Plate(quota: true).Bottom < 120f);
        }

        [Fact]
        public void TheHint_FitsBetweenThePlateAndTheRightEdge_ShrinkingDownTo13Px()
        {
            // 1280 px: centred, it may reach 640 − 288 − 12 = 340 px each way.
            float room = TopCorner.HintRoom(1280f, Margin, 1280f - Margin);
            Assert.Equal(680f, room, 1);
            // The trackers from the top right (an open panel at the bottom right) leave less: they start at 922 px.
            float narrow = TopCorner.HintRoom(1280f, Margin, 1280f - Margin - 330f);
            Assert.Equal(2f * (922f - 12f - 640f), narrow, 1);
            // Words of 36 px a font px: 18 px is 648, fits 680; at the trackers' room of 540 it takes 15 px (540).
            static float Words(int size) => 36f * size;
            Assert.Equal(18, TopCorner.HintFit(Words, room));
            Assert.Equal(15, TopCorner.HintFit(Words, narrow));
            // Wider still at 13 px: none fits, and the hint goes under the plate.
            Assert.Equal(-1, TopCorner.HintFit(Words, 400f));
            // A screen too narrow for any room.
            Assert.Equal(0f, TopCorner.HintRoom(500f, Margin, 500f - Margin));
        }
    }
}
