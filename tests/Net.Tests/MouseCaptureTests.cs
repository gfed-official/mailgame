using PerformativeMail.Client;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>Whose the mouse is (issue (c) of the 25fbca5 playtest): the view's again whenever a panel closes.</summary>
    public class MouseCaptureTests
    {
        [Fact]
        public void TheMouseIsTheViewsUntilAPanelOpens_AndAgainWhenItCloses()
        {
            var mouse = new MouseCapture();
            Assert.True(mouse.Wanted);

            // The shop's cards go up, or the Draft's.
            mouse.SetPanelOpen(true);
            Assert.False(mouse.Wanted);
            // A click on a card is the panel's, not one that takes the mouse back.
            Assert.False(mouse.Click());
            Assert.False(mouse.Wanted);

            mouse.SetPanelOpen(false);
            Assert.True(mouse.Wanted);
            Assert.False(mouse.Released);
            // In the world a click is a swing.
            Assert.False(mouse.Click());
        }

        [Fact]
        public void EscWithNothingOpenLetsTheMouseGo_UntilAClickOrEscTakesItBack()
        {
            var mouse = new MouseCapture();
            mouse.Toggle();
            Assert.True(mouse.Released);
            Assert.False(mouse.Wanted);
            // The click that takes it back is spent on that.
            Assert.True(mouse.Click());
            Assert.True(mouse.Wanted);
            Assert.False(mouse.Click());

            mouse.Toggle();
            Assert.False(mouse.Wanted);
            mouse.Toggle();
            Assert.True(mouse.Wanted);
        }

        [Fact]
        public void ClosingAPanelGivesTheViewBack_AlsoToWhoHadLetTheMouseGo()
        {
            // The playtest's case: Esc pressed once too often, then the Draft's cards come up and a key takes one.
            var mouse = new MouseCapture();
            mouse.Toggle();
            mouse.SetPanelOpen(true);
            Assert.True(mouse.Released);
            Assert.False(mouse.Wanted);
            mouse.SetPanelOpen(false);
            Assert.False(mouse.Released);
            Assert.True(mouse.Wanted);
        }

        [Fact]
        public void EscUnderAPanelIsThePanels_AndLetsNothingGo()
        {
            var mouse = new MouseCapture();
            mouse.SetPanelOpen(true);
            // Esc closes the panel (the screen does that); it is no second meaning of the key.
            mouse.Toggle();
            Assert.False(mouse.Released);
            mouse.SetPanelOpen(false);
            Assert.True(mouse.Wanted);

            // Told twice that nothing is open, nothing changes: a mouse let go stays let go.
            mouse.Toggle();
            mouse.SetPanelOpen(false);
            Assert.True(mouse.Released);
        }
    }
}
