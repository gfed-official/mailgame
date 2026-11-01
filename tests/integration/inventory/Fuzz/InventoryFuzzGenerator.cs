using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Integration.Inventory.Fuzz;

/// <summary>
/// The H-R19-1 generator (story grid-inventory/018), shared by H-DET-1: a PCG32 stream (<see cref="Pcg32"/>, the Sim's own generator)
/// that writes the M-1a op set for one player slot at a time, reading the world only (between ticks: <see cref="Snapshot"/>) to aim
/// most ops at live entries, free cells and stacks of the same key. Real inventory ops (<c>Pick</c>, <c>Move</c> whole / split /
/// swap, <c>Merge</c>, <c>QuickMove</c>, <c>Return</c>, malformed ones) go to the op FIFO from the player's actor; the automation set
/// goes as <see cref="FuzzAutomationSystem"/> commands. Every op's type is in the low 5 bits of its <see cref="Op.ReqId"/>. More than
/// half of the ops name the shared chest. Deterministic for a seed and a world state; not thread-safe.
/// </summary>
internal sealed class InventoryFuzzGenerator
{
    /// <summary>Mask of the <see cref="FuzzOpType"/> in an op's <see cref="Op.ReqId"/>.</summary>
    public const uint TypeMask = 0x1F;

    /// <summary>Mail units pre-populated (60 addresses, 3 kinds, 10 % Cursed).</summary>
    public const int PrepopulatedMail = 400;

    /// <summary>Addresses the mail is drawn from.</summary>
    public const int Addresses = 60;

    /// <summary>Live mail units above which a drawn <c>Spawn</c> becomes a <c>Consume</c> (keeps the run near the fixture's 400 units, the scale the arena's 50 % peak is asserted on).</summary>
    // Mail Dev default, approved by Evan 2026-11-01 1:48 PM PT by card click (TD-206 item 4): a drawn spawn at 600 or more live mail units becomes a consume, keeping the run near the fixture's 400 units.
    public const int MailCeiling = 600;

    // Draw weights (percent) of the fuzz op types; the rest of the 100 is malformed ops.
    private static readonly (FuzzOpType Type, uint Weight)[] _weights =
    [
        (FuzzOpType.Pick, 13), (FuzzOpType.MoveWhole, 20), (FuzzOpType.MoveSplit, 7), (FuzzOpType.MoveSwap, 4), (FuzzOpType.Merge, 7),
        (FuzzOpType.QuickMove, 10), (FuzzOpType.Return, 5), (FuzzOpType.Open, 5), (FuzzOpType.Close, 1), (FuzzOpType.Spawn, 5),
        (FuzzOpType.Detach, 4), (FuzzOpType.WorldItemCreate, 3), (FuzzOpType.Catch, 2), (FuzzOpType.Pickup, 5), (FuzzOpType.Consume, 4),
    ];

    private readonly InventoryFuzzWorld _w;
    private readonly List<ushort>[] _byContainer;
    private readonly List<uint> _worldItems = new(1024);
    private readonly List<uint> _goneWorldItems = new(64);
    private readonly DefIndex[] _kindOf = new DefIndex[Addresses];
    private readonly ActorId[] _actors = new ActorId[PlayerSlot.Count];
    private Pcg32 _rng;
    private uint _seq;
    private uint _nextMailId = 1;
    private int _goneNext;

    public InventoryFuzzGenerator(ulong seed, InventoryFuzzWorld world)
    {
        _w = world;
        _rng = new Pcg32(seed, 0x18UL);
        _byContainer = new List<ushort>[world.Inventory.Containers.Capacity];
        for (int i = 0; i < _byContainer.Length; i++)
        {
            _byContainer[i] = new List<ushort>(64);
        }

        ContentSet content = world.Host.Content;
        DefIndex postcard = InventoryFuzzWorld.DefOf(content.MailKinds, "postcard");
        DefIndex letter = InventoryFuzzWorld.DefOf(content.MailKinds, "letter");
        DefIndex small = InventoryFuzzWorld.DefOf(content.MailKinds, "small");
        for (int a = 0; a < Addresses; a++)
        {
            _kindOf[a] = a >= 50 ? small : a % 2 == 0 ? postcard : letter;
        }
    }

