using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The 24cbd0a playtest's N18: the tag of a mail stack in a grid tells the PO boxes and the flats apart.</summary>
    public class MailTagTests
    {
        [Fact]
        public void AHouseIsItsNumber_AFlatItsNumberAndUnit_APoBoxItsBox()
        {
            var map = WorldGenerator.Generate(Shared.Content, 1);
            var house = map.Destinations.First(d => !d.IsPoBoxBank && !d.IsApartment && !d.IsDock);
            Assert.Equal("#" + house.Address.Number, HudText.MailTag(house.Address, house));
            var bank = map.Destinations.Single(d => d.IsPoBoxBank);
            var a = bank.Address;
            Assert.Equal("Box 13", HudText.MailTag(new AddressId(a.District, a.Street, a.Number, 13), bank));
            Assert.NotEqual(HudText.MailTag(new AddressId(a.District, a.Street, a.Number, 2), bank), HudText.MailTag(new AddressId(a.District, a.Street, a.Number, 3), bank));
            Assert.Equal("#12-3", HudText.MailTag(new AddressId(4, 2, 12, 3), null));
        }
    }
}
