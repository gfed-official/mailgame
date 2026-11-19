namespace PerformativeMail.Client
{
    /// <summary>
    /// Whose the mouse is (issue (c) of the 25fbca5 playtest): the view's while nothing is open,
    /// the pointer's while a panel is, and the view's again the moment the panel closes, whatever
    /// was pressed in between. Esc with nothing open lets the mouse go until a click or Esc takes
    /// it back.
    /// </summary>
    public sealed class MouseCapture
    {
        /// <summary>Let go with Esc while nothing was open; a solo host's run is paused for as long.</summary>
        public bool Released { get; private set; }
        /// <summary>A panel with something to click is open: the inventory, a chest, the shop's cards, the Draft's.</summary>
        public bool PanelOpen { get; private set; }
        /// <summary>The mouse turns the view.</summary>
        public bool Wanted => !Released && !PanelOpen;

        public void SetPanelOpen(bool open)
        {
            if (PanelOpen == open) return;
            PanelOpen = open;
            // Whoever closes a panel is back in the world, also if they had let the mouse go before they opened it.
            if (!open) Released = false;
        }

        /// <summary>Esc with nothing open: lets the mouse go, or takes it back.</summary>
        public void Toggle()
        {
            if (!PanelOpen) Released = !Released;
        }

        /// <summary>A click into the view. True when it was spent on taking the mouse back, and so is not a swing of the hammer.</summary>
        public bool Click()
        {
            if (PanelOpen || !Released) return false;
            Released = false;
            return true;
        }
    }
}
