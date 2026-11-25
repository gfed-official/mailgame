using System;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Sim
{
    public enum RequestKind : byte
    {
        OpenContainer = 0,
        CloseContainer = 1,
        Move = 2,
        QuickMove = 3,
        Sort = 4,
        Deliver = 5,
        /// <summary>Host, Lobby: start the run.</summary>
        StartRun = 6,
        /// <summary>Lobby / Prep: set or clear the player's Ready flag (<see cref="SimRequest.Count"/> 1 or 0).</summary>
        SetReady = 7,
        /// <summary>Host, Results: back to the Lobby.</summary>
        ContinueRun = 8,
        /// <summary>Host, alone on the server: freeze or resume the tick (<see cref="SimRequest.Count"/> 1 or 0).</summary>
        SetPaused = 9,
        /// <summary>Draft: take the card of the offer at index <see cref="SimRequest.Count"/>.</summary>
        PickPerk = 10,
        /// <summary>At the shop counter, Prep or Payday: buy the entry of ContentDb.ShopItems at index <see cref="SimRequest.Count"/>.</summary>
        Buy = 11,
        /// <summary>At the shop counter: sell <see cref="SimRequest.Count"/> units (0 = the whole stack) of an entry of the player's own containers.</summary>
        Sell = 12,
        /// <summary>Use one unit of an entry of the player's own containers on themselves (a Bandage, Berries).</summary>
        Use = 13,
        /// <summary>
        /// Prep or Delivery: one swing at the resource node <see cref="SimRequest.DestId"/> (an index
        /// into WorldMap.Props) with what is in hotbar slot <see cref="SimRequest.X"/>.
        /// </summary>
        Harvest = 14,
        /// <summary>
        /// Prep or Delivery: build ContentDb.Buildings[<see cref="SimRequest.DestId"/>] on the tile
        /// (<see cref="SimRequest.X"/>, <see cref="SimRequest.Y"/>), turned <see cref="SimRequest.Count"/> quarter turns.
        /// </summary>
        PlaceConstruct = 15,
        /// <summary>Prep or Delivery: take down what is built on the tile (<see cref="SimRequest.X"/>, <see cref="SimRequest.Y"/>).</summary>
        RemoveConstruct = 16,
        /// <summary>Get into the saddle of the vehicle whose id is <see cref="SimRequest.EntryId"/> (getting off is the jump key).</summary>
        Mount = 17,
        /// <summary>Lobby: take the starting kit ContentDb.Kits[<see cref="SimRequest.Count"/>].</summary>
        SetKit = 18,
        /// <summary>
        /// Host, Lobby: play SimWorld.WorldChoices[<see cref="SimRequest.Count"/>] from the seed
        /// <see cref="SimRequest.EntryId"/> (doc 01 §5.3, doc 06 §5 step 1).
        /// </summary>
        SetWorld = 19,
        /// <summary>The Draft, before choosing: three new cards of the player's own (doc 01 §2.5 "one free reroll per player per run").</summary>
        Reroll = 20,
        /// <summary>
        /// Prep or Delivery: a line of ContentDb.Buildings[<see cref="SimRequest.DestId"/>] (doc 04
        /// §1.1 drag placement) from (<see cref="SimRequest.X"/>, <see cref="SimRequest.Y"/>) to the
        /// tile packed in <see cref="SimRequest.ToContainer"/> (x | y &lt;&lt; 8) along a row or a column,
        /// each turned <see cref="SimRequest.Count"/> quarter turns. Every tile is judged by itself.
        /// </summary>
        PlaceLine = 21,
        /// <summary>A client's belt segment headed at (<see cref="SimRequest.X"/>, <see cref="SimRequest.Y"/>) did not match the server's checksum: send it whole.</summary>
        BeltResync = 22,
        /// <summary>
        /// Doc 04 §2.1: the carrier holds interact at the belt on tile (<see cref="SimRequest.X"/>,
        /// <see cref="SimRequest.Y"/>); its segment runs faster for a moment. Sent again while held.
        /// </summary>
        PushBelt = 23,
        /// <summary>
        /// A sorter's or a splitter's filters on tile (<see cref="SimRequest.X"/>, <see cref="SimRequest.Y"/>)
        /// (doc 04 §2.4, doc 06 §3 ConfigureConstruct). <see cref="SimRequest.Count"/>: 0 opens
        /// them (the status comes while open), 1 shuts them, 2 sets output
        /// <see cref="SimRequest.EntryId"/> / 16, slot EntryId % 16 to the filter <see cref="SimRequest.DestId"/>.
        /// </summary>
        ConfigureConstruct = 24,
        /// <summary>Doc 04 §3.1: an Oil Can out of the pockets into the tank of vehicle <see cref="SimRequest.EntryId"/>.</summary>
        Refuel = 25,
        /// <summary>
        /// Doc 09 §2.6 Ping: a mark for the team at (<see cref="SimRequest.EntryId"/>,
        /// <see cref="SimRequest.ToContainer"/>) in centimetres, of the kind <see cref="SimRequest.Count"/> (<see cref="PingKind"/>).
        /// </summary>
        Ping = 26,
    }

    /// <summary>What a ping says (doc 09 §2.6: a click, or the wheel's "Deliver here", "Build here", "Danger", "Need materials").</summary>
    public enum PingKind : byte
    {
        Here = 0,
        Deliver = 1,
        Build = 2,
        Danger = 3,
        Materials = 4,
    }

    /// <summary>
    /// A reliable client action (doc 06 §4 strategy R). One flat struct covers every M0 request;
    /// unused fields are zero. The server always answers with a RequestResultEvent carrying RequestId.
    /// </summary>
    public struct SimRequest
    {
        public uint RequestId;
        public RequestKind Kind;
        /// <summary>Open/Close/Sort: the container. Move/QuickMove/Deliver: the source container.</summary>
        public uint Container;
        public uint EntryId;
        /// <summary>Move: target container.</summary>
        public uint ToContainer;
        public byte X, Y;
        public bool Rotated;
        /// <summary>Move: units to move, 0 = whole stack.</summary>
        public ushort Count;
        /// <summary>Deliver: destination id. Harvest: the node, an index into WorldMap.Props. PlaceConstruct: the building.</summary>
        public ushort DestId;
        public SortKey SortKey;

        public static SimRequest Open(uint reqId, uint container) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.OpenContainer, Container = container };

        public static SimRequest Close(uint reqId, uint container) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.CloseContainer, Container = container };

        public static SimRequest Move(uint reqId, uint from, uint entryId, uint to, int x, int y, bool rotated = false, int count = 0) =>
            new SimRequest
            {
                RequestId = reqId, Kind = RequestKind.Move, Container = from, EntryId = entryId, ToContainer = to,
                X = (byte)x, Y = (byte)y, Rotated = rotated, Count = (ushort)count,
            };

        public static SimRequest QuickMove(uint reqId, uint from, uint entryId) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.QuickMove, Container = from, EntryId = entryId };

        public static SimRequest Sort(uint reqId, uint container, SortKey key) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Sort, Container = container, SortKey = key };

        public static SimRequest Deliver(uint reqId, uint from, uint entryId, int destId) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Deliver, Container = from, EntryId = entryId, DestId = (ushort)destId };

        public static SimRequest StartRun(uint reqId) => new SimRequest { RequestId = reqId, Kind = RequestKind.StartRun };

        public static SimRequest SetReady(uint reqId, bool ready) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.SetReady, Count = (ushort)(ready ? 1 : 0) };
        /// <summary>
        /// A client's world is built and drawn (doc 01 §1 Generating): a Ready that counts only
        /// while the server is still Generating, so one that arrives after Prep began is no Prep Ready.
        /// </summary>
        public static SimRequest WorldDrawn(uint reqId) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.SetReady, Count = WorldDrawnCount };
        /// <summary><see cref="Count"/> of <see cref="WorldDrawn"/>.</summary>
        public const ushort WorldDrawnCount = 2;

        public static SimRequest ContinueRun(uint reqId) => new SimRequest { RequestId = reqId, Kind = RequestKind.ContinueRun };

        public static SimRequest SetPaused(uint reqId, bool paused) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.SetPaused, Count = (ushort)(paused ? 1 : 0) };

        public static SimRequest PickPerk(uint reqId, int card) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.PickPerk, Count = (ushort)card };

        public static SimRequest Buy(uint reqId, int shopItem) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Buy, Count = (ushort)shopItem };

        public static SimRequest Sell(uint reqId, uint from, uint entryId, int count = 0) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Sell, Container = from, EntryId = entryId, Count = (ushort)count };

        public static SimRequest Use(uint reqId, uint from, uint entryId) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Use, Container = from, EntryId = entryId };

        public static SimRequest Harvest(uint reqId, int node, int heldSlot) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Harvest, DestId = (ushort)node, X = (byte)heldSlot };

        public static SimRequest PlaceConstruct(uint reqId, int building, int tx, int tz, int rotation) =>
            new SimRequest
            {
                RequestId = reqId, Kind = RequestKind.PlaceConstruct, DestId = (ushort)building, X = (byte)tx, Y = (byte)tz, Count = (ushort)rotation,
            };

        public static SimRequest Mount(uint reqId, uint vehicle) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Mount, EntryId = vehicle };

        public static SimRequest Refuel(uint reqId, uint vehicle) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.Refuel, EntryId = vehicle };

        public static SimRequest Reroll(uint reqId) => new SimRequest { RequestId = reqId, Kind = RequestKind.Reroll };

        public static SimRequest SetWorld(uint reqId, int choice, uint seed) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.SetWorld, Count = (ushort)choice, EntryId = seed };

        public static SimRequest SetKit(uint reqId, int kit) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.SetKit, Count = (ushort)kit };

        public static SimRequest PlaceLine(uint reqId, int building, int x0, int z0, int x1, int z1, int rotation) =>
            new SimRequest
            {
                RequestId = reqId, Kind = RequestKind.PlaceLine, DestId = (ushort)building, X = (byte)x0, Y = (byte)z0,
                ToContainer = (uint)(x1 & 0xFF) | (uint)(z1 & 0xFF) << 8, Count = (ushort)rotation,
            };

        public static SimRequest BeltResync(uint reqId, int headX, int headZ) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.BeltResync, X = (byte)headX, Y = (byte)headZ };

        public const ushort ConfigureOpen = 0, ConfigureClose = 1, ConfigureSet = 2, ConfigureHire = 3;

        public static SimRequest Configure(uint reqId, int tx, int tz, ushort op, int output = 0, int slot = 0, ushort filter = 0) =>
            new SimRequest
            {
                RequestId = reqId, Kind = RequestKind.ConfigureConstruct, X = (byte)tx, Y = (byte)tz, Count = op,
                EntryId = (uint)(output * 16 + slot), DestId = filter,
            };

        public static SimRequest PushBelt(uint reqId, int tx, int tz) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.PushBelt, X = (byte)tx, Y = (byte)tz };

        public static SimRequest Ping(uint reqId, float x, float z, PingKind kind) =>
            new SimRequest
            {
                RequestId = reqId, Kind = RequestKind.Ping, EntryId = (uint)Math.Max(0, (int)MathF.Round(x * 100f)),
                ToContainer = (uint)Math.Max(0, (int)MathF.Round(z * 100f)), Count = (ushort)kind,
            };

        public static SimRequest RemoveConstruct(uint reqId, int tx, int tz) =>
            new SimRequest { RequestId = reqId, Kind = RequestKind.RemoveConstruct, X = (byte)tx, Y = (byte)tz };
    }
}
