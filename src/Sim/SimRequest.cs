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
        /// <summary>Deliver: destination id.</summary>
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

        public static SimRequest ContinueRun(uint reqId) => new SimRequest { RequestId = reqId, Kind = RequestKind.ContinueRun };
    }
}
