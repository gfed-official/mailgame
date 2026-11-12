using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The Intake manifest keeps its rows where they are while it is open (fa44a16 playtest).</summary>
    public class ManifestListTests
    {
        private static readonly AddressId A2 = new AddressId(1, 1, 2);
        private static readonly AddressId A5 = new AddressId(1, 1, 5);
        private static readonly AddressId A8 = new AddressId(1, 1, 8);

        private readonly GridContainer _intake = new GridContainer(1, Shared.Content.Container("po_intake"));
        private readonly EntryIdAllocator _ids = new EntryIdAllocator();
        private readonly ManifestList _list = new ManifestList();
        private uint _mailId = 1;

        private void Arrive(string kindId, AddressId address, int count = 1)
        {
            var kind = Shared.Content.MailKind(kindId);
            for (int i = 0; i < count; i++)
            {
                var item = new MailItem { Id = _mailId++, Kind = kind.Index, Address = address, Value = (ushort)kind.BaseValue, SpawnShift = 1, DeadlineShift = 1 };
                Assert.True(InventoryOps.AutoPlace(Shared.Content, _intake, ItemStack.OfMail(item), _ids));
            }
        }

        private void Take(int row)
        {
            Assert.True(_intake.Remove(_list.Rows[row].FirstEntry) != null);
        }

        private string[] Addresses() => _list.Rows.Select(r => $"{r.Address.Number}{(r.Empty ? "-" : "")}").ToArray();

        [Fact]
        public void OpensInAddressOrderWithOneRowPerAddressAndKind()
        {
            Arrive("letter", A8, 2);
            Arrive("small", A2);
            Arrive("letter", A2, 3);
            Arrive("letter", A5);
            _list.Update(_intake);

            ushort letter = Shared.Content.MailKind("letter").Index, small = Shared.Content.MailKind("small").Index;
            Assert.Equal(new[] { (2, letter, 3), (2, small, 1), (5, letter, 1), (8, letter, 2) },
                _list.Rows.Select(r => (r.Address.Number, r.Kind, r.Items)).ToArray());
            Assert.Equal(7, _list.TotalItems);
            Assert.All(_list.Rows, r => Assert.NotNull(_intake.Get(r.FirstEntry)));
        }

        [Fact]
        public void MailThatArrivesWhileOpenNeverMovesARow()
        {
            Arrive("letter", A5, 5);
            Arrive("letter", A8);
            _list.Update(_intake);
            Assert.Equal(new[] { "5", "8" }, Addresses());

            // A new address sorts before both rows, but goes to the bottom; more for 5 grows its row in place.
            Arrive("letter", A2);
            Arrive("letter", A5);
            _list.Update(_intake);
            Assert.Equal(new[] { "5", "8", "2" }, Addresses());
            Assert.Equal(6, _list.Rows[0].Items);
        }

        [Fact]
        public void AnEmptiedRowStaysInPlaceUntilTheListIsOpenedAgain()
        {
            Arrive("letter", A2);
            Arrive("letter", A5);
            Arrive("letter", A8);
            _list.Update(_intake);

            Take(0);
            _list.Update(_intake);
            Assert.Equal(new[] { "2-", "5", "8" }, Addresses());
            Assert.Equal(0u, _list.Rows[0].FirstEntry);
            Assert.Equal(2, _list.TotalItems);

            // The same address comes back into its old row; an empty row at the end is dropped.
            Arrive("letter", A2, 2);
            Take(2);
            _list.Update(_intake);
            Assert.Equal(new[] { "2", "5" }, Addresses());
            Assert.Equal(2, _list.Rows[0].Items);

            Take(0);
            Arrive("letter", A8);
            _list.Update(_intake);
            Assert.Equal(new[] { "2-", "5", "8" }, Addresses());

            _list.Reset();
            _list.Update(_intake);
            Assert.Equal(new[] { "5", "8" }, Addresses());
        }

        [Fact]
        public void ManifestRowText()
        {
            var hub = new PerformativeMail.Server.Transport.LoopbackHub();
            var server = new PerformativeMail.Server.ServerRuntime(Shared.Content, new PerformativeMail.Sim.SimSettings { Seed = 3, QuickStart = true }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            for (int i = 0; i < 10; i++) { client.Tick(); server.Tick(); }
            var world = client.World!;
            var dest = world.Map.Destinations[0];
            ushort letter = Shared.Content.MailKind("letter").Index;

            Assert.Equal($"{dest.Label}  —  3 Letters", HudText.ManifestRow(world, new ManifestRow { Address = dest.Address, Kind = letter, FirstEntry = 4, Stacks = 1, Items = 3 }));
            Assert.Equal($"{dest.Label}  —  30 Letters  (2 stacks)", HudText.ManifestRow(world, new ManifestRow { Address = dest.Address, Kind = letter, FirstEntry = 4, Stacks = 2, Items = 30 }));
            Assert.Equal($"{dest.Label}  —  no Letters left", HudText.ManifestRow(world, new ManifestRow { Address = dest.Address, Kind = letter }));
        }
    }
}
