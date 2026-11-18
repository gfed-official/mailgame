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
