using System.Collections.Generic;
using PerformativeMail.Client;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>World labels thin out with distance, give way to nearer ones and stay off the HUD (nits 14 and 15).</summary>
    public class LabelDeclutterTests
    {
        private static readonly ScreenBox[] NoHud = new ScreenBox[0];

        private static LabelCandidate Label(float x, float y, float distance, int rank = 0, float fade = 1f) =>
            new LabelCandidate { Box = ScreenBox.Around(x, y, 120f, 30f), Distance = distance, Rank = rank, Fade = fade };

        [Fact]
        public void ALabelFadesOutBetweenItsTwoDistances()
        {
            Assert.Equal(1f, LabelDeclutter.DistanceFade(3f, 9f, 15f));
            Assert.Equal(1f, LabelDeclutter.DistanceFade(9f, 9f, 15f));
            Assert.Equal(0.5f, LabelDeclutter.DistanceFade(12f, 9f, 15f), 4);
            Assert.Equal(0f, LabelDeclutter.DistanceFade(15f, 9f, 15f));
            Assert.Equal(0f, LabelDeclutter.DistanceFade(80f, 9f, 15f));
            // No end: a landmark is read from anywhere.
            Assert.Equal(1f, LabelDeclutter.DistanceFade(500f, 0f, 0f));
        }

        [Fact]
        public void LabelsThatPileUp_LeaveOnlyTheNearest()
        {
            // A street seen end on: every mailbox's address falls on the same spot.
            var far = Label(640f, 360f, 40f);
            var near = Label(650f, 365f, 6f);
            var middle = Label(630f, 355f, 20f);
            var aside = Label(200f, 300f, 30f);
            var labels = new List<LabelCandidate> { far, near, middle, aside };

            LabelDeclutter.Resolve(labels, NoHud);

            Assert.True(near.Shown);
            Assert.False(middle.Shown);
            Assert.False(far.Shown);
            Assert.True(aside.Shown);
        }

        [Fact]
        public void AHiddenLabelTakesNoRoom()
        {
            // The middle one gives way to the near one, so the far one (clear of the near one) may stay.
            var near = Label(600f, 360f, 5f);
            var middle = Label(700f, 360f, 10f);
            var far = Label(800f, 360f, 20f);
            var labels = new List<LabelCandidate> { far, middle, near };

            LabelDeclutter.Resolve(labels, NoHud);

            Assert.True(near.Shown);
            Assert.False(middle.Shown);
            Assert.True(far.Shown);
        }

        [Fact]
        public void AFadedOutLabelIsNotShownAndCoversNothing()
        {
            var gone = Label(640f, 360f, 5f, fade: 0f);
            var behind = Label(640f, 360f, 30f);
            var labels = new List<LabelCandidate> { gone, behind };

            LabelDeclutter.Resolve(labels, NoHud);

            Assert.False(gone.Shown);
            Assert.True(behind.Shown);
        }

        [Fact]
        public void ALabelUnderTheHudIsNotShown_WhateverItsRank()
        {
            var hud = new[] { new ScreenBox(420f, 18f, 860f, 92f) };
            var nameTag = Label(600f, 60f, 1.5f, rank: 2);
            var below = Label(600f, 140f, 1.5f, rank: 2);
            var labels = new List<LabelCandidate> { nameTag, below };

            LabelDeclutter.Resolve(labels, hud);

            Assert.False(nameTag.Shown);
            Assert.True(below.Shown);
        }

        // The HUD's top group as it is on a 1280 x 720 screen: the plate, and the hint under it.
        private static readonly ScreenBox[] TopLines = { new ScreenBox(416f, 0f, 864f, 98f), ScreenBox.Around(640f, 117f, 560f, 24f) };

        [Fact]
        public void ANameOnTheHudIsDrawnUnderIt_NotLeftOut()
        {
            // The a5d987e playtest: "Guest" over the top bar, with the guest right before the host.
            var name = Label(520f, 60f, 1.2f, rank: 2);
            name.Slides = true;
            var number = Label(520f, 60f, 20f);
            LabelDeclutter.Resolve(new List<LabelCandidate> { number, name }, TopLines);
            Assert.True(name.Shown);
            Assert.False(number.Shown);
            // Under the plate it would lie on the hint, so it goes under that too.
            Assert.Equal(129f, name.Box.Y0);
            Assert.Equal(159f, name.Box.Y1);
            Assert.Equal((460f, 580f), (name.Box.X0, name.Box.X1));
            Assert.Equal(84f, name.SlideY);
            Assert.DoesNotContain(TopLines, clear => name.Box.Overlaps(clear));

            // Beside the hint, under the plate is far enough.
            var left = Label(300f, 90f, 3f, rank: 2);
            left.Box = ScreenBox.Around(390f, 90f, 120f, 30f);
            left.Slides = true;
            LabelDeclutter.Resolve(new List<LabelCandidate> { left }, new[] { TopLines[0] });
            Assert.True(left.Shown);
            Assert.Equal(98f, left.Box.Y0);
            Assert.Equal(23f, left.SlideY);
        }

        [Fact]
        public void ANameOnTheOpenMapIsNotDrawn_ItCannotSlideFarEnough()
        {
            // The map panel on a 1280 x 720 screen (playtest c59d497: a guest's name showed through it).
            var map = new[] { new ScreenBox(60f, 132f, 1220f, 560f) };
            var name = Label(640f, 300f, 4f, rank: 2);
            name.Box = ScreenBox.Around(640f, 300f, 160f, 40f);
            name.Slides = true;
            var beside = Label(30f, 300f, 4f, rank: 2);
            beside.Box = ScreenBox.Around(30f, 300f, 50f, 20f);
            LabelDeclutter.Resolve(new List<LabelCandidate> { name, beside }, map);
            Assert.False(name.Shown);
            Assert.True(beside.Shown);
        }

        [Fact]
        public void ANameClearOfTheHudStaysWhereItIs_AndOneTooFarUpIsLeftOut()
        {
            var clear = Label(200f, 300f, 6f, rank: 2);
            clear.Slides = true;
            // Its owner stands so close that the name is far above the screen: no name beats one in their face.
            var above = Label(640f, 100f, 0.6f, rank: 2);
            above.Box = ScreenBox.Around(640f, -150f, 120f, 30f);
            above.Slides = true;
            var tall = new[] { new ScreenBox(416f, -400f, 864f, 98f) };
            LabelDeclutter.Resolve(new List<LabelCandidate> { clear, above }, tall);
            Assert.True(clear.Shown);
            Assert.Equal(0f, clear.SlideY);
            Assert.Equal(285f, clear.Box.Y0);
            Assert.False(above.Shown);
            Assert.Equal(0f, above.SlideY);
            Assert.Equal(-165f, above.Box.Y0);
        }

        [Fact]
        public void ANameDrawnLower_TakesItsRoomThere()
        {
            // Two guests side by side before the host: both names would land on the same spot under the hint.
            var near = Label(600f, 50f, 1.0f, rank: 2);
            var far = Label(640f, 60f, 1.4f, rank: 2);
            near.Slides = far.Slides = true;
            var address = Label(610f, 150f, 8f, rank: 1);
            LabelDeclutter.Resolve(new List<LabelCandidate> { address, far, near }, TopLines);
            Assert.True(near.Shown);
            Assert.Equal(129f, near.Box.Y0);
            Assert.False(far.Shown);
            Assert.False(address.Shown);
        }

        [Fact]
        public void ANameOverAPanelGoesUnderItsEdge_OrIsLeftOut_AndIsNeverDrawnOnIt()
        {
            // Issue (d) of the 25fbca5 playtest: "Guest" over a corner of the Draft's cards. The cards on a 1280 x 720 screen.
            var draft = new ScreenBox(210f, 144f, 1070f, 470f);
            var hud = new[] { TopLines[0], TopLines[1], draft };

            // Over the lower corner: the name is drawn right under the panel.
            var corner = Label(250f, 450f, 4f, rank: 2);
            corner.Slides = true;
            // Over the upper corner the way down is too long: no name beats one across the cards.
            var top = Label(1040f, 150f, 3f, rank: 2);
            top.Slides = true;
            // Before the host's face, on the HUD's top lines: under those lie the cards.
            var face = Label(520f, 60f, 1.2f, rank: 2);
            face.Slides = true;
            // An address under the cards does not slide; one beside them stays.
            var address = Label(640f, 300f, 6f, rank: 1);
            var beside = Label(120f, 300f, 6f, rank: 1);
            var labels = new List<LabelCandidate> { corner, top, face, address, beside };

            LabelDeclutter.Resolve(labels, hud);

            Assert.True(corner.Shown);
            Assert.Equal(470f, corner.Box.Y0);
            Assert.Equal(35f, corner.SlideY);
            Assert.False(top.Shown);
            Assert.Equal(0f, top.SlideY);
            Assert.False(face.Shown);
            Assert.Equal(0f, face.SlideY);
            Assert.False(address.Shown);
            Assert.True(beside.Shown);
            foreach (var label in labels)
                if (label.Shown) Assert.DoesNotContain(hud, clear => label.Box.Overlaps(clear));
        }

        [Fact]
        public void AHigherRankWinsOverANearerLabel()
        {
            var number = Label(640f, 360f, 4f, rank: 0);
            var name = Label(640f, 360f, 12f, rank: 2);
            var labels = new List<LabelCandidate> { number, name };

            LabelDeclutter.Resolve(labels, NoHud);

            Assert.True(name.Shown);
            Assert.False(number.Shown);
        }

        [Fact]
        public void BoxesThatOnlyTouchDoNotOverlap()
        {
            var a = new ScreenBox(0f, 0f, 10f, 10f);
            Assert.False(a.Overlaps(new ScreenBox(10f, 0f, 20f, 10f)));
            Assert.False(a.Overlaps(new ScreenBox(0f, 10f, 10f, 20f)));
            Assert.True(a.Overlaps(new ScreenBox(9f, 9f, 20f, 20f)));
        }
    }
}
