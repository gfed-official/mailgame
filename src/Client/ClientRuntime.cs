using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    public enum ClientState : byte
    {
        Connecting,
        /// <summary>Hello sent, waiting for the join state.</summary>
        Joining,
        Joined,
        Rejected,
        Disconnected,
    }

    /// <summary>What the local player wants this tick; filled by the input layer or a bot.</summary>
    public struct ClientInput
    {
        /// <summary>World-space move direction, each axis -1..1.</summary>
        public float MoveX, MoveZ;
        /// <summary>Where the player looks: yaw 0 faces −Z. The body faces the same way (first person).</summary>
        public float YawRadians;
        /// <summary>Look pitch, positive up. Client-side only: it aims the camera and the interact ray.</summary>
        public float PitchRadians;
        public InputButtons Buttons;
        public byte HeldSlot;
    }

    /// <summary>
    /// Client side of the connection (doc 07 §3): keeps <see cref="ClientWorld"/> up to date from
    /// snapshots and events, predicts the local player and turns UI actions into requests.
    /// </summary>
    public sealed class ClientRuntime : IClientTransportHandler
    {
        /// <summary>Remote players are rendered this far in the past (doc 06 §3: 100 ms).</summary>
        public const double InterpolationDelayTicks = 3.0;
        private const int MaxPendingInputs = 120;
        /// <summary>The cards of the shop stay up this far past the reach they were opened within (the server serves a little further still).</summary>
        private const float ShopLeaveSlack = 0.4f;
        /// <summary>
        /// Playtest df77576: the shop's cards open as Payday runs out came down under the Draft's,
        /// and the next number key, meant for the shop, took a perk. For this long after that the
        /// number keys take no card (a click still does).
        /// </summary>
        public const double ShopToDraftKeyGraceSeconds = 1.5;
        /// <summary>The server tick (as this client reckons it) before which the Draft's number keys do nothing.</summary>
        private double _draftKeysFrom;

        public ClientState State { get; private set; } = ClientState.Connecting;
        public RejectReason RejectReason { get; private set; }
        public string? DisconnectDetail { get; private set; }
        public ClientWorld? World { get; private set; }
        public string Name { get; }
        /// <summary>What this client asks the server for when it joins (playtest nits 10 and 13).</summary>
        public PlaytestFlags Playtest { get; }
        /// <summary>The shift this client asks runs to start at, 0 = the first.</summary>
        public int StartShift { get; }
        /// <summary>--playtest-wallet: the money (cents) this player asks runs to start with.</summary>
        public int StartWallet { get; }
        /// <summary>
        /// Who this client says it is (doc 06 §6): a server that still holds the body of a player who
        /// said the same and lost their connection gives it back. 0: nobody in particular.
        /// </summary>
        public ulong Account { get; }

        /// <summary>Set by the input layer; sampled once per client tick.</summary>
        public ClientInput Input;

        /// <summary>Raised for every reliable event after it has been applied to <see cref="World"/>.</summary>
        public event Action<SimEvent>? EventReceived;
        /// <summary>Raised with the request that was answered and the server's verdict.</summary>
        public event Action<SimRequest, OpResult>? RequestCompleted;
        /// <summary>Raised at the start of every client tick, before <see cref="Input"/> is sampled (bots decide here).</summary>
        public event Action? TickStarting;
        /// <summary>Raised when the host has put the game onto another map or seed and this client has built it.</summary>
        public event Action? WorldReplaced;
        /// <summary>
        /// Whether the screen has the world drawn (the Generating phase, doc 01 §1); the client
        /// says it is ready once it has. Null (a headless client): at once.
        /// </summary>
        public Func<bool>? WorldDrawn;
        /// <summary>The Ready of this Generating phase has gone out.</summary>
        private bool _generatedSent;

        // Diagnostics (M0 acceptance: container hashes match on every version; prediction error).
        public int ContainerVersionsVerified { get; private set; }
        public int ContainerDesyncs { get; private set; }
        public int SnapshotsReceived { get; private set; }
        public int BadPackets { get; private set; }
        public double PredictionErrorRms => _errorSamples == 0 ? 0.0 : Math.Sqrt(_errorSumSq / _errorSamples);
        public double PredictionErrorMax { get; private set; }
        public int PendingRequestCount => _pendingRequests.Count;
        /// <summary>Hammer swings and cannon shots the local player has started; the view model plays each one.</summary>
        public int LocalHammerSwings { get; private set; }
        public int LocalCannonShots { get; private set; }
        /// <summary>Swings at a tree, a bush or a boulder the local player has started; the view model plays these too.</summary>
        public int LocalHarvestSwings { get; private set; }

        private readonly ContentDb _content;
        private readonly IClientTransport _transport;
        private MoveConstants _move;
        private readonly NetWriter _writer = new NetWriter(256);
        private readonly List<PlayerInput> _pendingInputs = new List<PlayerInput>();
        private readonly InputsMsg _inputsMsg = new InputsMsg();
        private readonly Dictionary<uint, SimRequest> _pendingRequests = new Dictionary<uint, SimRequest>();
        private readonly List<ushort> _enemyScratch = new List<ushort>();
        private readonly List<SimEvent> _early = new List<SimEvent>();
        private uint _hammerCooldownTicks, _cannonCooldownTicks;
        private float _shopRange;
        private uint _hammerReadySeq, _cannonReadySeq;
        private uint _nextSeq = 1;
        private uint _nextRequestId = 1;
        private uint _lastSnapshotTick;
        private double _accumulator;
        private double _errorSumSq;
        private long _errorSamples;

        public ClientRuntime(ContentDb content, IClientTransport transport, string name, PlaytestFlags playtest = PlaytestFlags.None, int startShift = 0,
            ulong account = 0, int rank = 1, int startWallet = 0)
        {
            StartWallet = Math.Max(0, startWallet);
            Account = account;
            Rank = Math.Clamp(rank, 1, byte.MaxValue);
            Playtest = playtest;
            StartShift = Math.Clamp(startShift, 0, byte.MaxValue);
            _content = content;
            _transport = transport;
            ReadTunables();
            Name = name;
        }

        private void ReadTunables()
        {
            _move = MoveConstants.From(_content);
            _hammerCooldownTicks = SimConstants.SecondsToTicks(_content.Balance.GetDouble("hammerCooldownSeconds"));
            _cannonCooldownTicks = SimConstants.SecondsToTicks(_content.Balance.GetDouble("cannonCooldownSeconds"));
            _shopRange = _content.Balance.GetFloat("shopRange");
        }

        /// <summary>
        /// After a hot reload of the content this client runs on (doc 07 §6; the host's own client
        /// shares the server's): the prediction steps with the new numbers, the perks taken count
        /// with their new values.
        /// </summary>
        public void ContentReloaded()
        {
            ReadTunables();
            World?.ContentReloaded();
        }

        /// <summary>Real-time driver: polls the network once, then runs the fixed client ticks the elapsed time covers.</summary>
        public void Update(double deltaSeconds)
        {
            _transport.Poll(this);
            _accumulator += deltaSeconds;
            int ticks = 0;
            while (_accumulator >= SimConstants.TickDt)
            {
                _accumulator -= SimConstants.TickDt;
                if (ticks++ < 5) StepInput();
            }
        }

        /// <summary>One client tick with a network poll first (bots and tests drive this directly).</summary>
        public void Tick()
        {
            _transport.Poll(this);
            StepGenerating();
            StepInput();
            World?.PrunePings();
        }

        /// <summary>
        /// Doc 01 §1 "Generating": the server waits for every client to have the world built (the
        /// map from the seed, its hash checked at the Welcome or the world's change) and drawn;
        /// this client says so with its Ready as soon as the screen has.
        /// </summary>
        private void StepGenerating()
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Global.Phase != RunPhase.Generating)
            {
                _generatedSent = false;
                return;
            }
            if (_generatedSent || world.LocalReady || !(WorldDrawn?.Invoke() ?? true)) return;
            if (Send(SimRequest.WorldDrawn(0)) != 0) _generatedSent = true;
        }

        public void Disconnect()
        {
            if (State == ClientState.Disconnected) return;
            _transport.Close();
            State = ClientState.Disconnected;
        }

        /// <summary>Fraction of the current client tick already elapsed, for render smoothing of the local player.</summary>
        public float TickFraction => (float)(_accumulator / SimConstants.TickDt);

        // --- requests ---------------------------------------------------------------------------

        /// <summary>Sends a request; the id is assigned here and returned. The answer arrives via <see cref="RequestCompleted"/>.</summary>
        private readonly HashSet<int> _beltResyncAsked = new HashSet<int>();

        public uint Send(SimRequest request)
        {
            if (State != ClientState.Joined) return 0;
            request.RequestId = _nextRequestId++;
            _pendingRequests[request.RequestId] = request;
            if (InventoryPrediction.Predicts(request.Kind)) World!.Prediction.Add(request);
            _writer.Reset();
            RequestMsg.Write(_writer, request);
            _transport.Send(NetChannel.Reliable, _writer.ToArray());
            return request.RequestId;
        }

        public uint OpenContainer(uint containerId) => Send(SimRequest.Open(0, containerId));

        /// <summary>Gets into the saddle of a parked vehicle (doc 04 §3.1). 0 where there is nothing to ask.</summary>
        public uint Mount(EntityId vehicle)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused || world.Local.Riding) return 0;
            if (!world.Vehicles.TryGetValue(vehicle, out var v) || v.Ridden) return 0;
            return Send(SimRequest.Mount(0, vehicle.Value));
        }
        /// <summary>A ping for the team at a point of the map (doc 09 §2.6). 0 if not sent.</summary>
        public uint Ping(float x, float z, PingKind kind)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal) return 0;
            if (x < 0f || z < 0f || x > world.Map.WidthM || z > world.Map.DepthM) return 0;
            return Send(SimRequest.Ping(0, x, z, kind));
        }

        /// <summary>An Oil Can from the pockets into a vehicle's tank (doc 04 §3.1). 0 if not sent.</summary>
        public uint Refuel(EntityId vehicle)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused) return 0;
            if (!world.Vehicles.TryGetValue(vehicle, out var v) || !v.Def.Fuelled) return 0;
            return Send(SimRequest.Refuel(0, vehicle.Value));
        }

        public uint CloseContainer() => World == null || World.OpenContainerId == 0 ? 0 : Send(SimRequest.Close(0, World.OpenContainerId));
        public uint QuickMove(uint containerId, uint entryId) => Send(SimRequest.QuickMove(0, containerId, entryId));
        /// <summary>Drag and drop: the entry (or <paramref name="count"/> units of it; 0 = all) to a cell of a container.</summary>
        public uint Move(uint fromContainer, uint entryId, uint toContainer, int x, int y, bool rotated = false, int count = 0) =>
            Send(SimRequest.Move(0, fromContainer, entryId, toContainer, x, y, rotated, count));
        public uint Sort(uint containerId, SortKey key) => Send(SimRequest.Sort(0, containerId, key));
        public uint Deliver(uint containerId, uint entryId, int destId) => Send(SimRequest.Deliver(0, containerId, entryId, destId));
        public uint StartRun() => Send(SimRequest.StartRun(0));
        public uint SetReady(bool ready) => Send(SimRequest.SetReady(0, ready));
        public uint ContinueRun() => Send(SimRequest.ContinueRun(0));
        public uint SetPaused(bool paused) => Send(SimRequest.SetPaused(0, paused));

        /// <summary>
        /// Takes a card of the Draft (doc 01 §2.5). Returns the request id, 0 where there is nothing
        /// to ask: no Draft, no such card, a card that is not open to this player, or a choice made.
        /// </summary>
        public uint PickPerk(int card)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || world.Global.Phase != RunPhase.Draft) return 0;
            return world.CardState(card) == OpResult.Ok ? Send(SimRequest.PickPerk(0, card)) : 0;
        }

        /// <summary>
        /// The reroll (doc 01 §2.5, R in the Draft): three new cards of this player's own, while
        /// they have not chosen and have a reroll left. Returns the request id, 0 where there is
        /// nothing to ask.
        /// </summary>
        public uint Reroll()
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.CanReroll) return 0;
            return Send(SimRequest.Reroll(0));
        }

        /// <summary>
        /// A number key, counted from 0 for "1": while a card of the Draft is open to this player
        /// the keys of the cards take a card and no hotbar slot. Returns whether the key was the
        /// Draft's; a card that is not open takes the key and does nothing.
        /// </summary>
        public bool DraftKeysHeld => World != null && World.ServerTick < _draftKeysFrom;

        public bool PressCardKey(int key)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || world.Global.Phase != RunPhase.Draft) return false;
            if (key < 0 || key >= world.DraftOffer.Count || world.FirstOpenCard < 0) return false;
            // A key meant for the shop that the Draft has just replaced is swallowed, not a pick.
            if (DraftKeysHeld) return true;
            PickPerk(key);
            return true;
        }

        // --- the starting kit (doc 03 §4.4) ----------------------------------------------------

        /// <summary>The Postal Rank this client says it has: the kits up to it are open.</summary>
        public int Rank { get; }

        /// <summary>The kit the local player has picked (the server's word, from the snapshots).</summary>
        public KitDef LocalKit
        {
            get
            {
                var world = World;
                if (world == null || !world.Players.TryGetValue(world.LocalId, out var me)) return _content.DefaultKit;
                return me.Kit < _content.Kits.Count ? _content.Kits[me.Kit] : _content.DefaultKit;
            }
        }

        /// <summary>OK if the kit can be picked here and now; why not otherwise.</summary>
        public OpResult KitState(KitDef kit)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || world.Global.Phase != RunPhase.Lobby) return OpResult.WrongPhase;
            return kit.Rank > Rank ? OpResult.RankTooLow : OpResult.Ok;
        }

        /// <summary>Picks a kit in the Lobby; 0 if it is not open.</summary>
        public uint ChooseKit(KitDef kit) => KitState(kit) == OpResult.Ok ? Send(SimRequest.SetKit(0, kit.Index)) : 0;

        /// <summary>The next kit open at this rank after the one picked, round to the first; 0 when there is no other.</summary>
        public uint CycleKit()
        {
            var kits = _content.Kits;
            int from = LocalKit.Index;
            for (int i = 1; i < kits.Count; i++)
            {
                var kit = kits[(from + i) % kits.Count];
                if (KitState(kit) == OpResult.Ok) return ChooseKit(kit);
            }
            return 0;
        }

        // --- the world (doc 01 §5.3, doc 06 §5) ------------------------------------------------

        private readonly Random _seedDice = new Random();

        /// <summary>The map played now among SimWorld.WorldChoices; −1 for one not offered.</summary>
        public int WorldChoice
        {
            get
            {
                var world = World;
                if (world == null) return -1;
                for (int i = 0; i < SimWorld.WorldChoices.Count; i++) if (SimWorld.WorldChoices[i] == world.Map.Id) return i;
                return -1;
            }
        }

        /// <summary>True when this player may pick the world: the host, in the Lobby.</summary>
        public bool CanChooseWorld => State == ClientState.Joined && World != null && World.IsHost && World.Global.Phase == RunPhase.Lobby;

        /// <summary>Host, Lobby: play a world of SimWorld.WorldChoices from a seed; 0 if not the host's to pick now.</summary>
        public uint ChooseWorld(int choice, uint seed) =>
            CanChooseWorld && choice >= 0 && choice < SimWorld.WorldChoices.Count ? Send(SimRequest.SetWorld(0, choice, seed)) : 0;

        /// <summary>The next world, from the same seed.</summary>
        public uint CycleWorld() => ChooseWorld((WorldChoice + 1) % SimWorld.WorldChoices.Count, World?.Seed ?? 1);

        /// <summary>The same world from a seed drawn at random.</summary>
        public uint NewSeed() => ChooseWorld(Math.Max(0, WorldChoice), (uint)_seedDice.Next(1, int.MaxValue));

        // --- the shop (doc 03 §4.4) -------------------------------------------------------------

        /// <summary>The counter serves the local player where they stand: within its reach in Prep, anywhere at Payday.</summary>
        private bool AtShop(ClientWorld world, float slack)
        {
            if (!world.HasLocal || world.Local.Dead || !world.ShopServes) return false;
            var po = world.Map.PostOffice;
            float dx = world.Local.X - po.ShopX, dz = world.Local.Z - po.ShopZ;
            return ShopRules.InReach(world.Global.Phase, dx * dx + dz * dz, _shopRange + slack);
        }

        /// <summary>
        /// Puts the cards of the shop up. Nothing is asked of the server: the counter keeps no
        /// record of who looks at it, and every purchase is checked on its own. Returns whether
        /// they are up; a chest or the Intake that was open is shut first.
        /// </summary>
        public bool OpenShop()
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !AtShop(world, 0f)) return false;
            if (world.OpenContainerId != 0) CloseContainer();
            CloseFilters();
            world.ShopOpen = true;
            return true;
        }

        public void CloseShop()
        {
            if (World != null) World.ShopOpen = false;
        }

        /// <summary>
        /// The shop key: the cards up where the counter serves this player (<see cref="OpenShop"/>:
        /// no aim asked), down when they are up. Returns whether the key did anything.
        /// </summary>
        public bool ToggleShop()
        {
            var world = World;
            if (world == null || !world.ShopOpen) return OpenShop();
            CloseShop();
            return true;
        }

        /// <summary>
        /// Buys an entry off the counter for the team's money. Returns the request id, 0 where there
        /// is nothing to ask: the cards are not up, the entry is not to be had as things stand
        /// (<see cref="ClientWorld.ShopState"/>), or the last request for it is still on its way.
        /// </summary>
        public uint Buy(ShopItemDef def)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.ShopOpen || world.IsBuying(def)) return 0;
            if (world.ShopState(def) != OpResult.Ok) return 0;
            uint id = Send(SimRequest.Buy(0, def.Index));
            if (id != 0) world.ShopPending.Add(def);
            return id;
        }

        /// <summary>
        /// A number key, counted from 0 for "1": while the cards of the shop are up the keys of the
        /// cards buy and select no hotbar slot. Returns whether the key was the shop's; a card that
        /// cannot be bought takes the key and does nothing.
        /// </summary>
        public bool PressShopKey(int key)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.ShopOpen) return false;
            var shelf = world.ShopShelf;
            if (key < 0 || key >= shelf.Count) return false;
            Buy(shelf[key]);
            return true;
        }

        /// <summary>
        /// Sells a stack the local player carries (or <paramref name="count"/> units of it; 0 = all)
        /// over the counter. Returns the request id, 0 while the cards are not up.
        /// </summary>
        public uint Sell(uint containerId, uint entryId, int count = 0)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.ShopOpen) return 0;
            return Send(SimRequest.Sell(0, containerId, entryId, count));
        }

        /// <summary>
        /// The use key: one of what lies in the selected hotbar slot, when it is something to use
        /// (a Bandage, Berries). Returns the request id, 0 where the hand holds nothing of the kind;
        /// the server says no at full health.
        /// </summary>
        public uint UseHeld()
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused) return 0;
            var entry = world.UsableAt(Input.HeldSlot);
            return entry == null ? 0 : Send(SimRequest.Use(0, world.HotbarId, entry.EntryId));
        }

        /// <summary>
        /// One swing at a resource node (doc 02 §3.9) with the selected hotbar slot in the hand.
        /// Returns the request id, 0 where there is nothing to ask: the swing would not land as
        /// things stand (<see cref="ClientWorld.NodeState"/>). How far the node is, is the server's to say.
        /// </summary>
        public uint Harvest(int node)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused) return 0;
            if (world.NodeState(node, Input.HeldSlot) != OpResult.Ok) return 0;
            uint id = Send(SimRequest.Harvest(0, node, Input.HeldSlot));
            if (id != 0) LocalHarvestSwings++;
            return id;
        }

        /// <summary>
        /// Builds a recipe on a tile (doc 04 §1.1), turned <paramref name="rotation"/> quarters.
        /// Returns the request id, 0 where there is nothing to ask: it would be refused as things
        /// stand (<see cref="ClientWorld.PlaceState"/>).
        /// </summary>
        public uint PlaceConstruct(RecipeDef recipe, int tx, int tz, int rotation)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused) return 0;
            if (world.PlaceState(recipe, tx, tz, rotation) != OpResult.Ok) return 0;
            var building = recipe.Building;
            return Send(SimRequest.PlaceConstruct(0, building.Index, tx, tz, rotation % Math.Max(1, building.Rotations)));
        }

        /// <summary>
        /// Lays a line of a recipe (doc 04 §1.1 drag placement), each tile turned the same. Returns
        /// the request id, 0 where not one tile of it would be built (<see cref="ClientWorld.LineStates"/>).
        /// </summary>
        // --- the filters of sorters and splitters (doc 04 §2.3, §2.4, doc 09 §2.7) ------------------

        /// <summary>A little more than the server's reach, so the panel does not shut under a carrier the server still serves.</summary>
        private const float FilterLeaveSlack = 1.0f;

        /// <summary>Whether the local player may have the filters of a construct up where they stand.</summary>
        public bool CanConfigure(Construct? built, float slack = 0f)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || built == null || built.Def.FilterOutputs == 0) return false;
            float reach = _content.Balance.GetFloat("buildRange") + built.Def.Radius + slack;
            float dx = world.Local.X - built.CentreX, dz = world.Local.Z - built.CentreZ;
            return dx * dx + dz * dz <= reach * reach;
        }

        /// <summary>
        /// Puts the filters of a sorter or a splitter up (doc 09 §2.7 "configure"): what was open on
        /// the right of the inventory screen is shut, and the server sends what the construct does
        /// while they are up. Returns the request id, 0 if not here.
        /// </summary>
        public uint OpenFilters(Construct built)
        {
            var world = World;
            if (world == null || !CanConfigure(built)) return 0;
            if (world.OpenContainerId != 0) CloseContainer();
            world.ShopOpen = false;
            world.FilterTarget = built;
            world.FilterStatus = null;
            world.FilterPending.Clear();
            return Send(SimRequest.Configure(0, built.Tx, built.Tz, SimRequest.ConfigureOpen));
        }

        /// <summary>Takes the filters down; returns whether they were up.</summary>
        public bool CloseFilters()
        {
            var world = World;
            if (world?.FilterTarget == null) return false;
            var built = world.FilterTarget;
            CloseFiltersHere(world);
            if (State == ClientState.Joined) Send(SimRequest.Configure(0, built.Tx, built.Tz, SimRequest.ConfigureClose));
            return true;
        }

        private static void CloseFiltersHere(ClientWorld world)
        {
            world.FilterTarget = null;
            world.FilterStatus = null;
            world.FilterPending.Clear();
        }

        /// <summary>Sets a slot of an output of the open construct to a filter; the panel shows it at once. 0 if nothing was sent.</summary>
        public uint SetFilter(int output, int slot, MailFilter filter)
        {
            var world = World;
            var built = world?.FilterTarget;
            if (world == null || built == null || output < 0 || output >= built.Def.FilterOutputs || slot < 0 || slot >= built.Def.FilterSlots) return 0;
            if (world.ShownFilter(built, output, slot) == filter) return 0;
            uint id = Send(SimRequest.Configure(0, built.Tx, built.Tz, SimRequest.ConfigureSet, output, slot, filter.Encode()));
            if (id != 0) world.FilterPending[output * 16 + slot] = filter;
            return id;
        }

        /// <summary>The next (+1) or previous (−1) of <see cref="ClientWorld.FilterChoices"/> for an output's first slot. 0 if nothing was sent.</summary>
        public uint StepFilter(int output, int step)
        {
            var world = World;
            var built = world?.FilterTarget;
            // A depot's card after its stops is its driver: a click hires one.
            if (built != null && built.Def.HasConsole && output == built.Def.RouteStops) return step > 0 ? HireDriver() : 0;
            if (world == null || built == null || output < 0 || output >= built.Def.FilterOutputs) return 0;
            var choices = world.FilterChoices(built);
            int at = choices.IndexOf(world.ShownFilter(built, output, 0));
            int next = ((Math.Max(0, at) + step) % choices.Count + choices.Count) % choices.Count;
            return SetFilter(output, 0, choices[next]);
        }

        /// <summary>Hires the driver of the depot whose console is up (doc 04 §3.2), for this shift. 0 if nothing was sent.</summary>
        public uint HireDriver()
        {
            var world = World;
            var built = world?.FilterTarget;
            if (world == null || built == null || !built.Def.HasConsole || world.DriverAt(built) != null) return 0;
            return Send(SimRequest.Configure(0, built.Tx, built.Tz, SimRequest.ConfigureHire));
        }

        /// <summary>A number key while the filters are up: it steps that output on. Returns whether the key was theirs.</summary>
        public bool PressFilterKey(int key)
        {
            var built = World?.FilterTarget;
            if (built == null) return false;
            if (key >= 0 && key < built.Def.FilterOutputs) StepFilter(key, 1);
            else if (built.Def.HasConsole && key == built.Def.RouteStops) HireDriver();
            return true;
        }

        /// <summary>Pushes the belt on a tile (doc 04 §2.1): its segment runs faster for a moment. 0 if not sent.</summary>
        public uint PushBelt(int tx, int tz)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused) return 0;
            if (!Sim.Automation.BeltNetwork.IsBelt(world.Constructs.At(tx, tz))) return 0;
            return Send(SimRequest.PushBelt(0, tx, tz));
        }

        public uint PlaceLine(RecipeDef recipe, int x0, int z0, int x1, int z1, int rotation)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused) return 0;
            var states = new List<(int, int, OpResult)>();
            if (!world.LineStates(recipe, x0, z0, x1, z1, states) || !states.Exists(t => t.Item3 == OpResult.Ok)) return 0;
            var building = recipe.Building;
            return Send(SimRequest.PlaceLine(0, building.Index, x0, z0, x1, z1, rotation % Math.Max(1, building.Rotations)));
        }

        /// <summary>
        /// Takes a construct down for what it gives back (doc 04 §1.4). Returns the request id, 0
        /// where it would be refused as things stand (<see cref="ClientWorld.RemoveState"/>).
        /// </summary>
        public uint RemoveConstruct(Construct built)
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || world.Paused) return 0;
            if (world.RemoveState(built) != OpResult.Ok) return 0;
            return Send(SimRequest.RemoveConstruct(0, built.Tx, built.Tz));
        }

        /// <summary>
        /// The pause key (doc 09 §2.12): resumes a paused run; pauses one this client hosts alone.
        /// Returns the request id, 0 where the key does not pause (multiplayer never pauses).
        /// </summary>
        public uint TogglePause()
        {
            var world = World;
            if (State != ClientState.Joined || world == null) return 0;
            if (world.Paused) return SetPaused(false);
            return world.CanPause ? SetPaused(true) : 0;
        }

        /// <summary>
        /// The one "go" key: the host starts the run in the Lobby and leaves Results; everyone else
        /// in the Lobby, and everyone in Prep, toggles Ready. Returns the request id, 0 if the key
        /// does nothing right now.
        /// </summary>
        public uint PressGo()
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal) return 0;
            switch (world.Global.Phase)
            {
                case RunPhase.Lobby: return world.IsHost ? StartRun() : SetReady(!world.LocalReady);
                case RunPhase.Prep: return SetReady(!world.LocalReady);
                // A stopped clock never runs out, so the host ends the shift by hand.
                case RunPhase.Delivery: return world.ClockStopped && world.IsHost ? ContinueRun() : 0;
                case RunPhase.Results: return world.IsHost ? ContinueRun() : 0;
                default: return 0;
            }
        }

        // --- local tick -------------------------------------------------------------------------

        private void StepInput()
        {
            TickStarting?.Invoke();
            var world = World;
            if (State != ClientState.Joined || world == null) return;
            // Paused: the server simulates no input, so none is predicted or sent, and its clock stands.
            if (world.Paused) return;
            world.ServerTick += 1.0;
            // Doc 07 §8: the belts' items move here at the server's speed; only its events take them off an end.
            world.Belts.Step(SimConstants.TickDt, null);
            world.Pipes.Step(SimConstants.TickDt, arrive: false);
            // The tank of what the local player drives goes down here as on the server; its events set it right.
            if (world.HasLocal && world.LocalRide is { Def.Fuelled: true } driven && world.Local.RideSpeed > 0.1f)
                driven.Fuel = MathF.Max(0f, driven.Fuel - SimConstants.TickDt);
            if (!world.HasLocal) return;

            var input = new PlayerInput
            {
                Seq = _nextSeq++,
                MoveX = PlayerInput.QuantizeAxis(Input.MoveX),
                MoveZ = PlayerInput.QuantizeAxis(Input.MoveZ),
                Yaw = PlayerInput.QuantizeYaw(Input.YawRadians),
                Buttons = Input.Buttons,
                HeldSlot = Input.HeldSlot,
            };
            PlayerMovement.Step(ref world.Local, input, world.Map, world.LocalSpeedMult, _move, world.Constructs);
            _pendingInputs.Add(input);
            if (_pendingInputs.Count > MaxPendingInputs) _pendingInputs.RemoveAt(0);
            PredictAttacks(world, input);
            // The cards come down with the shutters, and for whoever walks off or goes down.
            if (world.FilterTarget != null && (world.Constructs.At(world.FilterTarget.Tx, world.FilterTarget.Tz) != world.FilterTarget || !CanConfigure(world.FilterTarget, FilterLeaveSlack)))
                CloseFilters();
            if (world.ShopOpen && !AtShop(world, ShopLeaveSlack))
            {
                world.ShopOpen = false;
                if (world.Global.Phase == RunPhase.Draft)
                    _draftKeysFrom = world.ServerTick + SimConstants.SecondsToTicks(ShopToDraftKeyGraceSeconds);
            }

            _inputsMsg.Inputs.Clear();
            for (int i = Math.Max(0, _pendingInputs.Count - InputsMsg.MaxInputs); i < _pendingInputs.Count; i++)
                _inputsMsg.Inputs.Add(_pendingInputs[i]);
            _writer.Reset();
            _inputsMsg.Write(_writer);
            _transport.Send(NetChannel.Unreliable, _writer.ToArray());
        }

        /// <summary>
        /// The swing and the shot are shown the moment the button goes down, on the same cooldowns
        /// the server keeps; what they hit is the server's to say.
        /// </summary>
        private void PredictAttacks(ClientWorld world, in PlayerInput input)
        {
            if (world.Local.Dead) return;
            if (input.Has(InputButtons.Attack) && input.Seq >= _hammerReadySeq)
            {
                _hammerReadySeq = input.Seq + _hammerCooldownTicks;
                LocalHammerSwings++;
            }
            if (input.Has(InputButtons.AltAttack) && input.Seq >= _cannonReadySeq && CannonAmmo > 0)
            {
                _cannonReadySeq = input.Seq + _cannonCooldownTicks;
                LocalCannonShots++;
            }
        }

        /// <summary>Packages the local player could fire from the mail cannon.</summary>
        public int CannonAmmo
        {
            get
            {
                var world = World;
                if (world == null || world.HotbarId == 0) return 0;
                return CountAmmo(world.Hotbar) + CountAmmo(world.Inventory);
            }
        }

        private int CountAmmo(GridContainer container)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Mail && _content.MailKinds[e.Stack.DefIndex].CannonAmmo) n += e.Stack.Count;
            return n;
        }

        // --- transport callbacks ----------------------------------------------------------------

        public void OnConnected()
        {
            if (State != ClientState.Connecting) return;
            _writer.Reset();
            new HelloMsg { ProtocolHash = Protocol.Hash(_content), Name = Name, Playtest = Playtest, StartShift = (byte)StartShift, Account = Account, Rank = (byte)Rank, StartWallet = (uint)StartWallet }.Write(_writer);
            _transport.Send(NetChannel.Reliable, _writer.ToArray());
            State = ClientState.Joining;
        }

        public void OnDisconnected()
        {
            if (State != ClientState.Rejected) State = ClientState.Disconnected;
        }

        public void OnData(NetChannel channel, byte[] data)
        {
            try
            {
                var r = new NetReader(data);
                var type = (MsgType)r.U8();
                switch (type)
                {
                    case MsgType.Welcome: HandleWelcome(WelcomeMsg.Read(r)); break;
                    case MsgType.Reject:
                        RejectReason = RejectMsg.Read(r);
                        State = ClientState.Rejected;
                        break;
                    case MsgType.Snapshot: HandleSnapshot(SnapshotMsg.Read(r)); break;
                    case MsgType.Events:
                        // The Welcome travels on a channel of its own: what overtakes it waits for it.
                        if (World == null && State == ClientState.Joining) _early.AddRange(EventsMsg.Read(r));
                        else foreach (var ev in EventsMsg.Read(r)) HandleEvent(ev);
                        break;
                    default: throw new NetFormatException($"unexpected message type {(byte)type}");
                }
            }
            catch (NetFormatException)
            {
                BadPackets++;
            }
        }

        private void HandleWelcome(WelcomeMsg m)
        {
            if (State != ClientState.Joining) return;
            // World check (doc 06 §5 step 5): the client builds the map itself and compares hashes.
            // A generated town is built here from the seed, as the server built it (doc 02 §2); the hashes must agree.
            var map = BuildMap(m.MapId, m.Seed);
            if (map == null || map.MapHash != m.MapHash || m.ChestContainerIds.Count != map.Chests.Count)
            {
                DisconnectDetail = $"map '{m.MapId}' does not match the server's";
                _transport.Close();
                State = ClientState.Disconnected;
                return;
            }
            var world = new ClientWorld(_content, map)
            {
                LocalId = m.PlayerId,
                Seed = m.Seed,
                IntakeContainerId = m.IntakeContainerId,
                ChestContainerIds = m.ChestContainerIds.ToArray(),
                Global = m.Global,
                GlobalTick = m.Tick,
                ServerTick = m.Tick,
                RaidTick = m.RaidTick,
                LocalRespawnTick = m.RespawnTick,
                LocalProtectedUntilTick = m.ProtectedUntilTick,
            };
            foreach (var p in m.Players) world.Players[p.Id] = new ClientPlayer(p.Id, p.Name, p.Slot);
            foreach (var b in m.Bags)
                world.Bags[b.ContainerId] = new ClientBag { ContainerId = b.ContainerId, Owner = b.Owner, X = b.X, Z = b.Z, DespawnTick = b.DespawnTick };
            foreach (var l in m.Loot) AddLoot(world, l);
            foreach (var perk in m.Perks)
                if (perk.Perk < _content.Perks.Count) world.AddPerk(perk.Holder, _content.Perks[perk.Perk]);
            foreach (byte perk in m.DraftOffer)
                if (perk < _content.Perks.Count) world.DraftOffer.Add(_content.Perks[perk]);
            world.RerollsLeft = m.RerollsLeft;
            foreach (var pick in m.DraftPicks)
                if (world.Players.TryGetValue(pick.Player, out var picker))
                {
                    picker.DraftPick = pick.Card;
                    picker.DraftPerk = pick.Perk < _content.Perks.Count ? _content.Perks[pick.Perk] : null;
                }
            foreach (byte item in m.ShopSpecials)
                if (item < _content.ShopItems.Count) world.ShopSpecials.Add(_content.ShopItems[item]);
            foreach (byte item in m.ShopOwned)
                if (item < _content.ShopItems.Count) world.ShopOwned.Add(_content.ShopItems[item]);
            foreach (var node in m.Nodes) world.Nodes.Set(node.Node, node.HitsLeft);
            foreach (var c in m.Constructs)
                if (c.Building < _content.Buildings.Count && map.InBounds(c.X, c.Y))
                {
                    var built = world.Constructs.Add(_content.Buildings[c.Building], c.X, c.Y, c.Rotation, c.Hp, c.ContainerId);
                    if (c.Filters != null) world.Constructs.SetFilters(built, c.Filters.ConvertAll(MailFilter.Decode));
                }
            foreach (var v in m.Vehicles)
                if (v.Def < _content.Vehicles.Count)
                    world.Vehicles[v.Id] = new ClientVehicle
                    {
                        Id = v.Id, Def = _content.Vehicles[v.Def], X = v.X, Z = v.Z, Heading = v.Heading, CargoContainer = v.CargoContainer, Rider = v.Rider,
                    };
            World = world;
            Input.YawRadians = map.PostOffice.SpawnYaw;
            Input.PitchRadians = 0f;
            world.Local.Yaw = PlayerInput.QuantizeYaw(map.PostOffice.SpawnYaw);
            world.HotbarId = m.Hotbar.ContainerId;
            world.InventoryId = m.Inventory.ContainerId;
            LoadContainer(m.Hotbar);
            LoadContainer(m.Inventory);
            State = ClientState.Joined;
            // The server has parked this player already; its event may have come before the Welcome.
            if ((Playtest & PlaytestFlags.AtMailbox) != 0 && world.Players.TryGetValue(m.PlayerId, out var self))
                FaceMailbox(world, PlaytestSpot.Mailbox(map, self.Slot));
            foreach (var ev in _early) HandleEvent(ev);
            _early.Clear();
        }

        /// <summary>The map of an id: a file's, or the seed's generated town built here (doc 02 §2). Null if there is none.</summary>
        private WorldMap? BuildMap(string mapId, uint seed)
        {
            if (!WorldGenerator.Generates(mapId)) return _content.Maps.TryGetValue(mapId, out var file) ? file : null;
            try { return WorldGenerator.Generate(_content, seed); }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>The host's new world (doc 06 §5): built here from its id and seed, its hash checked as at the Welcome.</summary>
        private void ReplaceWorld(ClientWorld world, WorldChangedEvent e)
        {
            var map = BuildMap(e.MapId, e.Seed);
            if (map == null || map.MapHash != e.MapHash || e.ChestContainerIds.Count != map.Chests.Count)
            {
                DisconnectDetail = $"map '{e.MapId}' does not match the server's";
                _transport.Close();
                State = ClientState.Disconnected;
                return;
            }
            world.ReplaceMap(map, e.Seed, e.ChestContainerIds);
            Input.YawRadians = map.PostOffice.SpawnYaw;
            Input.PitchRadians = 0f;
            WorldReplaced?.Invoke();
        }

        private static void AddLoot(ClientWorld world, LootDroppedEvent e)
        {
            var loot = new ClientLoot
            {
                Id = e.LootId, X = e.X, Z = e.Z, EnemyKind = e.EnemyKind, Mega = e.Mega, Coins = e.Coins, DroppedTick = world.ServerTick,
            };
            loot.Items.AddRange(e.Items);
            world.Loot[e.LootId] = loot;
        }

        /// <summary>
        /// The server has put the local player before a mailbox (<see cref="PlaytestFlags.AtMailbox"/>):
        /// stand there at once and look at the box, so that its prompt is up without a mouse move.
        /// </summary>
        private void FaceMailbox(ClientWorld world, DestinationDef dest)
        {
            PlaytestSpot.Stand(world.Map, dest, _move.Radius, out float x, out float z);
            var feet = new PlayerMoveState { X = x, Z = z, Y = world.Map.HeightAt(x, z), Grounded = true };
            var box = AimBox.Mailbox(world.Map, dest);
            ViewRay.Aim(feet, box.X, box.Y, box.Z, out Input.YawRadians, out Input.PitchRadians);
            feet.Yaw = PlayerInput.QuantizeYaw(Input.YawRadians);
            // Inputs sent from the old place would only walk the server's copy away from the box.
            _pendingInputs.Clear();
            world.Local = feet;
            world.ParkX = x;
            world.ParkZ = z;
            world.HasPark = true;
            // The mail for the box in hand, when there is some: holding the key is all that is left to do.
            var hotbar = world.Hotbar;
            for (int slot = 1; slot < hotbar.Cols; slot++)
            {
                var entry = hotbar.EntryAt(slot, 0);
                if (entry == null || entry.Stack.Kind != StackKind.Mail || entry.Stack.Address != dest.Address) continue;
                Input.HeldSlot = (byte)slot;
                break;
            }
        }

        private void HandleSnapshot(SnapshotMsg m)
        {
            var world = World;
            if (world == null || State != ClientState.Joined) return;
            if (SnapshotsReceived > 0 && m.Tick <= _lastSnapshotTick) return; // stale or duplicate
            _lastSnapshotTick = m.Tick;
            SnapshotsReceived++;

            // Clock: follow the newest snapshot, snapping only when the estimate has drifted far.
            double drift = m.Tick - world.ServerTick;
            if (Math.Abs(drift) > 6.0) world.ServerTick = m.Tick;
            else world.ServerTick += drift * 0.1;

            foreach (var snap in m.Players)
                if (world.Players.TryGetValue(snap.Id, out var player)) player.Push(m.Tick, snap);
            if (m.HasEnemies) ApplyEnemies(world, m);
            foreach (var (id, x, z, heading) in m.NpcVehicles)
                if (world.Vehicles.TryGetValue(new EntityId(id), out var driven) && driven.NpcDriving)
                {
                    driven.X = x;
                    driven.Z = z;
                    driven.Heading = heading;
                }
            if (m.HasGlobal)
            {
                world.Global = m.Global;
                world.GlobalTick = m.Tick;
            }
            if (m.HasOwn) Reconcile(world, m);
        }

        /// <summary>The enemy list is complete: whoever is not in it has fallen or fled.</summary>
        private void ApplyEnemies(ClientWorld world, SnapshotMsg m)
        {
            foreach (var snap in m.Enemies)
            {
                if (snap.Kind >= _content.Enemies.Count) continue;
                // Ids are 16 bits of a counter: after a wrap the same number can be somebody else.
                if (!world.Enemies.TryGetValue(snap.Id, out var enemy) || enemy.Def.Index != snap.Kind || enemy.Mega != snap.Mega)
                    world.Enemies[snap.Id] = enemy = new ClientEnemy(snap.Id, _content.Enemies[snap.Kind], snap.Mega);
                enemy.Push(m.Tick, snap);
            }
            if (world.Enemies.Count == m.Enemies.Count) return;
            _enemyScratch.Clear();
            foreach (var e in world.Enemies.Values) if (e.LastTick != m.Tick) _enemyScratch.Add(e.Id);
            foreach (ushort id in _enemyScratch) world.Enemies.Remove(id);
        }

        /// <summary>
        /// Prediction reconciliation (doc 06 §4): take the server's state, drop the inputs it has
        /// simulated, replay the rest. The distance the predicted position moves is the prediction error.
        /// </summary>
        private void Reconcile(ClientWorld world, SnapshotMsg m)
        {
            float beforeX = world.Local.X, beforeZ = world.Local.Z;
            bool hadLocal = world.HasLocal;
            ushort yaw = world.Local.Yaw;

            int acked = 0;
            while (acked < _pendingInputs.Count && _pendingInputs[acked].Seq <= m.AckSeq) acked++;
            if (acked > 0) _pendingInputs.RemoveRange(0, acked);

            world.Local = m.Own;
            world.Local.Yaw = yaw;
            world.LocalSpeedMult = m.OwnSpeedMult;
            if (m.OwnHp < world.LocalHp) world.LocalHurtTick = m.Tick;
            world.LocalHp = m.OwnHp;
            world.HasLocal = true;
            foreach (var input in _pendingInputs)
                PlayerMovement.Step(ref world.Local, input, world.Map, world.LocalSpeedMult, _move, world.Constructs);

            if (!hadLocal) return;
            double dx = world.Local.X - beforeX, dz = world.Local.Z - beforeZ;
            double errSq = dx * dx + dz * dz;
            _errorSumSq += errSq;
            _errorSamples++;
            double err = Math.Sqrt(errSq);
            if (err > PredictionErrorMax) PredictionErrorMax = err;
        }

        private void HandleEvent(SimEvent ev)
        {
            var world = World;
            if (world == null) return;
            switch (ev)
            {
                case WorldChangedEvent e:
                    ReplaceWorld(world, e);
                    break;
                case PlayerJoinedEvent e:
                    if (!world.Players.ContainsKey(e.Player)) world.Players[e.Player] = new ClientPlayer(e.Player, e.Name, e.Slot);
                    break;
                case PlayerLeftEvent e:
                    world.LastLeft = world.Players.TryGetValue(e.Player, out var leaver) ? leaver : null;
                    world.Players.Remove(e.Player);
                    // Their id is free for the next to join, who did not drop these.
                    foreach (var bag in world.Bags.Values)
                        if (bag.Owner == e.Player) bag.Owner = EntityId.None;
                    break;
                case PlayerAwayEvent e:
                    if (world.Players.TryGetValue(e.Player, out var away))
                    {
                        away.Away = true;
                        away.AwayUntilTick = e.UntilTick;
                    }
                    break;
                case PlayerBackEvent e:
                    if (world.Players.TryGetValue(e.Player, out var back)) back.Away = false;
                    break;
                case ContainerStateEvent e:
                    if (LoadContainer(e.State) != null && e.State.ContainerId != world.HotbarId && e.State.ContainerId != world.InventoryId)
                    {
                        if (world.OpenContainerId != 0 && world.OpenContainerId != e.State.ContainerId)
                            world.Containers.Remove(world.OpenContainerId);
                        world.OpenContainerId = e.State.ContainerId;
                        world.Prediction.Invalidate();
                        // One thing at a time on the right of the inventory screen.
                        world.ShopOpen = false;
                        if (world.FilterTarget != null) CloseFilters();
                    }
                    break;
                case ContainerDeltaEvent e:
                    ApplyDelta(world, e.Delta);
                    break;
                case ContainerClosedEvent e:
                    DropExternal(world, e.ContainerId);
                    break;
                case RequestResultEvent e:
                    if (_pendingRequests.TryGetValue(e.RequestId, out var request))
                    {
                        _pendingRequests.Remove(e.RequestId);
                        // Either way the guess ends here: an accepted move is in the mirrors by now
                        // (its deltas come first), a refused one falls back to what the server has.
                        world.Prediction.Resolve(e.RequestId);
                        if (request.Kind == RequestKind.CloseContainer && e.Result == OpResult.Ok) DropExternal(world, request.Container);
                        if (request.Kind == RequestKind.Buy && request.Count < _content.ShopItems.Count)
                            world.ShopPending.Remove(_content.ShopItems[request.Count]);
                        if (request.Kind == RequestKind.ConfigureConstruct && request.Count == SimRequest.ConfigureSet
                            && world.FilterPending.TryGetValue((int)request.EntryId, out var asked) && asked.Encode() == request.DestId)
                            world.FilterPending.Remove((int)request.EntryId);
                        if (request.Kind == RequestKind.ConfigureConstruct && request.Count == SimRequest.ConfigureOpen && e.Result != OpResult.Ok
                            && world.FilterTarget != null && world.FilterTarget.Tx == request.X && world.FilterTarget.Tz == request.Y)
                            CloseFiltersHere(world);
                        RequestCompleted?.Invoke(request, e.Result);
                    }
                    break;
                case PhaseChangedEvent e:
                {
                    var g = world.Global;
                    g.Shift = e.Shift; g.Phase = e.Phase; g.PhaseEndTick = e.PhaseEndTick; g.Quota = e.Quota; g.Outcome = e.Outcome;
                    world.Global = g;
                    world.GlobalTick = e.Tick;
                    // Every phase change clears the Ready flags (the snapshots confirm it a moment later).
                    foreach (var p in world.Players.Values) p.Ready = false;
                    if (e.Phase == RunPhase.Lobby)
                    {
                        world.LastSummary = null;
                        world.LocalKills = 0;
                        world.LocalLootCoins = 0;
                        world.ClearPerks();
                        world.ClearShop();
                        world.Nodes.Regrow(all: true);
                    }
                    if (e.Phase != RunPhase.Results) world.Results = null;
                    // The offer comes just before the phase it is for.
                    if (e.Phase != RunPhase.Draft) world.ClearDraft();
                    break;
                }
                case DraftOfferEvent e:
                    // New cards of one's own after a reroll leave the others' picks standing.
                    if (e.Reroll) world.DraftOffer.Clear();
                    else world.ClearDraft();
                    foreach (byte perk in e.Perks)
                        if (perk < _content.Perks.Count) world.DraftOffer.Add(_content.Perks[perk]);
                    world.RerollsLeft = e.RerollsLeft;
                    break;
                case PerkPickedEvent e:
                    if (e.Perk >= _content.Perks.Count) break;
                    world.AddPerk(e.Player, _content.Perks[e.Perk]);
                    if (e.Card != PerkPickedEvent.NoCard && world.Players.TryGetValue(e.Player, out var picked))
                    {
                        picked.DraftPick = e.Card;
                        picked.DraftPerk = _content.Perks[e.Perk];
                    }
                    break;
                case ShopOfferEvent e:
                    world.ShopSpecials.Clear();
                    foreach (byte item in e.Specials)
                        if (item < _content.ShopItems.Count) world.ShopSpecials.Add(_content.ShopItems[item]);
                    break;
                case ShopBoughtEvent e:
                    if (e.Item >= _content.ShopItems.Count) break;
                    var bought = _content.ShopItems[e.Item];
                    if (bought.OncePerRun && !world.ShopOwned.Contains(bought)) world.ShopOwned.Add(bought);
                    break;
                case NodeHarvestedEvent e:
                    world.Nodes.Set(e.Node, e.HitsLeft);
                    // Everybody else's swing is seen as their hammer's is; one's own was shown when it began.
                    if (e.Player != world.LocalId && world.Players.TryGetValue(e.Player, out var harvester))
                    {
                        harvester.AttackTick = (uint)Math.Max(0.0, world.ServerTick);
                        harvester.AttackWeapon = Weapon.Hammer;
                    }
                    break;
                case NodesRegrownEvent e:
                    world.Nodes.Regrow(e.All);
                    break;
                case ConstructPlacedEvent e:
                    if (e.Building < _content.Buildings.Count && world.Map.InBounds(e.X, e.Y))
                        world.Constructs.Add(_content.Buildings[e.Building], e.X, e.Y, e.Rotation, e.Hp, e.ContainerId);
                    break;
                case ConstructRemovedEvent e:
                {
                    var unbuilt = world.Constructs.At(e.X, e.Y);
                    world.LastRemoved = unbuilt;
                    if (unbuilt != null && unbuilt.ContainerId != 0) DropExternal(world, unbuilt.ContainerId);
                    world.Constructs.Remove(e.X, e.Y);
                    break;
                }
                case ConstructConfiguredEvent e:
                {
                    var set = world.Constructs.At(e.X, e.Y);
                    if (set == null || set.Tx != e.X || set.Tz != e.Y) break;
                    var filters = new List<MailFilter>(e.Filters.Count);
                    foreach (ushort f in e.Filters) filters.Add(MailFilter.Decode(f));
                    world.Constructs.SetFilters(set, filters);
                    break;
                }
                case InserterSwungEvent e:
                    if (world.Map.InBounds(e.X, e.Y)) world.InserterSwings[e.Y * world.Map.Cols + e.X] = (world.ServerTick, e.Kind);
                    break;
                case SorterStatusEvent e:
                    if (world.FilterTarget != null && world.FilterTarget.Tx == e.X && world.FilterTarget.Tz == e.Y) world.FilterStatus = e;
                    break;
                case ConstructDamagedEvent e:
                {
                    var hit = world.Constructs.At(e.X, e.Y);
                    if (hit != null) world.Constructs.SetHp(hit, e.Hp);
                    break;
                }
                case ConstructsClearedEvent:
                    foreach (var built in world.Constructs.All)
                        if (built.ContainerId != 0) DropExternal(world, built.ContainerId);
                    world.Belts.Clear();
                    world.Pipes.Clear();
                    world.Constructs.Clear();
                    break;
                case BeltStateEvent e:
                {
                    var belts = world.Belts;
                    var seg = world.Map.InBounds(e.X, e.Y) ? belts.SegmentByHead(e.Y * world.Map.Cols + e.X) : null;
                    if (seg == null) break;
                    var items = new List<Sim.Automation.BeltItem>(e.Items.Count);
                    foreach (var info in e.Items) items.Add(world.BeltItemOf(info));
                    belts.SetItems(seg, items);
                    _beltResyncAsked.Remove(seg.Head);
                    break;
                }
                case BeltItemAddedEvent e:
                {
                    var belts = world.Belts;
                    var seg = world.Map.InBounds(e.X, e.Y) ? belts.SegmentByHead(e.Y * world.Map.Cols + e.X) : null;
                    if (seg != null) belts.Add(seg, world.BeltItemOf(e.Item));
                    break;
                }
                case BeltItemMovedEvent e:
                {
                    var belts = world.Belts;
                    var seg = world.Map.InBounds(e.X, e.Y) ? belts.SegmentByHead(e.Y * world.Map.Cols + e.X) : null;
                    var item = belts.Find(e.MailId);
                    belts.RemoveMail(e.MailId);
                    if (seg == null || item == null) break;
                    var moved = item.Value;
                    moved.Pos = 0f;
                    moved.Lane = e.Lane;
                    belts.Add(seg, moved);
                    break;
                }
                case BeltItemRemovedEvent e:
                    world.Belts.RemoveMail(e.MailId);
                    break;
                case PipeCapsuleEvent e:
                {
                    var map = world.Map;
                    if (!map.InBounds(e.FromX, e.FromY) || !map.InBounds(e.OutX, e.OutY)) break;
                    var item = world.BeltItemOf(e.Item);
                    if (world.Pipes.Send(item.Mail, e.FromY * map.Cols + e.FromX, e.OutY * map.Cols + e.OutX) == null) world.PipeMismatches++;
                    break;
                }
                case PipeCapsuleRemovedEvent e:
                    world.Pipes.RemoveMail(e.MailId);
                    break;
                case BeltPushedEvent e:
                {
                    var seg = world.Map.InBounds(e.X, e.Y) ? world.Belts.SegmentByHead(e.Y * world.Map.Cols + e.X) : null;
                    if (seg != null) seg.PushTicks = Math.Max(seg.PushTicks, e.Ticks);
                    break;
                }
                case BeltChecksumEvent e:
                {
                    var belts = world.Belts;
                    var seg = world.Map.InBounds(e.X, e.Y) ? belts.SegmentByHead(e.Y * world.Map.Cols + e.X) : null;
                    if (seg != null && seg.Items.Count == e.Count && Sim.Automation.BeltNetwork.Checksum(seg) == e.Hash) break;
                    // Off: the segment whole, once until it comes (doc 06 §4).
                    world.BeltMismatches++;
                    if (_beltResyncAsked.Add(e.Y * world.Map.Cols + e.X)) Send(SimRequest.BeltResync(0, e.X, e.Y));
                    break;
                }
                case VehicleAddedEvent e:
                    if (e.Def < _content.Vehicles.Count)
                        world.Vehicles[e.Vehicle] = new ClientVehicle
                        {
                            Id = e.Vehicle, Def = _content.Vehicles[e.Def], X = e.X, Z = e.Z, Heading = e.Heading, CargoContainer = e.CargoContainer,
                        };
                    break;
                case VehicleRemovedEvent e:
                    if (world.Vehicles.TryGetValue(e.Vehicle, out var removed))
                    {
                        DropExternal(world, removed.CargoContainer);
                        world.Vehicles.Remove(e.Vehicle);
                    }
                    break;
                case PingEvent e:
                    world.AddPing(e);
                    break;
                case VehicleFuelEvent e:
                    if (world.Vehicles.TryGetValue(e.Vehicle, out var fuelled)) fuelled.Fuel = e.Seconds;
                    break;
                case DriverEvent e:
                {
                    int key = e.Y * world.Map.Cols + e.X;
                    // The truck it had and the one it has now: who drives each.
                    if (world.Drivers.TryGetValue(key, out var was) && was.Vehicle != e.Vehicle && world.Vehicles.TryGetValue(was.Vehicle, out var left))
                        left.NpcDriving = left.NpcAboard = false;
                    world.Drivers[key] = e;
                    if (world.Vehicles.TryGetValue(e.Vehicle, out var run))
                    {
                        run.NpcAboard = e.State != DriverState.None && e.State != DriverState.Waiting;
                        run.NpcDriving = e.State is DriverState.Out or DriverState.Unloading or DriverState.Returning or DriverState.Fleeing or DriverState.Stranded;
                    }
                    break;
                }
                case VehicleMountedEvent e:
                    if (world.Vehicles.TryGetValue(e.Vehicle, out var mounted)) mounted.Rider = e.Player;
                    break;
                case VehicleParkedEvent e:
                    if (world.Vehicles.TryGetValue(e.Vehicle, out var parked))
                    {
                        parked.Rider = EntityId.None;
                        parked.X = e.X;
                        parked.Z = e.Z;
                        parked.Heading = e.Heading;
                        if (e.Player == world.LocalId) DropExternal(world, parked.CargoContainer);
                    }
                    break;
                case VehiclesClearedEvent:
                    world.Drivers.Clear();
                    foreach (var v in world.Vehicles.Values) DropExternal(world, v.CargoContainer);
                    world.Vehicles.Clear();
                    break;
                case PausedChangedEvent e:
                    world.Paused = e.Paused;
                    if (e.Paused) world.ServerTick = e.Tick;
                    break;
                case ShiftSummaryEvent e:
                    world.LastSummary = e.Summary;
                    break;
                case RunResultsEvent e:
                    world.Results = e.Results;
                    break;
                case AttackEvent e:
                    if (world.Players.TryGetValue(e.Player, out var attacker))
                    {
                        attacker.AttackTick = (uint)Math.Max(0.0, world.ServerTick);
                        attacker.AttackWeapon = e.Weapon;
                    }
                    break;
                case EnemyDiedEvent e:
                    world.Enemies.Remove((ushort)e.Enemy.Counter);
                    if (e.Killer == world.LocalId) world.LocalKills++;
                    break;
                case LootDroppedEvent e:
                    AddLoot(world, e);
                    break;
                case LootTakenEvent e:
                    if (e.Player == world.LocalId) world.LocalLootCoins += e.Coins;
                    if (e.Gone) world.Loot.Remove(e.LootId);
                    else if (world.Loot.TryGetValue(e.LootId, out var left))
                    {
                        left.Coins = Math.Max(0, left.Coins - e.Coins);
                        foreach (var taken in e.Items)
                        {
                            int at = left.Items.FindIndex(i => i.Item == taken.Item);
                            if (at < 0) continue;
                            int rest = left.Items[at].Count - taken.Count;
                            if (rest <= 0) left.Items.RemoveAt(at);
                            else left.Items[at] = new LootItem { Item = taken.Item, Count = (byte)rest };
                        }
                    }
                    break;
                case LootGoneEvent e:
                    world.Loot.Remove(e.LootId);
                    break;
                case RaidChangedEvent e:
                {
                    var g = world.Global;
                    g.Raid = e.Raid;
                    g.RaidEdgeA = e.EdgeA;
                    g.RaidEdgeB = e.EdgeB;
                    world.Global = g;
                    world.RaidTick = e.Tick;
                    if (e.Raid == RaidState.None) world.Enemies.Clear();
                    break;
                }
                case PlayerDiedEvent e:
                    if (world.Players.TryGetValue(e.Player, out var fallen)) fallen.RespawnTick = e.RespawnTick;
                    if (e.Player == world.LocalId) world.LocalRespawnTick = e.RespawnTick;
                    break;
                case PlayerRespawnedEvent e:
                    if (e.Player == world.LocalId) world.LocalProtectedUntilTick = e.ProtectedUntilTick;
                    break;
                case PlayerParkedEvent e:
                    if (e.Destination < world.Map.Destinations.Count) FaceMailbox(world, world.Map.Destinations[e.Destination]);
                    break;
                case BagDroppedEvent e:
                    world.Bags[e.ContainerId] = new ClientBag { ContainerId = e.ContainerId, Owner = e.Owner, X = e.X, Z = e.Z, DespawnTick = e.DespawnTick };
                    break;
            }
            EventReceived?.Invoke(ev);
            // After the listeners: the feed still says whose bag it was.
            if (ev is BagGoneEvent gone) world.Bags.Remove(gone.ContainerId);
        }

        private GridContainer? LoadContainer(ContainerState state)
        {
            var world = World!;
            if (state.DefIndex >= _content.Containers.Count) { ContainerDesyncs++; return null; }
            var mirror = new GridContainer(state.ContainerId, _content.Containers[state.DefIndex]);
            mirror.Load(state);
            world.Containers[state.ContainerId] = mirror;
            world.Prediction.Invalidate();
            VerifyHash(mirror, state.Hash);
            return mirror;
        }

        private void ApplyDelta(ClientWorld world, ContainerDelta delta)
        {
            // A delta for a container we no longer view was already in flight when we closed it.
            if (!world.Containers.TryGetValue(delta.ContainerId, out var mirror)) return;
            world.Prediction.Invalidate();
            if (!mirror.Apply(delta)) { ContainerDesyncs++; return; }
            VerifyHash(mirror, delta.Hash);
        }

        private void VerifyHash(GridContainer mirror, uint serverHash)
        {
            if (mirror.ComputeHash() == serverHash) ContainerVersionsVerified++;
            else ContainerDesyncs++;
        }

        private static void DropExternal(ClientWorld world, uint containerId)
        {
            if (world.OpenContainerId != containerId) return;
            world.Containers.Remove(containerId);
            world.OpenContainerId = 0;
            world.Prediction.Invalidate();
        }
    }
}
