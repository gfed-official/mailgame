using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    public class ContentTests
    {
        [Fact]
        public void RealContentLoadsAndValidates()
        {
            var db = TestContent.Db;
            Assert.Equal(6, db.MailKinds.Count);
            Assert.Equal(20, db.MailKind("letter").MaxStack);
            Assert.Equal(8, db.MailKind("letter").BaseValue);
            Assert.False(db.MailKind("cargo").CarryByHand);
            Assert.NotEqual(0u, db.ContentHash);
        }

        [Fact]
        public void ContainerGridsMatchChapter03()
        {
            var db = TestContent.Db;
            Assert.Equal((8, 1), (db.Container("hotbar").Cols, db.Container("hotbar").Rows));
            Assert.Equal((8, 2), (db.Container("player_inventory").Cols, db.Container("player_inventory").Rows));
            Assert.Equal((20, 16), (db.Container("po_intake").Cols, db.Container("po_intake").Rows));
            Assert.Equal((8, 4), (db.Container("chest").Cols, db.Container("chest").Rows));
            Assert.Single(db.Container("hotbar").ReservedCells);
        }

        [Fact]
        public void TestMapHasOneStreetTenHousesAndAPostOffice()
        {
            var map = TestContent.Db.Map("test_street");
            Assert.Single(map.Streets);
            Assert.Equal(10, map.Destinations.Count);
            Assert.NotEqual(0UL, map.MapHash);

            var seen = new HashSet<AddressId>();
            foreach (var d in map.Destinations)
            {
                Assert.Equal("house", d.Type.Id);
                Assert.True(seen.Add(d.Address), $"duplicate address {d.Address}");
                Assert.False(map.IsSolid((int)(d.MailboxX / 2f), (int)(d.MailboxZ / 2f)));
                Assert.Same(d, map.FindDestination(d.Address));
            }
            Assert.Equal("1 Larch Lane", map.Destinations[0].Label);

            var po = map.PostOffice;
            Assert.False(map.IsSolid((int)(po.SpawnPadX / 2f), (int)(po.SpawnPadZ / 2f)));
            Assert.Equal(TileType.Street, map.GetTile(0, 13));
        }

        [Fact]
        public void ContentHashIsStableAcrossLoads()
        {
            string root = DirectoryContentSource.FindContentRoot(System.AppContext.BaseDirectory)!;
            var a = ContentDb.Load(new DirectoryContentSource(root));
            var b = ContentDb.Load(new DirectoryContentSource(root));
            Assert.Equal(a.ContentHash, b.ContentHash);
            Assert.Equal(a.Map("test_street").MapHash, b.Map("test_street").MapHash);
        }

        [Fact]
        public void MissingContentReportsErrorsInsteadOfThrowing()
        {
            ContentDb.TryLoad(new MemoryContentSource(), out var errors);
            Assert.NotEmpty(errors);
        }

        [Theory]
        [InlineData(1, 1, 600)]
        [InlineData(1, 3, 1225)]
        [InlineData(1, 4, 1477)]
        [InlineData(1, 8, 2318)]
        [InlineData(5, 1, 4000)]
        public void QuotaFollowsChapter01Formula(int shift, int players, int expected)
        {
            Assert.Equal(expected, QuotaFormula.Quota(TestContent.Db.Balance, shift, players));
        }
    }
}
