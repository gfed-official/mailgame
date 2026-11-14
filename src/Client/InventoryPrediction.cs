using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Client
{
    /// <summary>
    /// The optimistic preview of item moves (doc 06 §4 strategy R, doc 09 §2.5 "pending state").
    /// The confirmed mirrors in <see cref="ClientWorld.Containers"/> only ever change by server
    /// deltas. What the UI shows is a view: the confirmed state with every request still on its
    /// way applied on top, by the same <see cref="InventoryOps"/> the server runs. A rejected
    /// request simply drops out of the list and the item is back where the server has it; a
    /// confirmed one drops out too, and by then the server's delta has put the item there for real.
    /// </summary>
    public sealed class InventoryPrediction
    {
        /// <summary>Ids of entries that exist only in the preview (a split's new stack); the server never uses this range.</summary>
        public const uint PrivateIdBase = 0x8000_0000u;

        private readonly ContentDb _content;
        private readonly ClientWorld _world;
        private readonly List<SimRequest> _pending = new List<SimRequest>();
        private readonly Dictionary<uint, GridContainer> _views = new Dictionary<uint, GridContainer>();
        private readonly List<GridContainer> _targets = new List<GridContainer>(2);
        private bool _stale;

        /// <summary>Changes whenever a view may show something else than before: a redraw key for the UI.</summary>
        public int Revision { get; private set; }
        public int PendingCount => _pending.Count;

        public InventoryPrediction(ContentDb content, ClientWorld world)
        {
            _content = content;
            _world = world;
        }

        public static bool Predicts(RequestKind kind) =>
            kind == RequestKind.Move || kind == RequestKind.QuickMove || kind == RequestKind.Sort;

        /// <summary>A request went out: show its effect from now on.</summary>
        public void Add(in SimRequest request)
        {
            _pending.Add(request);
            Invalidate();
        }

        /// <summary>The server answered (either way): the request is no longer a guess.</summary>
        public void Resolve(uint requestId)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].RequestId != requestId) continue;
                _pending.RemoveAt(i);
                Invalidate();
                return;
            }
        }

        /// <summary>A confirmed mirror changed (or came or went): the views have to be rebuilt on top of it.</summary>
        public void Invalidate()
        {
            _stale = true;
            Revision++;
        }

        /// <summary>The container as the player should see it; the confirmed mirror itself while nothing is pending.</summary>
        public GridContainer? View(uint containerId)
        {
            if (!_world.Containers.TryGetValue(containerId, out var confirmed)) return null;
            if (_pending.Count == 0) return confirmed;
            if (_stale) Rebuild();
            return _views.TryGetValue(containerId, out var view) ? view : confirmed;
        }

        /// <summary>True while the server has not confirmed this entry as it is shown (doc 09 §2.5: drawn at 60 %).</summary>
        public bool IsPending(uint containerId, GridEntry shown)
        {
            if (_pending.Count == 0) return false;
            if (!_world.Containers.TryGetValue(containerId, out var confirmed)) return false;
            var real = confirmed.Get(shown.EntryId);
            return real == null || real.X != shown.X || real.Y != shown.Y || real.Rotated != shown.Rotated || real.Stack.Count != shown.Stack.Count;
        }

        /// <summary>True for an entry the server does not know yet (the new half of a split): it cannot be named in a request.</summary>
        public static bool IsPrivate(GridEntry entry) => entry.EntryId >= PrivateIdBase;

        private void Rebuild()
        {
            _stale = false;
            _views.Clear();
            // The same private ids on every rebuild, as long as the same requests are pending.
            var ids = new EntryIdAllocator(PrivateIdBase);
            foreach (var request in _pending) Apply(request, ids);
        }

        /// <summary>A scratch copy of a container the player may touch: their own, or the open external one.</summary>
        private GridContainer? Scratch(uint containerId)
        {
            if (_views.TryGetValue(containerId, out var view)) return view;
            if (containerId != _world.HotbarId && containerId != _world.InventoryId && containerId != _world.OpenContainerId) return null;
            if (!_world.Containers.TryGetValue(containerId, out var confirmed)) return null;
            view = confirmed.Clone();
            _views[containerId] = view;
            return view;
        }

        /// <summary>
        /// Mirrors the server's handling of the request (SimWorld.Requests) on the scratch copies.
        /// A request that fails here is skipped: the server will most likely refuse it as well.
        /// </summary>
        private void Apply(in SimRequest r, EntryIdAllocator ids)
        {
            var from = Scratch(r.Container);
            if (from == null) return;
            switch (r.Kind)
            {
                case RequestKind.Move:
                {
                    var to = Scratch(r.ToContainer);
                    if (to == null) return;
                    InventoryOps.Move(_content, from, r.EntryId, to, r.X, r.Y, r.Rotated, r.Count, ids);
                    break;
                }
                case RequestKind.QuickMove:
                {
                    _targets.Clear();
                    bool own = r.Container == _world.HotbarId || r.Container == _world.InventoryId;
                    if (!own)
                    {
                        Add(_targets, Scratch(_world.HotbarId));
                        Add(_targets, Scratch(_world.InventoryId));
                    }
                    else if (_world.OpenContainerId != 0) Add(_targets, Scratch(_world.OpenContainerId));
                    else Add(_targets, Scratch(r.Container == _world.HotbarId ? _world.InventoryId : _world.HotbarId));
                    InventoryOps.QuickMove(_content, from, r.EntryId, _targets);
                    break;
                }
                case RequestKind.Sort:
                    InventoryOps.Sort(_content, from, r.SortKey);
                    break;
            }
        }

        private static void Add(List<GridContainer> list, GridContainer? container)
        {
            if (container != null) list.Add(container);
        }
    }
}