    /// <summary>Fuzz ops generated (setup ops excluded).</summary>
    public long Generated { get; private set; }

    /// <summary>Fuzz ops whose payload names the shared chest as a container (malformed ops never count).</summary>
    public long SharedTargeted { get; private set; }

    /// <summary>The 8 <c>PlayerJoin</c> ops (System actor), slot order (<c>ContainerCatalogueTests.JoinOp</c>'s shape).</summary>
    public List<Op> JoinOps()
    {
        var ops = new List<Op>();
        for (byte slot = 0; slot < PlayerSlot.Count; slot++)
        {
            var op = new Op { Kind = OpKind.PlayerJoin, Actor = ActorId.System, ReqId = Req(FuzzOpType.Setup) };
            op.SetPayload(new PlayerJoinPayload { Slot = slot });
            ops.Add(op);
        }

        return ops;
    }

    /// <summary>
    /// The pre-population commands, after the joins: one <c>Spawn</c> per address (its units share one StackKey; addresses 0-14 into
    /// the shared chest, 15-24 the other chest, 25-39 the panels' <c>player_inventory</c>, 40-59 the Intake), 400 units over 60
    /// addresses, kinds postcard / letter / small, every address with <c>a % 10 == 9</c> Cursed (40 units); then one <c>TryCatch</c>
    /// per arranged item entry, round-robin over the slots.
    /// </summary>
    public List<Op> PrepopulationOps()
    {
        CacheActors();
        var ops = new List<Op>();
        for (int a = 0; a < Addresses; a++)
        {
            int units = (PrepopulatedMail / Addresses) + (a < PrepopulatedMail % Addresses ? 1 : 0);
            ContainerId target = a < 15 ? _w.Shared
                : a < 25 ? _w.Other
                : a < 40 ? _w.Inventory.PlayerInventoryOf(PlayerSlot.FromValue((byte)((a - 25) % PlayerSlot.Count)))
                : _w.Inventory.IntakeContainer;
            ops.Add(SpawnCommand(FuzzOpType.Setup, target, a, units));
        }

        uint[] items = _w.ArrangedItemEntries;
        for (int i = 0; i < items.Length; i++)
        {
            var c = new FuzzCommandPayload { Verb = (byte)FuzzVerb.Catch, Slot = (byte)(i % PlayerSlot.Count), X = items[i] };
            ops.Add(FuzzAutomationSystem.Command(Req(FuzzOpType.Setup), in c));
        }

        return ops;
    }

    /// <summary>Re-reads the containers' entries and the live WorldItems (between ticks; the next ops are aimed from it).</summary>
    public void Snapshot()
    {
        foreach (List<ushort> list in _byContainer)
        {
            list.Clear();
        }

        EntryPool entries = _w.Inventory.Entries;
        for (int s = 0; s < entries.Capacity; s++)
        {
            ref readonly EntryRecord e = ref entries[(ushort)s];
            if (e.IsLive && e.ContainerSlot != EntryRecord.NoContainer)
            {
                _byContainer[e.ContainerSlot].Add((ushort)s);
            }
        }

        WorldItemTable table = _w.Inventory.WorldItems.Table;
        foreach (uint id in _worldItems)
        {
            if (table.TryGetSlot(new EntityId(id), out _))
            {
                continue;
            }

            if (_goneWorldItems.Count < 64)
            {
                _goneWorldItems.Add(id);
            }
            else
            {
                _goneWorldItems[_goneNext++ % 64] = id; // a ring of recently removed ids (stale pickups)
            }
        }

        _worldItems.Clear();
        for (int s = 0; s < table.Capacity; s++)
        {
            if (table[(ushort)s].IsLive)
            {
                _worldItems.Add(table[(ushort)s].Id.Value);
            }
        }
    }

