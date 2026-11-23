using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Address Sorter and its filters over the loopback transport (doc 04 §2.4, doc 06 §3
    /// ConfigureConstruct, doc 09 §2.7): built on four tiles on every end, its filters put up by the
    /// interact key, stepped by a number key, seen by everybody and by whoever joins late, and the
    /// panel told what each output would take of the Intake's mail.
    /// </summary>
    public class SorterFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content = Shared.Content;

            public Session() => Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false }, Hub);

            public SimWorld Sim => Server.World;

            public ClientRuntime Join(string name)
            {
                var client = new ClientRuntime(Content, Hub.CreateClient(), name, PlaytestFlags.None, 0);
                Clients.Add(client);
                Run(10);
                return client;
            }

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }

            public Player Truth(ClientRuntime client) => Sim.GetPlayer(client.World!.LocalId)!;
        }

        private static (Session, ClientRuntime, ClientRuntime, Construct) Built()
        {
            var s = new Session();
            var host = s.Join("host");
            var guest = s.Join("guest");
            Assert.NotEqual(0u, host.PressGo());
            s.Run(10);
            Assert.Equal(RunPhase.Prep, s.Sim.Run.Phase);
            var truth = s.Truth(host);
            s.Sim.Economy.AddLoot(1000);
            Assert.True(s.Sim.Give(truth.Inventory, ItemStack.OfItem(s.Content.Item("iron_ingot"), 10)));
            Assert.True(s.Sim.Give(truth.Inventory, ItemStack.OfItem(s.Content.Item("stone"), 10)));
            var po = s.Sim.Map.PostOffice;
            truth.Move.X = po.ShopX + 1f;
            truth.Move.Z = po.ShopZ;
            s.Run(10);
            Assert.True(host.OpenShop());
            Assert.NotEqual(0u, host.Buy(s.Content.ShopItem("bp_sorting")));
            s.Run(10);
            host.CloseShop();
            Assert.Contains(s.Content.ShopItem("bp_sorting"), guest.World!.ShopOwned);
            truth.Move.X = 31.2f;
            truth.Move.Z = 44f;
            s.Run(10);
            var sorter = s.Content.Recipes.Single(r => r.Building.Id == "address_sorter_mk1");
            Assert.Equal(OpResult.Ok, host.World!.PlaceState(sorter, 16, 21, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(sorter, 16, 21, 0));
            s.Run(10);
            var built = host.World.Constructs.At(17, 22)!;
            Assert.Equal((16, 21), (built.Tx, built.Tz));
            Assert.Same(guest.World.Constructs.At(16, 21), guest.World.Constructs.At(17, 22));
            return (s, host, guest, built);
        }

        [Fact]
        public void TheInteractKey_PutsTheFiltersUp_ANumberKeyStepsThem_AndEverybodySeesIt()
        {
            var (s, host, guest, built) = Built();
            var interaction = new InteractionController(host);
            var box = AimBox.Built(host.World!.Map, built);
            ViewRay.Aim(host.World.Local, box.X, box.Y, box.Z, out host.Input.YawRadians, out host.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Configure, interaction.Prompt.Kind);
            Assert.Equal("Set the filters of the Address Sorter  [E]", HudText.Prompt(host.World, interaction.Prompt, out _));
            interaction.Update(Dt, true, false);
            interaction.Update(Dt, false, false);
            Assert.Same(built, host.World.FilterTarget);
            Assert.Equal(InteractKind.CloseFilters, interaction.Prompt.Kind);
            s.Run(10);
            Assert.NotNull(host.World.FilterStatus);
            Assert.Equal(s.Sim.GetPlayer(host.World.LocalId)!.ViewingFilters, 21 * s.Sim.Map.Cols + 16 + 1);

            // The panel: the title, the buffer, a card for each way out and the overflow.
            string text = HudText.FilterText(host.World);
            Assert.StartsWith("ADDRESS SORTER   ·   filters\nIn its buffer 0/8   ·   sorted 0   ·   to overflow 0\n[1]  Front\nnothing (no filter)\n", text);
            Assert.Contains("\nOverflow\nwhatever no filter catches\n", text);
            Assert.EndsWith(HudText.FilterHint, text);

            // Key 1 steps the front on to the first street there is; the panel shows it at once.
            var choices = host.World.FilterChoices(built);
            var first = choices[1];
            Assert.Equal(FilterKind.Street, first.Kind);
            Assert.True(host.PressFilterKey(0));
            Assert.Equal(first, host.World.ShownFilter(built, 0, 0));
            Assert.Equal(MailFilter.Empty, built.Filter(0, 0));
            s.Run(10);
            Assert.Equal(first, built.Filter(0, 0));
            Assert.Equal(first, guest.World!.Constructs.At(16, 21)!.Filter(0, 0));
            Assert.Equal(first, s.Sim.Constructs.At(16, 21)!.Filter(0, 0));
            string street = s.Sim.Map.GetStreet(first.Value)!.Name;
            Assert.Equal(new[] { "[1]  Front", street }, HudText.FilterRow(host.World, built, 0).Take(2));
            // A right click (step −1) back to nothing; key 3 is the right output; key 4 is nothing's.
            Assert.NotEqual(0u, host.StepFilter(0, -1));
            Assert.True(host.PressFilterKey(2));
            Assert.True(host.PressFilterKey(3));
            s.Run(10);
            Assert.Equal((MailFilter.Empty, first), (built.Filter(0, 0), built.Filter(2, 0)));
            // The kinds come after the streets and districts.
            Assert.Equal(FilterKind.Kind, choices.Last().Kind);
            Assert.Equal(s.Content.MailKinds.Count, choices.Count(c => c.Kind == FilterKind.Kind));

            // Whoever joins late finds the filters as they stand.
            var late = s.Join("late");
            Assert.Equal(first, late.World!.Constructs.At(17, 21)!.Filter(2, 0));

            // The key takes it down; walking off does too.
            interaction.Update(Dt, true, false);
            interaction.Update(Dt, false, false);
            Assert.Null(host.World.FilterTarget);
            s.Run(5);
            Assert.Equal(0, s.Sim.GetPlayer(host.World.LocalId)!.ViewingFilters);
            Assert.NotEqual(0u, host.OpenFilters(built));
            s.Run(5);
            var truth = s.Truth(host);
            truth.Move.X = 10f;
            truth.Move.Z = 47f;
            s.Run(10);
            Assert.Null(host.World.FilterTarget);
            Assert.Equal(0, truth.ViewingFilters);
        }

        [Fact]
        public void InBuildMode_ASplittersFiltersComeUpByTheInteractKey_ByKindOnly()
        {
            var (s, host, _, _) = Built();
            var truth = s.Truth(host);
            Assert.True(s.Sim.Give(truth.Inventory, ItemStack.OfItem(s.Content.Item("plank"), 4)));
            s.Run(5);
            var splitter = s.Content.Recipes.Single(r => r.Building.Id == "splitter");
            Assert.NotEqual(0u, host.PlaceConstruct(splitter, 14, 22, 0));
            s.Run(10);
            var built = host.World!.Constructs.At(14, 22)!;
            var build = new BuildController(host);
            var interaction = new InteractionController(host) { Building = () => build.Active };
            var box = AimBox.Built(host.World.Map, built);
            ViewRay.Aim(host.World.Local, box.X, box.Y, box.Z, out host.Input.YawRadians, out host.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.PushBelt, interaction.Prompt.Kind);
            Assert.EndsWith("[hold E]   ·   its filters in Build mode", HudText.Prompt(host.World, interaction.Prompt, out _));
            Assert.True(build.Toggle());
            build.Update(Dt, false, false);
            Assert.EndsWith("   ·   [E] filters", HudText.BuildPrompt(host.World, build.Ghost, out _));
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Configure, interaction.Prompt.Kind);
            interaction.Update(Dt, true, false);
            Assert.Same(built, host.World.FilterTarget);
            var choices = host.World.FilterChoices(built);
            Assert.Equal(1 + s.Content.MailKinds.Count, choices.Count);
            Assert.StartsWith("SPLITTER   ·   filters by kind\n[1]  Front\nanything the others do not take\n", HudText.FilterText(host.World));
            Assert.True(host.PressFilterKey(1));
            s.Run(10);
            Assert.Equal(new MailFilter(FilterKind.Kind, s.Content.MailKinds[0].Index), s.Sim.Constructs.At(14, 22)!.Filter(1, 0));
            Assert.Equal(HudText.FilterName(host.World, built, choices[1]), HudText.FilterRow(host.World, built, 1)[1]);
        }
    }
}
