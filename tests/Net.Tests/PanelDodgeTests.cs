using PerformativeMail.Client;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The f40acb7 playtest: the open Intake covered the Post Office's HP row and "Close Intake [E]".</summary>
    public class PanelDodgeTests
    {
        // The inventory screen with the Intake at 1280×720, its tally line on top since UI polish plan item 4.
        private static readonly ScreenBox Intake = new ScreenBox(253f, 170f, 1037f, 580f);

        [Fact]
        public void TheTrackers_StandFromTheTopRight_WhileThePanelIsOnThemAtTheBottom()
        {
            // Post Office, raid, bag, perks: four rows up from the bottom reach 562 px, under the panel's foot.
            Assert.True(PanelDodge.TrackersFromTop(4, 927f, 1252f, 720f, 28f, 28f, 6f, Intake));
            // One row stays at the bottom, clear of it.
            Assert.False(PanelDodge.TrackersFromTop(1, 927f, 1252f, 720f, 28f, 28f, 6f, Intake));
            // No panel, nothing moves.
            Assert.False(PanelDodge.TrackersFromTop(4, 927f, 1252f, 720f, 28f, 28f, 6f, null));
            // A panel that reaches the top too: they stay where they were.
            Assert.False(PanelDodge.TrackersFromTop(4, 927f, 1252f, 720f, 28f, 28f, 6f, new ScreenBox(253f, 20f, 1037f, 700f)));
        }

        [Fact]
        public void ThePrompt_GoesUnderThePanel_ElseOverIt_ElseBesideIt()
        {
            // Under the crosshair at 400 px, in the panel: under its foot, over the hotbar.
            var under = PanelDodge.Prompt(400f, 50f, 160f, Intake, 180f, 642f, 1280f, 28f);
            Assert.Equal(588f, under.Y0, 1);
            Assert.Equal(28f, under.X0, 1);
            // No room under it: over its head, if that is under the hint.
            var tall = new ScreenBox(253f, 250f, 1037f, 600f);
            Assert.Equal(186f, PanelDodge.Prompt(400f, 56f, 160f, tall, 180f, 642f, 1280f, 28f).Y0, 1);
            // No room over it either: in the gutter on its left, level with its middle.
            var big = new ScreenBox(253f, 170f, 1037f, 620f);
            var gutter = PanelDodge.Prompt(400f, 56f, 160f, big, 180f, 642f, 1280f, 28f);
            Assert.Equal(245f, gutter.X1, 1);
            Assert.Equal(395f - 28f, gutter.Y0, 1);
            Assert.False(gutter.Overlaps(big));
            // Clear of the panel or no panel: where it was.
            Assert.Equal(400f, PanelDodge.Prompt(400f, 56f, 160f, new ScreenBox(253f, 460f, 1037f, 600f), 180f, 642f, 1280f, 28f).Y0, 1);
            Assert.Equal(400f, PanelDodge.Prompt(400f, 56f, 160f, null, 180f, 642f, 1280f, 28f).Y0, 1);
        }

        [Fact]
        public void APanel_IsLaidOut_ByItsAnchorsOffsetsAndTheSizeItsLinesAsk()
        {
            // The Payday card: anchored at the middle, grown both ways to 520 px across at 1280 px.
            var (x0, x1) = PanelDodge.LaidOut(0.5f, 0.5f, 0f, 0f, 1280f, 520f, 0);
            Assert.Equal(380f, x0, 1);
            Assert.Equal(900f, x1, 1);
            // Down from its top at 173 px to 410 px of lines.
            var (y0, y1) = PanelDodge.LaidOut(0f, 0f, 173f, 173f, 720f, 410f, 1);
            Assert.Equal(173f, y0, 1);
            Assert.Equal(583f, y1, 1);
            // Grown up from its foot.
            Assert.Equal((90f, 200f), PanelDodge.LaidOut(0f, 0f, 100f, 200f, 720f, 110f, -1));
            // Already wider than its lines: as its offsets say.
            Assert.Equal((10f, 610f), PanelDodge.LaidOut(0f, 0.5f, 10f, -30f, 1280f, 100f, 0));
        }
    }
}