    /// <summary>The next fuzz op for <paramref name="slot"/>'s player (call <see cref="Snapshot"/> first in each tick).</summary>
    public Op Next(int slot)
    {
        CacheActors();
        FuzzOpType type = Draw();
        Op op = type switch
        {
            FuzzOpType.Pick => Pick(slot),
            FuzzOpType.MoveWhole => Move(slot, split: false),
            FuzzOpType.MoveSplit => Move(slot, split: true),
            FuzzOpType.MoveSwap => Swap(slot),
            FuzzOpType.Merge => Merge(slot),
            FuzzOpType.QuickMove => QuickMove(slot),
            FuzzOpType.Return => PlayerOp(slot, InvOpKinds.Return, FuzzOpType.Return),
            FuzzOpType.Open => Open(slot),
            FuzzOpType.Close => Command(FuzzOpType.Close, new FuzzCommandPayload { Verb = (byte)FuzzVerb.Close, Slot = (byte)slot, A = (byte)(Roll(5) < 2 ? 1 : 0) }),
            // Mail Dev default, approved by Evan 2026-11-01 1:48 PM PT by card click (TD-206 item 4): a drawn spawn at 600 or more live mail units becomes a consume, keeping the run near the fixture's 400 units.
            FuzzOpType.Spawn when MailLive() < MailCeiling => SpawnCommand(FuzzOpType.Spawn, Roll(2) == 0 ? _w.Inventory.IntakeContainer : _w.Shared, (int)Roll(Addresses), 1 + (int)Roll(3)),
            FuzzOpType.Spawn => Consume(slot), // above the ceiling the faucet pauses, so the units stay near the fixture's scale
            FuzzOpType.Detach => Detach(slot),
            FuzzOpType.WorldItemCreate => Land(slot),
            FuzzOpType.Catch => Catch(slot),
            FuzzOpType.Pickup => Pickup(slot),
            FuzzOpType.Consume => Consume(slot),
            _ => Malformed(slot),
        };
        Generated++;
        if (NamesShared(in op))
        {
            SharedTargeted++;
        }

        return op;
    }

    // ------------------------------------------------------------------ op builders

    private Op Pick(int slot)
    {
        ContainerId from = ChooseOpen(slot, allowCursor: false);
        var p = new PickPayload { From = from.Value, EntryId = AnyEntryIn(from) };
        return PlayerOp(slot, InvOpKinds.Pick, FuzzOpType.Pick, in p);
    }

    private Op Move(int slot, bool split)
    {
        ContainerId cursor = _w.Inventory.CursorOf(PlayerSlot.FromValue((byte)slot));
        ContainerId from = !split && HasEntries(cursor) && Roll(10) < 7 ? cursor : ChooseOpen(slot, allowCursor: true);
        uint entry = AnyEntryIn(from, out byte count);
        ContainerId to = ChooseOpen(slot, allowCursor: !split || from != cursor);
        (short x, short y, byte rotated) = AnyCell(to);
        var p = new MovePayload
        {
            From = from.Value, EntryId = entry, To = to.Value, BaseVersion = VersionOf(to), X = x, Y = y, Rotated = rotated,
            Count = split ? (byte)(count > 1 ? 1 + Roll((uint)count - 1) : 1) : (byte)0,
        };
        return PlayerOp(slot, InvOpKinds.Move, split ? FuzzOpType.MoveSplit : FuzzOpType.MoveWhole, in p);
    }

    private Op Swap(int slot)
    {
        var s = PlayerSlot.FromValue((byte)slot);
        ContainerId cursor = _w.Inventory.CursorOf(s);
        ContainerId hotbar = _w.Inventory.HotbarOf(s);
        uint entry = AnyEntryIn(cursor);
        short x = (short)(1 + Roll(7));
        if (Slot(hotbar) is ushort hb && _byContainer[hb].Count > 0)
        {
            x = _w.Inventory.Entries[_byContainer[hb][(int)Roll((uint)_byContainer[hb].Count)]].X; // an occupied slot
        }

        var p = new MovePayload { From = cursor.Value, EntryId = entry, To = hotbar.Value, BaseVersion = VersionOf(hotbar), X = x, Y = 0 };
        return PlayerOp(slot, InvOpKinds.Move, FuzzOpType.MoveSwap, in p);
    }

