using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The practice run (doc 09 §6: a fixed seed, two shifts, no raid, no XP) and the Daily Seed (doc 01 §5.3: the UTC
    /// date hashed, a fixed stamp set), both chosen by the host in the Lobby (design choice 165).
    /// </summary>
    public class RunModeTests
    {
        private const int Tps = 30;
        private static ContentDb Db => TestContent.Db;
        private static readonly DateTime Today = new DateTime(2026, 11, 27, 23, 30, 0, DateTimeKind.Utc);
        private uint _nextRequest = 1;
        private uint _nextMailId = 960000;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static SimWorld NewWorld(DateTime? now = null) =>
            new SimWorld(Db, new SimSettings { Seed = 7, Raids = true, Draft = false, RunEvents = true, UtcNow = () => now ?? Today });

        private static byte Mask(params string[] ids) => (byte)ids.Aggregate(0, (m, id) => m | 1 << Db.Stamp(id).Index);

        private static void RunUntil(SimWorld world, Func<bool> done, int maxSeconds, Action? eachTick = null)
        {
            for (int i = 0; i < maxSeconds * Tps && !done(); i++)
            {
                world.Step();
                eachTick?.Invoke();
            }
            Assert.True(done(), $"still in {world.Run.Phase} of shift {world.Run.Shift} after {maxSeconds} s");
        }

        /// <summary>Plays the shift at hand to its quota and through its Payday.</summary>
        private void PlayShift(SimWorld world, Player carrier, Action eachTick)
        {
            if (world.Run.Phase == RunPhase.Prep) Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(carrier.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(carrier, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            var entry = carrier.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.Deliver(0, carrier.Hotbar.Id, entry.EntryId, home.Id)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 400, eachTick);
            RunUntil(world, () => world.Run.Phase != RunPhase.Payday, 30, eachTick);
        }

        [Fact]
        public void Practice_IsTheTestStreetFromItsSeed_TwoShifts_NoRaid_NoXp()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetStamps(Mask("cursed_mail", "rush_hour"));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetWorld(0, 1, 99, RunMode.Practice)));
            Assert.Equal((RunMode.Practice, "test_street", (uint)Db.Balance.GetInt("practiceSeed"), (byte)0),
                (world.Run.Mode, world.Map.Id, world.Settings.Seed, world.Stamps));
            Assert.Equal(RunMode.Practice, world.GetGlobalState().Mode);
            Assert.Equal(2, world.ShiftCount);
            // No stamps go on a practice run.
            Assert.Equal(OpResult.NotAllowed, Request(world, host, SimRequest.SetStamps(0, Mask("rush_hour"))));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            bool raid = false;
            void Watch() => raid |= world.Raid != RaidState.None || world.LiveEvents != 0;
            PlayShift(world, host, Watch);
            Assert.Equal((RunPhase.Prep, 2), (world.Run.Phase, world.Run.Shift));
            PlayShift(world, host, Watch);
            Assert.False(raid, "a raid or a run event came in practice");
            Assert.Equal((RunPhase.Results, RunOutcome.Victory), (world.Run.Phase, world.Run.Outcome));
            Assert.Equal(0, world.Run.Results!.Players.Single().Xp);
            // Back in the Lobby the next run is a practice run again, until the host chooses otherwise.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunMode.Practice, world.Run.Mode);
        }

        [Fact]
        public void ANormalRun_HasTheBalancesFiveShifts()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            PlayShift(world, host, () => { });
            Assert.Equal(5, world.ShiftCount);
            Assert.Equal((RunPhase.Prep, 2), (world.Run.Phase, world.Run.Shift));
        }

        [Fact]
        public void AnotherWorldOrSeed_LeavesPractice_AndOnlyTheHostChooses()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            Assert.Equal(OpResult.NotHost, Request(world, guest, SimRequest.SetWorld(0, 0, 0, RunMode.Practice)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetWorld(0, 0, 0, RunMode.Practice)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetWorld(0, 0, 1234)));
            Assert.Equal((RunMode.Normal, 1234u), (world.Run.Mode, world.Settings.Seed));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, Mask("rush_hour"))));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.SetWorld(0, 0, 0, (RunMode)9)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.SetWorld(0, 0, 0, RunMode.Practice)));
        }

        [Fact]
        public void TheDailySeed_IsTheUtcDatesHash_OnTheSmallIsland_WithTheDailyStamps()
        {
            var a = NewWorld();
            var ah = a.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(a, ah, SimRequest.SetWorld(0, 0, 55, RunMode.Daily)));
            ushort day = RunModes.Day(Today);
            Assert.Equal("2026-11-27", RunModes.DayText(day));
            Assert.Equal((RunMode.Daily, WorldGenerator.SmallIsland, RunModes.DailySeed(day), Mask("rush_hour", "heavy_load"), day),
                (a.Run.Mode, a.Map.Id, a.Settings.Seed, a.Stamps, a.Run.DailyDay));
            Assert.Equal(day, a.GetGlobalState().DailyDay);
            // The stamps are the day's whatever the host's rank, and stay so.
            Assert.Equal(OpResult.NotAllowed, Request(a, ah, SimRequest.SetStamps(0, 0)));

            // Somebody else on the same UTC day (later, elsewhere) gets the same; the next day another.
            var b = NewWorld(Today.AddMinutes(-60 * 23));
            var bh = b.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(b, bh, SimRequest.SetWorld(0, 0, 0, RunMode.Daily)));
            Assert.Equal(a.Settings.Seed, b.Settings.Seed);
            Assert.Equal(a.Map.MapHash, b.Map.MapHash);
            Assert.NotEqual(RunModes.DailySeed(day), RunModes.DailySeed((ushort)(day + 1)));
            Assert.Equal(5, a.ShiftCount);

            // A new seed is a run of the host's own again: the day's stamps come off.
            Assert.Equal(OpResult.Ok, Request(a, ah, SimRequest.SetWorld(0, 1, 77)));
            Assert.Equal((RunMode.Normal, (byte)0, (ushort)0), (a.Run.Mode, a.Stamps, a.Run.DailyDay));
        }

        [Fact]
        public void EveryStampHasItsOwnCode_AndTheDailyStampsAreTierOne()
        {
            Assert.Equal(new[] { "CM", "RH", "HL", "DR", "NR", "PA", "MM", "SC" }, Db.Stamps.Select(s => s.Code));
            Assert.All(Db.Stamps.Where(s => s.Daily), s => Assert.Equal(1, s.Tier));
            Assert.Equal(Mask("rush_hour", "heavy_load"), RunModes.DailyStamps(Db));
        }

        [Theory]
        [InlineData("test_street", 1u, 0, "PM1-TEST-00000001")]
        [InlineData("small_island", 0x7F3A9C21u, 0b1001, "PM1-SMALL-7F3A9C21-CM.DR")]
        [InlineData("small_island", 0xABCu, 0b0110, "PM1-SMALL-00000ABC-RH.HL")]
        public void TheSeedString_IsWrittenAsDoc01Says_AndReadsBack(string mapId, uint seed, int stamps, string text)
        {
            Assert.Equal(text, SeedStrings.Format(Db, mapId, seed, stamps));
            Assert.True(SeedStrings.TryParse(Db, "  " + text.ToLowerInvariant() + "\n", out int choice, out uint back, out byte mask, out string error), error);
            Assert.Equal((mapId, seed, (byte)stamps), (SimWorld.WorldChoices[choice], back, mask));
        }

        [Theory]
        [InlineData("", "not a seed string")]
        [InlineData("hello", "not a seed string")]
        [InlineData("PM2-TEST-00000001", "not a seed string")]
        [InlineData("PM1-LARGE-00000001", "no world 'LARGE'")]
        [InlineData("PM1-SMALL-XYZ", "'XYZ' is no seed")]
        [InlineData("PM1-SMALL-123456789", "'123456789' is no seed")]
        [InlineData("PM1-SMALL-00000000", "'00000000' is no seed")]
        [InlineData("PM1-SMALL-0000002A-CM.ZZ", "no stamp 'ZZ'")]
        [InlineData("PM1-SMALL-0000002A-CM.CM", "no stamp 'CM'")]
        [InlineData("PM1-SMALL-0000002A-CM-DR", "not a seed string")]
        public void ASeedStringThatIsNone_SaysWhatIsWrong(string text, string want)
        {
            Assert.False(SeedStrings.TryParse(Db, text, out _, out _, out _, out string error));
            Assert.StartsWith(want, error);
        }
    }
}
