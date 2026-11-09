using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Ops;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Inventory;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// The <see cref="PlayerOpKinds.HotbarSelect"/> op (Mail Lead planning default, regroup 2026-11-08: "a 4 B <c>HotbarSelect</c> op in the
/// Players block keeps the ADR-0015 check 2 text intact") on a composed M-1a world, driven through the drain, and read by
/// <see cref="ThrowValidator"/> check 2 through the production <see cref="PlayersHotbarSelection"/>. As in <c>ThrowValidationTests</c>, the
/// hotbar entry lives in an Inventory rig (<see cref="InvAutoPlaceRig"/>) that shares slot 0 with the composed world.
/// </summary>
public sealed class HotbarSelectTests
{
    private const byte EntryIndex = 3;

    [Fact]
    public void Select_TheEntrysSlot_TurnsValidatorCheck2FromStaleToOk()
    {
        // Arrange: a 5-unit letter at hotbar index 3; the player has selected nothing yet (index 0).
        var rig = new SelectRig();
        var inv = new InvAutoPlaceRig();
        EntryId entry = inv.Entries[inv.PlaceMail(inv.HotbarA, "letter", EntryIndex, 0, 4u, new uint[] { 11, 12, 13, 14, 15 })].Id;
        ThrowCheck before = rig.ValidateThrow(inv, entry, 5);

        // Act
        Reply reply = rig.Select(rig.Actor, EntryIndex);

        // Assert
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal((ushort)ThrowReason.StaleEntry, before.Reason);
        Assert.True(rig.ValidateThrow(inv, entry, 5).IsOk);
    }

    [Theory]
    [InlineData(7u, false, (ushort)0)]                          // the last slot
    [InlineData(8u, false, CoreReason.BadRequest)]              // one past the hotbar
    [InlineData(uint.MaxValue, false, CoreReason.BadRequest)]
    [InlineData(2u, true, CoreReason.BadRequest)]               // the System actor has no slot
    public void Select_IndexAndActor_StoreTheIndexOrAreBadRequestAndChangeNothing(uint index, bool systemActor, ushort expected)
    {
        // Arrange
        var rig = new SelectRig();
        Assert.Equal(ReplyResult.Ok, rig.Select(rig.Actor, 5).Result);
        byte[] players = rig.PlayersSection();

        // Act
        Reply reply = rig.Select(systemActor ? ActorId.System : rig.Actor, index);

        // Assert
        Assert.Equal(expected, reply.Reason);
        Assert.Equal(expected == 0 ? (byte)index : (byte)5, rig.Players.SelectedHotbarIndex(rig.Slot));
        Assert.Equal(expected != 0, players.AsSpan().SequenceEqual(rig.PlayersSection()));
    }

    [Fact]
    public void LeaveAndRejoin_StartsAgainOnTheHandsSlot()
    {
        // Arrange
        var rig = new SelectRig();
        Assert.Equal(ReplyResult.Ok, rig.Select(rig.Actor, 6).Result);

        // Act
        rig.Leave();
        rig.Join();

        // Assert
        Assert.Equal(0, rig.Players.SelectedHotbarIndex(rig.Slot));
    }

    private sealed class SelectRig
    {
        private readonly CaptureSink _sink = new();
        private uint _req;

        public SelectRig()
        {
            var args = new SimBuildArgs(SimProfile.M1aGreybox, BuiltInContent.CreateM1a(), 7) { World = WorldTablesFixture.Pad(), EventSink = _sink };
            Host = SimComposition.Build(in args);
            Players = Host.Systems.OfType<PlayersSystem>().Single();
            Throwing = Host.Systems.OfType<ThrowingSystem>().Single();
            Host.World.Tick();
            Join();
        }

        public SimHost Host { get; }

        public PlayersSystem Players { get; }

        public ThrowingSystem Throwing { get; }

        public PlayerSlot Slot => PlayerSlot.FromValue(0);

        public ActorId Actor => ActorId.FromEntity(Host.Services.Slots.Entry(Slot).PlayerId);

        public void Join()
        {
            var op = new Op { Kind = OpKind.PlayerJoin, ReqId = ++_req };
            op.SetPayload(new PlayerJoinPayload { Slot = 0 });
            Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
            Host.World.Tick();
        }

        public void Leave()
        {
            var op = new Op { Kind = OpKind.PlayerLeave, ReqId = ++_req };
            op.SetPayload(new PlayerLeavePayload { PlayerId = Host.Services.Slots.Entry(Slot).PlayerId.Value });
            Assert.True(Host.World.EnqueueOp(ActorId.System, in op));
            Host.World.Tick();
        }

        /// <summary>Enqueues one HotbarSelect from <paramref name="actor"/>, ticks, and returns its one reply.</summary>
        public Reply Select(ActorId actor, uint index)
        {
            var op = new Op { Kind = PlayerOpKinds.HotbarSelect, ReqId = ++_req };
            op.SetPayload(new HotbarSelectPayload { Index = index });
            int from = _sink.Ticks.Count;
            Assert.True(Host.World.EnqueueOp(actor, in op));
            Host.World.Tick();
            return Assert.Single(
                _sink.Ticks.Skip(from).SelectMany(t => t).Where(e => e.Kind == EventKind.OpReply).Select(e => e.PayloadAs<Reply>()),
                r => r.ReqId == op.ReqId);
        }

        /// <summary>The Players section's payload in a whole-world dump (header, then id u16 / length u32 / payload per section).</summary>
        public byte[] PlayersSection()
        {
            byte[] dump = DumpHash.Bytes(Host.World);
            for (int pos = 6; pos < dump.Length; pos += 6 + (int)BitConverter.ToUInt32(dump, pos + 2))
            {
                if (BitConverter.ToUInt16(dump, pos) == SectionIds.Players)
                {
                    return dump.AsSpan(pos + 6, (int)BitConverter.ToUInt32(dump, pos + 2)).ToArray();
                }
            }

            Assert.Fail("No Players section.");
            return Array.Empty<byte>();
        }

        /// <summary>A valid release of <paramref name="units"/> of <paramref name="entry"/> (FX-T direction) through the production facts.</summary>
        public ThrowCheck ValidateThrow(InvAutoPlaceRig inv, EntryId entry, byte units)
        {
            var feet = Players.HostPositions[Slot].Feet;
            var p = new ThrowReleasePayload
            {
                EntryId = entry.Value, Units = units, Side = 1, FeetX = feet.X, FeetY = feet.Y, FeetZ = feet.Z, DirX = 0, DirY = 3406, DirZ = -16_026,
            };
            var facts = new ThrowHostFacts(
                inv.Inventory, Players, Host.Services.Slots, new PlayersHotbarSelection(Players), new PlayersClaimCorroborator(Players),
                Players.Settings.ClaimToleranceMm, Throwing.Flights, Throwing.TatClocks, Host.World.Counters);
            return ThrowValidator.Validate(in p, Actor, Host.World.CurrentTick, in facts, out _);
        }
    }
}
