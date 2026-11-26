using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Doc 05 §5 "Damage numbers and a health bar appear over constructs when damaged; the HUD shows a
    /// compass marker for any construct under attack" (design choice 130): each blow the server deals
    /// a construct reaches the client with what it took; it counts as hit for 8 s and as under attack
    /// for 3 s; the marker's line names it; its place on screen is over it or on the edge its way.
    /// </summary>
    public class ConstructHitFlowTests
    {
        private const int Tx = 22, Tz = 15;

        [Fact]
        public void BlowsOnAWall_ReachTheClient_WithWhatTheyTook_AndAgeOut()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 2);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            var sim = server.World;
            var truth = sim.GetPlayer(ada.World!.LocalId)!;
            var wallDef = content.Building("wall_stone");
            Assert.True(sim.Grant(content.ShopItem("bp_defense"), truth.Id));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("stone"), 8)));
            truth.Move.X = BuildRules.TileCentre(Tx) + 2f;
            truth.Move.Z = BuildRules.TileCentre(Tz);
            Run(20);
            Assert.NotEqual(0u, ada.PlaceConstruct(wallDef.Recipe, Tx, Tz, 1));
            Run(10);
            var a = ada.World;
            var wall = a.Constructs.At(Tx, Tz)!;
            Assert.Empty(a.ConstructHits);
            Assert.Null(a.UnderAttack(a.Local.X, a.Local.Z));

            sim.DamageConstruct(sim.Constructs.At(Tx, Tz)!, 75f);
            Run(3);
            var hit = Assert.Single(a.ConstructHits);
            Assert.Equal((Tx, Tz, 75, false, wallDef), (hit.Tx, hit.Tz, hit.Amount, hit.Broke, hit.Def));
            Assert.True(a.HitWithin(wall, ClientWorld.HitShownSeconds));
            Assert.Same(hit, a.UnderAttack(a.Local.X, a.Local.Z));
            Assert.Equal("Stone Wall under attack", HudText.UnderAttack(hit));

            // Mended with the hammer is no blow.
            var mended = sim.Constructs.At(Tx, Tz)!;
            sim.Constructs.SetHp(mended, mended.Hp + 20);
            sim.Events.Add(new ConstructDamagedEvent { X = Tx, Y = Tz, Hp = (ushort)mended.Hp });
            Run(3);
            Assert.Single(a.ConstructHits);

            // Under attack for 3 s; its bar for 8.
            Run(4 * 30);
            Assert.Null(a.UnderAttack(a.Local.X, a.Local.Z));
            Assert.True(a.HitWithin(wall, ClientWorld.HitShownSeconds));
            Run(5 * 30);
            a.PruneConstructHits();
            Assert.Empty(a.ConstructHits);
            Assert.False(a.HitWithin(wall, ClientWorld.HitShownSeconds));

            // Broken: the last blow is what it had left.
            int left = sim.Constructs.At(Tx, Tz)!.Hp;
            sim.DamageConstruct(sim.Constructs.At(Tx, Tz)!, 5000f);
            Run(3);
            var last = Assert.Single(a.ConstructHits);
            Assert.Equal((left, true), (last.Amount, last.Broke));
            Assert.Equal("Stone Wall broken", HudText.UnderAttack(last));
        }

        [Fact]
        public void TheNearestOfWhatIsUnderAttack_IsTheMarkersOne()
        {
            var world = new ClientWorld(Shared.Content, Shared.Content.Maps.Values.First());
            var def = Shared.Content.Building("wall_wood");
            var near = new Construct(def, 10, 10, 0, 100, 0);
            var far = new Construct(def, 30, 10, 0, 100, 0);
            world.NoteConstructHit(far, 20, false);
            world.NoteConstructHit(near, 20, false);
            world.NoteConstructHit(near, 0, false);
            Assert.Equal(2, world.ConstructHits.Count);
            Assert.Same(world.ConstructHits[1], world.UnderAttack(near.CentreX + 1f, near.CentreZ));
            Assert.Same(world.ConstructHits[0], world.UnderAttack(far.CentreX, far.CentreZ));
        }

        [Theory]
        // In view: over it, pointing down at it.
        [InlineData(640f, 300f, false, true, 640f, 300f)]
        // Off the right: on the right edge, at its height's share of the way.
        [InlineData(2000f, 360f, false, false, 1232f, 360f)]
        // Behind, a little left of straight back: it is to the right, along the bottom.
        [InlineData(600f, 360f, true, false, 1232f, 360f)]
        // Straight behind: at the bottom's middle.
        [InlineData(640f, 360f, true, false, 640f, 672f)]
        public void TheMarkerStandsOverItInView_ElseOnTheEdgeItsWay(float px, float py, bool behind, bool over, float x, float y)
        {
            bool placed = EdgeMarker.Place(px, py, behind, 1280f, 720f, 48f, out float mx, out float my, out float angle);
            Assert.Equal(over, placed);
            Assert.Equal(x, mx, 1);
            Assert.Equal(y, my, 1);
            Assert.True(angle >= -MathF.PI && angle <= MathF.PI);
        }

        [Fact]
        public void OnTheTopEdge_TheMarkerAndItsWords_KeepOffTheShiftsPlate()
        {
            // The f32ea2e playtest: a turret straight ahead, high over the view, put "Turret under attack" over "DELIVERY".
            var keep = new List<ScreenBox> { new ScreenBox(416f, 0f, 864f, 100f) };
            bool over = EdgeMarker.Place(640f, -400f, false, 1280f, 720f, 48f, out float x, out float y, out float angle);
            Assert.False(over);
            Assert.Equal(48f, y, 1);
            // Pointing up, the chevron hangs under its tip: the tip goes just under the plate.
            float tip = EdgeMarker.TipClear(x, y, angle, keep);
            Assert.Equal(106f, tip, 1);
            // Words beside a tip still in the plate's row go under it; words clear of it stay.
            Assert.Equal(106f, EdgeMarker.WordsClear(560f, 54f, 200f, 24f, keep, 720f), 1);
            Assert.Equal(54f, EdgeMarker.WordsClear(900f, 54f, 200f, 24f, keep, 720f), 1);
            // A tip just under the plate pointing down, its chevron reaching up onto it, goes down too.
            Assert.Equal(126f, EdgeMarker.TipClear(640f, 110f, MathF.PI / 2f, keep), 1);
            // Off the plate's span the edge is free.
            Assert.Equal(48f, EdgeMarker.TipClear(1000f, 48f, angle, keep), 1);
        }

        [Fact]
        public void AChevronOverTheProtectedBanner_GoesUnderIt_NotOnItsWords()
        {
            // The e36633c playtest: the plate, the playtest hint under it and "PROTECTED in the yard · until …" at a
            // quarter of the way down. The Post Office up the screen put the tip under the hint, just over the
            // banner, and the upward chevron's body on "until".
            var keep = new List<ScreenBox>
            {
                new ScreenBox(416f, 0f, 864f, 100f),
                new ScreenBox(360f, 102f, 920f, 130f),
                new ScreenBox(170f, 160f, 1110f, 200f),
            };
            float angle = -MathF.PI / 2f + 0.1f;
            float tip = EdgeMarker.TipClear(700f, 48f, angle, keep);
            EdgeMarker.Extent(angle, out float x0, out float y0, out float x1, out float y1);
            var chevron = new ScreenBox(700f + x0, tip + y0, 700f + x1, tip + y1);
            foreach (var b in keep) Assert.False(chevron.Overlaps(b));
            Assert.True(tip + y1 <= 160f || tip + y0 >= 200f);
            // A chevron just beside a box's end is held off it too, not only a tip inside it.
            float beside = EdgeMarker.TipClear(1105f, 170f, MathF.PI, keep);
            EdgeMarker.Extent(MathF.PI, out x0, out y0, out x1, out y1);
            Assert.False(new ScreenBox(1105f + x0, beside + y0, 1105f + x1, beside + y1).Overlaps(keep[2]));
        }
    }
}