    private Op Merge(int slot)
    {
        ContainerId from = ChooseOpen(slot, allowCursor: true);
        uint entry = AnyEntryIn(from);
        ContainerId to = ChooseOpen(slot, allowCursor: false);
        uint target = AnyEntryIn(to);
        if (_w.Inventory.Entries.TryGetSlot(EntryId.FromValue(entry), out ushort es))
        {
            // Aim at a same-key stack in the chosen destination, else in any container of the slot's open set (the merge can succeed).
            StackKey key = StackKey.Of(in _w.Inventory.Entries[es]);
            var s = PlayerSlot.FromValue((byte)slot);
            ContainerId[] candidates = [to, _w.Inventory.OpenSet.ExternalOf(s), _w.Inventory.PlayerInventoryOf(s), _w.Inventory.HotbarOf(s)];
            foreach (ContainerId c in candidates)
            {
                if (SameKeyEntry(c, key, es) is uint t)
                {
                    to = c;
                    target = t;
                    break;
                }
            }
        }

        var p = new MergePayload { From = from.Value, EntryId = entry, To = to.Value, TargetEntryId = target };
        return PlayerOp(slot, InvOpKinds.Merge, FuzzOpType.Merge, in p);
    }

    private uint? SameKeyEntry(ContainerId c, StackKey key, ushort except)
    {
        if (Slot(c) is not ushort cs)
        {
            return null;
        }

        foreach (ushort t in _byContainer[cs])
        {
            if (t != except && StackKey.Of(in _w.Inventory.Entries[t]) == key)
            {
                return _w.Inventory.Entries[t].Id.Value;
            }
        }

        return null;
    }

    private Op QuickMove(int slot)
    {
        ContainerId from = ChooseOpen(slot, allowCursor: true);
        uint entry = AnyEntryIn(from, out byte count);
        var p = new QuickMovePayload { From = from.Value, EntryId = entry, Count = Roll(10) < 7 || count < 2 ? (byte)0 : (byte)(1 + Roll((uint)count - 1)) };
        return PlayerOp(slot, InvOpKinds.QuickMove, FuzzOpType.QuickMove, in p);
    }

    private Op Open(int slot)
    {
        uint r = Roll(100);
        ContainerId c = r < 90 ? _w.Shared : r < 95 ? _w.Other : _w.Inventory.IntakeContainer;
        return Command(FuzzOpType.Open, new FuzzCommandPayload { Verb = (byte)FuzzVerb.Open, Slot = (byte)slot, X = c.Value });
    }

    private Op Detach(int slot)
    {
        ContainerId hotbar = _w.Inventory.HotbarOf(PlayerSlot.FromValue((byte)slot));
        uint entry = AnyEntryIn(hotbar, out byte count);
        byte units = Roll(10) == 0 ? (byte)(count + 1) : count; // ~10 % stale units
        return Command(FuzzOpType.Detach, new FuzzCommandPayload { Verb = (byte)FuzzVerb.Detach, Slot = (byte)slot, X = entry, A = units });
    }

    private Op Land(int slot)
    {
        var c = new FuzzCommandPayload
        {
            Verb = (byte)FuzzVerb.Land, Slot = (byte)slot, X = AnyFlight(),
            A = (byte)(Roll(2) == 0 ? WorldItemOrigin.Dropped : WorldItemOrigin.Thrown), B = (byte)(Roll(5) == 0 ? 1 : 0),
        };
        return Command(FuzzOpType.WorldItemCreate, in c);
    }

    private Op Catch(int slot) =>
        Command(FuzzOpType.Catch, new FuzzCommandPayload { Verb = (byte)FuzzVerb.Catch, Slot = (byte)slot, X = AnyFlight(), B = (byte)(Roll(10) == 0 ? 1 : 0) });

    private Op Pickup(int slot)
    {
        uint id = _goneWorldItems.Count > 0 && Roll(10) == 0 ? _goneWorldItems[(int)Roll((uint)_goneWorldItems.Count)]
            : _worldItems.Count > 0 ? _worldItems[(int)Roll((uint)_worldItems.Count)] : 0;
        return Command(FuzzOpType.Pickup, new FuzzCommandPayload { Verb = (byte)FuzzVerb.Pickup, Slot = (byte)slot, X = id, Y = _actors[slot].Value });
    }

    private Op Consume(int slot)
    {
        uint r = Roll(100);
        ContainerId c = r < 30 ? _w.Inventory.IntakeContainer : r < 85 ? _w.Shared : ChooseOpen(slot, allowCursor: true);
        var p = new FuzzCommandPayload
        {
            Verb = (byte)FuzzVerb.Consume, Slot = (byte)slot, X = c.Value, Y = AnyEntryIn(c), A = (byte)Roll(3), Z = _actors[slot].Value,
        };
        return Command(FuzzOpType.Consume, in p);
    }

    /// <summary>One field only the Sim can judge malformed (InvRequestCheck's <c>bad_request</c> list); every one must be rejected.</summary>
    private Op Malformed(int slot)
    {
        ContainerId from = ChooseOpen(slot, allowCursor: true);
        uint entry = AnyEntryIn(from);
        ContainerId hotbar = _w.Inventory.HotbarOf(PlayerSlot.FromValue((byte)slot));
        switch (Roll(8))
        {
            case 0:
                return PlayerOp(slot, InvOpKinds.Pick, FuzzOpType.Malformed, new PickPayload { From = 0, EntryId = entry });
            case 1:
                return PlayerOp(slot, InvOpKinds.Move, FuzzOpType.Malformed, new MovePayload { From = from.Value, EntryId = entry, To = _w.Shared.Value, Pad0 = (byte)(1 + Roll(255)) });
            case 2:
                return PlayerOp(slot, InvOpKinds.Move, FuzzOpType.Malformed, new MovePayload { From = from.Value, EntryId = entry, To = _w.Shared.Value, Rotated = (byte)(2 + Roll(254)) });
            case 3:
                return PlayerOp(slot, InvOpKinds.Move, FuzzOpType.Malformed, new MovePayload { From = from.Value, EntryId = entry, To = hotbar.Value, X = 1, Rotated = 1 });
            case 4:
                return PlayerOp(slot, InvOpKinds.Merge, FuzzOpType.Malformed, new MergePayload { From = from.Value, EntryId = entry, To = _w.Shared.Value, TargetEntryId = 0 });
            case 5:
                return PlayerOp(slot, InvOpKinds.QuickMove, FuzzOpType.Malformed, new QuickMovePayload { From = _w.Shared.Value, EntryId = entry, Pad2 = (byte)(1 + Roll(255)) });
            case 6:
                return PlayerOp(slot, InvOpKinds.Close, FuzzOpType.Malformed, new ClosePayload { Container = 0xFFFF_0000u });
            default:
            {
                var other = PlayerSlot.FromValue((byte)((slot + 1) % PlayerSlot.Count));
                return PlayerOp(slot, InvOpKinds.Pick, FuzzOpType.Malformed, new PickPayload { From = _w.Shared.Value, EntryId = EntryId.Temp(other, 1).Value });
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private Op SpawnCommand(FuzzOpType type, ContainerId target, int address, int units)
    {
        var c = new FuzzCommandPayload
        {
            Verb = (byte)FuzzVerb.Spawn, X = target.Value, Y = (uint)address, A = (byte)_kindOf[address].Value,
            B = address % 10 == 9 ? (byte)1 : (byte)0, Z = _nextMailId, W = (uint)units,
        };
        _nextMailId += (uint)units;
        return Command(type, in c);
    }

    private Op Command(FuzzOpType type, in FuzzCommandPayload c) => FuzzAutomationSystem.Command(Req(type), in c);

    private Op PlayerOp(int slot, OpKind kind, FuzzOpType type) => new() { Kind = kind, Actor = _actors[slot], ReqId = Req(type) };

    private Op PlayerOp<T>(int slot, OpKind kind, FuzzOpType type, in T payload)
        where T : unmanaged
    {
        Op op = PlayerOp(slot, kind, type);
        op.SetPayload(in payload);
        return op;
    }

    private uint Req(FuzzOpType type) => (++_seq << 5) | (uint)type;

    private FuzzOpType Draw()
    {
        uint r = Roll(100);
        foreach ((FuzzOpType type, uint weight) in _weights)
        {
            if (r < weight)
            {
                return type;
            }

            r -= weight;
        }

        return FuzzOpType.Malformed;
    }

    private long MailLive() => _w.Inventory.Ledger.LiveSum(UnitLedger.MailRow);

    private uint Roll(uint bound) => (uint)(((ulong)_rng.Next32() * bound) >> 32);

    private void CacheActors()
    {
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            _actors[s] = _w.ActorOf(s);
        }
    }

    /// <summary>A container in the slot's open set (panel, cursor if allowed, its external); its external is chosen 95 % of the time when that is the shared chest.</summary>
    private ContainerId ChooseOpen(int slot, bool allowCursor)
    {
        var s = PlayerSlot.FromValue((byte)slot);
        ContainerId external = _w.Inventory.OpenSet.ExternalOf(s);
        if (!external.IsNone && (external == _w.Shared ? Roll(100) < 95 : Roll(4) == 0))
        {
            return external;
        }

        uint r = Roll(allowCursor ? 3u : 2u);
        return r == 0 ? _w.Inventory.HotbarOf(s) : r == 1 ? _w.Inventory.PlayerInventoryOf(s) : _w.Inventory.CursorOf(s);
    }

    private uint AnyEntryIn(ContainerId c) => AnyEntryIn(c, out _);

    /// <summary>A live entry of <paramref name="c"/> from the snapshot, else a random issued id (a stale or foreign entry).</summary>
    private uint AnyEntryIn(ContainerId c, out byte count)
    {
        count = 1;
        if (Slot(c) is ushort cs && _byContainer[cs].Count > 0)
        {
            ref readonly EntryRecord e = ref _w.Inventory.Entries[_byContainer[cs][(int)Roll((uint)_byContainer[cs].Count)]];
            count = e.Count;
            return e.Id.Value;
        }

        uint last = _w.Inventory.Entries.LastId;
        return last == 0 ? 1u : 1 + Roll(last);
    }

    private bool HasEntries(ContainerId c) => Slot(c) is ushort cs && _byContainer[cs].Count > 0;

    private uint AnyFlight()
    {
        IReadOnlyList<uint> flights = _w.Automation.Flights;
        return flights.Count > 0 ? flights[(int)Roll((uint)flights.Count)] : _w.Inventory.Entries.LastId;
    }

    /// <summary>A cell of <paramref name="c"/>, preferring a free one (up to four tries), and a rotation (grid layouts only).</summary>
    private (short X, short Y, byte Rotated) AnyCell(ContainerId c)
    {
        if (Slot(c) is not ushort cs)
        {
            return (0, 0, 0);
        }

        ContainerHeader h = _w.Inventory.Containers[cs];
        short x = 0;
        short y = 0;
        for (int i = 0; i < 4; i++)
        {
            x = (short)Roll(h.W);
            y = (short)Roll(h.H);
            if (_w.Inventory.Occupancy.OwnerAt(cs, h.W, x, y) == Occupancy.Free)
            {
                break;
            }
        }

        byte rotated = h.Layout == ContainerLayout.Grid && Roll(10) < 3 ? (byte)1 : (byte)0;
        return (x, y, rotated);
    }

    private uint VersionOf(ContainerId c) => Slot(c) is ushort cs ? _w.Inventory.Containers[cs].Version : 0u;

    private ushort? Slot(ContainerId c) => _w.Inventory.Containers.TryGetSlot(c, out ushort s) ? s : null;

    private bool NamesShared(in Op op)
    {
        uint shared = _w.Shared.Value;
        if ((FuzzOpType)(op.ReqId & TypeMask) == FuzzOpType.Malformed)
        {
            return false; // rejected before it reaches any container
        }

        if (op.Kind == FuzzAutomationSystem.CommandKind)
        {
            FuzzCommandPayload c = op.PayloadAs<FuzzCommandPayload>();
            return (FuzzVerb)c.Verb is FuzzVerb.Spawn or FuzzVerb.Open or FuzzVerb.Consume && c.X == shared; // X is a container only for these
        }

        if (op.Kind == InvOpKinds.Pick)
        {
            return op.PayloadAs<PickPayload>().From == shared;
        }

        if (op.Kind == InvOpKinds.Move)
        {
            MovePayload p = op.PayloadAs<MovePayload>();
            return p.From == shared || p.To == shared;
        }

        if (op.Kind == InvOpKinds.Merge)
        {
            MergePayload p = op.PayloadAs<MergePayload>();
            return p.From == shared || p.To == shared;
        }

        return op.Kind == InvOpKinds.QuickMove && op.PayloadAs<QuickMovePayload>().From == shared;
    }
}
