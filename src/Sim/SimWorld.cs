using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    public sealed class SimSettings
    {
        public uint Seed = 1;
        /// <summary>A map of content/maps, or <see cref="WorldGenerator.SmallIsland"/> for the seed's own town.</summary>
        public string MapId = "test_street";
        /// <summary>
        /// Nobody to wait for (tests, soak, bots): the run starts as soon as a player is present,
        /// Prep is skipped, Results returns to the Lobby by itself and players may join at any time.
        /// </summary>
        public bool QuickStart;
        /// <summary>Night raids (doc 05 §3). Off for tests and tools that want the shifts to themselves.</summary>
        public bool Raids = true;
        /// <summary>
        /// The perk Draft between a Payday and the next shift (doc 01 §2.5). Off for tests and tools
        /// that want the run without perks; a <see cref="QuickStart"/> server has none either.
        /// </summary>
        public bool Draft = true;
        /// <summary>
        /// The starting kits (doc 03 §4.4): personal items into the pockets as the run starts, the
        /// host's team items into the chest by the Post Office and its vehicles by the counter. The
        /// game turns them on; tests and tools that count what is in the pockets leave them off.
        /// </summary>
        public bool StartingKits;
        /// <summary>
        /// The Generating phase (doc 01 §1) between the host's start and Prep, waiting for every
        /// client to have the world drawn. The game turns it on; tests and tools that start a run
        /// straight into Prep leave it off.
        /// </summary>
        public bool Generating;
    }

    /// <summary>The small global block replicated at 5 Hz (doc 06 §4: wallet, complaint, quota progress).</summary>
    public struct GlobalState
    {
        public byte Shift;
        public RunPhase Phase;
        public uint PhaseEndTick;
        public int Quota;
        public int ShiftEarnings;
        public int Wallet;
        public byte Complaint;
        public ushort Backlog;
        public ushort Deliveries;
        public ushort Misdeliveries;
        public RunOutcome Outcome;
        public EntityId Host;
        public RaidState Raid;
        /// <summary>The raid's ways in (<see cref="SimWorld.RaidEdgeA"/>, <see cref="SimWorld.RaidEdgeB"/>), −1 for none.</summary>
        public sbyte RaidEdgeA, RaidEdgeB;
        /// <summary>Playtest nit 10: the Delivery clock stands (<see cref="SimWorld.ClockStopped"/>).</summary>
        public bool ClockStopped;
        /// <summary>Playtest nit 13: a missed quota does not end the run (<see cref="SimWorld.QuotaWaived"/>).</summary>
        public bool QuotaWaived;
    }

    /// <summary>
    /// The authoritative simulation (doc 07 §3). Plain C#, no engine types: the server hosts it,
    /// tests drive it directly. One <see cref="Step"/> = one 30 Hz tick.
    /// </summary>
    public sealed partial class SimWorld
    {
        private sealed class ContainerInfo
        {
            public GridContainer Container = null!;
            /// <summary>Owning player for body containers; None for world containers.</summary>
            public EntityId Owner;
            public float X, Z;
            /// <summary>The vehicle whose cargo it is: only its rider opens it.</summary>
            public Vehicles.Vehicle? Vehicle;
            public readonly List<EntityId> Viewers = new List<EntityId>();
        }

        private struct PendingRequest
        {
            public EntityId Player;
            public SimRequest Request;
        }

        public ContentDb Content { get; }
        public WorldMap Map { get; private set; }
        public SimSettings Settings { get; }
        public uint Tick { get; private set; }
        public Economy Economy { get; }
        public RunState Run { get; } = new RunState();
        /// <summary>Solo pause (doc 01 §1): the tick is frozen until the host resumes or somebody joins.</summary>
        public bool Paused { get; private set; }
        public MailSpawner Spawner { get; private set; }
        public GridContainer Intake { get; }
        /// <summary>The containers of the map's chests, in the order of <see cref="WorldMap.Chests"/>.</summary>
        public IReadOnlyList<GridContainer> MapChests => _mapChests;
        /// <summary>The map's resource nodes and what each has left.</summary>
        public NodeField Nodes { get; private set; }
        /// <summary>What the carriers have built this run.</summary>
        public ConstructField Constructs { get; private set; }
        public IReadOnlyList<Player> Players => _players;
        /// <summary>Events produced since the host last drained them.</summary>
        public List<SimEvent> Events { get; } = new List<SimEvent>();

        private readonly List<Player> _players = new List<Player>();
        private readonly List<GridContainer> _mapChests = new List<GridContainer>();
        private readonly Dictionary<uint, ContainerInfo> _containers = new Dictionary<uint, ContainerInfo>();
        private readonly List<PendingRequest> _requests = new List<PendingRequest>();
        private readonly EntityIdAllocator _entityIds = new EntityIdAllocator();
        private readonly EntryIdAllocator _entryIds = new EntryIdAllocator();
        // The balance's numbers as the sim uses them; read again after a hot reload (ContentReloaded).
        private MoveConstants _move;
        private int _maxPlayers;
        private float _serverInteractRange;
        private float _serverShopRange;
        private int _rewindTicks;
        private float _minInsertHold;
        private int _weightLight, _weightMedium, _weightHeavy;
        private float _weightPerPoint, _weightFloor;
        private uint _nextContainerId = 1;

        public SimWorld(ContentDb content, SimSettings? settings = null)
        {
            Content = content;
            Settings = settings ?? new SimSettings();
            Map = WorldGenerator.Resolve(content, Settings.MapId, Settings.Seed);
            var b = content.Balance;
            ReadTunables();
            ResetCombatRng();

            Economy = new Economy(b, content.MailKinds.Count);
            Intake = CreateContainer("po_intake", EntityId.None, Map.PostOffice.IntakeX, Map.PostOffice.IntakeZ).Container;
            Spawner = new MailSpawner(content, Map, Intake, _entryIds, Settings.Seed);
            foreach (var chest in Map.Chests)
                _mapChests.Add(CreateContainer(chest.Container.Id, EntityId.None, chest.X, chest.Z).Container);
            Nodes = new NodeField(content, Map);
            Constructs = new ConstructField(Map);
        }

        private void ReadTunables()
        {
            var b = Content.Balance;
            _move = MoveConstants.From(Content);
            _combat = new CombatTuning(b);
            _maxPlayers = b.GetInt("maxPlayers");
            _serverInteractRange = b.GetFloat("serverInteractRange");
            _serverShopRange = b.GetFloat("serverShopRange");
            _serverBuildRange = b.GetFloat("serverBuildRange");
            _rewindTicks = (int)SimConstants.SecondsToTicks(b.GetDouble("interactRewindSeconds"));
            _weightLight = b.GetInt("weightPointsLight");
            _weightMedium = b.GetInt("weightPointsMedium");
            _weightHeavy = b.GetInt("weightPointsHeavy");
            _weightPerPoint = b.GetFloat("weightSpeedPerPoint");
            _weightFloor = b.GetFloat("weightSpeedFloor");

            // The hold is timed on the client; the server only refuses deliveries that come
            // implausibly fast (MinDeliverIntervalTicks).
            _minInsertHold = 0.4f;
            foreach (var t in Content.DestinationTypes) _minInsertHold = Math.Min(_minInsertHold, t.ManualInsertHold);
        }

        /// <summary>
        /// After <see cref="ContentDb.ReloadFrom"/> (hot reload, doc 07 §6): the balance's numbers
        /// are read again and the perks taken are applied again over them. A carrier's health
        /// keeps its share of the new maximum; what is under way (a Delivery's quota, a raid
        /// already scheduled) stays as it was sampled.
        /// </summary>
        public void ContentReloaded()
        {
            float oldMaxHp = _combat.PlayerHp;
            ReadTunables();
            Economy.Retune(Content.Balance);
            _teamStats.Clear();
            foreach (var perk in _teamPerks) _teamStats.Apply(perk);
            foreach (var p in _players)
            {
                p.Stats.Clear();
                foreach (var perk in p.Perks) p.Stats.Apply(perk);
                if (p.MaxHp == oldMaxHp && oldMaxHp > 0f)
                {
                    float share = p.Hp / p.MaxHp;
                    p.MaxHp = _combat.PlayerHp;
                    if (!p.Dead) p.Hp = Math.Max(1f, MathF.Round(p.MaxHp * share, 2));
                }
                RecomputeWeight(p);
            }
        }

        public MoveConstants MoveConstants => _move;
        public bool IsFull => _players.Count >= _maxPlayers;

        public Player? GetPlayer(EntityId id)
        {
            foreach (var p in _players) if (p.Id == id) return p;
            return null;
        }

        public GridContainer? GetContainer(uint id) => _containers.TryGetValue(id, out var info) ? info.Container : null;

        // --- players --------------------------------------------------------------------------

        /// <summary>
        /// Adds a player on the spawn pad. Returns null if the server is full. The player takes the
        /// lowest free seat and the id that goes with it (seat 0 is Player#1), so whoever comes
        /// after a guest has left is Player#2 again (playtest nit 11).
        /// </summary>
        public Player? AddPlayer(string name, ulong account = 0)
        {
            if (IsFull) return null;
            byte slot = 0;
            while (SlotTaken(slot)) slot++;
            var id = new EntityId(EntityClass.Player, slot + 1u);

            SeatPosition(slot, out float px, out float pz);
            var hotbar = CreateContainer("hotbar", id, 0, 0);
            var inventory = CreateContainer("player_inventory", id, 0, 0);
            hotbar.Viewers.Add(id);
            inventory.Viewers.Add(id);

            var player = new Player(id, name, slot, hotbar.Container, inventory.Container);
            player.Move = new PlayerMoveState
            {
                X = px, Z = pz, Y = Map.HeightAt(px, pz), Grounded = true, Yaw = PlayerInput.QuantizeYaw(Map.PostOffice.SpawnYaw),
            };
            player.Hp = player.MaxHp = _combat.PlayerHp;
            player.JoinOrder = _nextJoinOrder++;
            player.Account = account;
            player.Kit = Content.DefaultKit;
            _players.Add(player);
            UpdateHost();
            Events.Add(new PlayerJoinedEvent { Player = id, Name = name, Slot = slot });
            // Doc 06 §6: who joins in Prep spawns with the starting kit's personal items.
            if (Run.Phase != RunPhase.Lobby) GivePersonalKit(player);
            if (_players.Count != 1) SetPaused(false);
            return player;
        }

        /// <summary>
        /// Removes a player at once: who leaves the Lobby, and whoever the server lets go without
        /// the grace of <see cref="Disconnect"/>. No bag is dropped: mail on the body goes back to
        /// the Intake instead of being lost (the rule doc 03 §3.3 has for despawning world mail).
        /// </summary>
        public void RemovePlayer(EntityId id)
        {
            var player = GetPlayer(id);
            if (player == null) return;
            CloseExternal(player, notify: false);
            foreach (var c in new[] { player.Hotbar, player.Inventory })
            {
                foreach (var e in c.Entries)
                    if (e.Stack.Kind == StackKind.Mail)
                        foreach (var m in e.Stack.Mail) Spawner.ReturnToIntake(m);
                _containers.Remove(c.Id);
            }
            CommitContainer(Intake.Id);
            _players.Remove(player);
            UpdateHost();
            _requests.RemoveAll(r => r.Player == id);
            // The id is free for the next to join: nothing of the fight may still name the one who left.
            ForgetInCombat(id);
            Events.Add(new PlayerLeftEvent { Player = id });
            if (_players.Count != 1) SetPaused(false);
        }

        /// <summary>Buffers one input for the next ticks. Old or duplicate sequence numbers are ignored.</summary>
        public void QueueInput(EntityId playerId, in PlayerInput input)
        {
            var p = GetPlayer(playerId);
            if (p == null) return;
            if (p.LastQueuedSeq != 0 && input.Seq <= p.LastQueuedSeq) return;
            p.LastQueuedSeq = input.Seq;
            p.Inputs.Enqueue(input);
            while (p.Inputs.Count > Player.MaxQueuedInputs) p.Inputs.Dequeue();
        }

        /// <summary>Queues a reliable request; it is validated and applied in arrival order on the next tick.</summary>
        public void Submit(EntityId playerId, in SimRequest request) =>
            _requests.Add(new PendingRequest { Player = playerId, Request = request });

        /// <summary>Full states of the containers a player always sees (their body containers).</summary>
        public List<ContainerState> GetOwnContainerStates(Player player) =>
            new List<ContainerState> { player.Hotbar.Snapshot(), player.Inventory.Snapshot() };

        public GlobalState GetGlobalState() => new GlobalState
        {
            Shift = (byte)Run.Shift,
            Phase = Run.Phase,
            PhaseEndTick = Run.PhaseEndTick,
            Quota = Run.Quota,
            ShiftEarnings = Economy.ShiftEarnings,
            Wallet = Economy.Wallet,
            Complaint = (byte)Economy.Complaint,
            Backlog = (ushort)Math.Min(ushort.MaxValue, Spawner.BacklogCount),
            Deliveries = (ushort)Math.Min(ushort.MaxValue, Economy.ShiftDeliveries),
            Misdeliveries = (ushort)Math.Min(ushort.MaxValue, Economy.ShiftMisdeliveries),
            Outcome = Run.Outcome,
            Host = Run.Host,
            Raid = Raid,
            RaidEdgeA = (sbyte)RaidEdgeA,
            RaidEdgeB = (sbyte)RaidEdgeB,
            ClockStopped = ClockStopped,
            QuotaWaived = QuotaWaived,
        };

        // --- tick -----------------------------------------------------------------------------

        /// <summary>
        /// Advances one tick in the order of doc 06 §3: requests and inputs → player movement →
        /// the fight → mail spawn → economy → run state. Events accumulate in <see cref="Events"/>. While
        /// <see cref="Paused"/> nothing advances, the tick counter included.
        /// </summary>
        public void Step()
        {
            if (Paused)
            {
                ApplyPauseRequests();
                if (Paused) return;
            }
            Tick++;
            ApplyRequests();
            StepPlayers();
            StepVehicles();
            StepCombat();
            StepBelts();
            StepMail();
            StepAway();
            StepRun();
        }

        private void StepPlayers()
        {
            foreach (var p in _players)
            {
                p.RecordPosition();
                if (p.BoostUntilTick != 0 && Tick >= p.BoostUntilTick)
                {
                    p.BoostUntilTick = 0;
                    RecomputeWeight(p);
                }
                if (p.Away)
                {
                    // Nobody steers them: they come to rest where they stood.
                    PlayerMovement.Step(ref p.Move, new PlayerInput { Yaw = p.Move.Yaw }, Map, p.SpeedMult, _move, Constructs);
                    continue;
                }

                // Inputs are spent from a credit that refills at 1.15 per tick: this is the
                // "maxSpeed × 1.15 × dt" clamp of doc 06 §7 for a server that simulates movement
                // itself. A late burst catches up; a client running fast is held to +15 %.
                p.InputCredit = Math.Min(p.InputCredit + 1.15f, 4f);
                int budget = Math.Min(3, (int)p.InputCredit);
                while (budget > 0 && p.Inputs.Count > 0)
                {
                    var input = p.Inputs.Dequeue();
                    PlayerMovement.Step(ref p.Move, input, Map, p.SpeedMult, _move, Constructs);
                    p.HeldSlot = input.HeldSlot < p.Hotbar.Cols ? input.HeldSlot : (byte)0;
                    HandleAttackInput(p, input);
                    p.LastProcessedSeq = input.Seq;
                    p.InputCredit -= 1f;
                    budget--;
                }

                if (p.OpenContainer != 0 && !InRange(p, _containers[p.OpenContainer]))
                    CloseExternal(p, notify: true);
            }
        }

        private void StepMail()
        {
            if (Run.Phase != RunPhase.Delivery) return;
            uint before = Spawner.MailSpawnedTotal;
            int backlogComplaint = 0;
            int spawned = Spawner.Step(Tick, ref backlogComplaint);
            if (backlogComplaint > 0) Economy.AddComplaint(backlogComplaint);
            CommitContainer(Intake.Id);
            if (spawned > 0)
            {
                Events.Add(new MailSpawnedEvent
                {
                    FirstMailId = before + 1,
                    Count = (ushort)spawned,
                    Backlog = (ushort)Math.Min(ushort.MaxValue, Spawner.BacklogCount),
                });
            }
            Economy.StepDelivery();
        }

        // --- containers -----------------------------------------------------------------------

        private ContainerInfo CreateContainer(string defId, EntityId owner, float x, float z)
        {
            var info = new ContainerInfo
            {
                Container = new GridContainer(_nextContainerId++, Content.Container(defId)),
                Owner = owner, X = x, Z = z,
            };
            _containers[info.Container.Id] = info;
            return info;
        }

        /// <summary>Adds a world container (tests and, later, constructs).</summary>
        public GridContainer AddWorldContainer(string defId, float x, float z) =>
            CreateContainer(defId, EntityId.None, x, z).Container;

        /// <summary>Places a loose stack into a container outside the request path (tests, kits).</summary>
        public bool Give(GridContainer container, ItemStack stack)
        {
            bool ok = InventoryOps.AutoPlace(Content, container, stack, _entryIds);
            CommitContainer(container.Id);
            return ok;
        }

        /// <summary>Closes a mutation: bumps the version and sends the delta to every viewer.</summary>
        private void CommitContainer(uint containerId)
        {
            if (!_containers.TryGetValue(containerId, out var info)) return;
            var delta = info.Container.Commit();
            if (delta == null) return;
            foreach (var viewer in info.Viewers)
                Events.Add(new ContainerDeltaEvent { Audience = viewer, Delta = delta });
            if (!info.Owner.IsNone)
            {
                var owner = GetPlayer(info.Owner);
                if (owner != null) RecomputeWeight(owner);
            }
        }

        /// <summary>The carrier's speed: what they carry slows them, their perks and a fresh delivery speed them up.</summary>
        private void RecomputeWeight(Player p)
        {
            int points = InventoryOps.WeightPoints(Content, p.Hotbar, _weightLight, _weightMedium, _weightHeavy)
                       + InventoryOps.WeightPoints(Content, p.Inventory, _weightLight, _weightMedium, _weightHeavy);
            float weight = Math.Clamp(1f - StatFor(p, Stat.WeightPenalty, _weightPerPoint * points), _weightFloor, 1f);
            float speed = StatFor(p, Stat.PlayerSpeed, 1f);
            if (Tick < p.BoostUntilTick) speed *= 1f + StatFor(p, Stat.DeliveryBoost, 0f);
            p.SpeedMult = weight * Math.Max(0f, speed);
        }

        private bool InRange(Player p, ContainerInfo info)
        {
            if (!info.Owner.IsNone) return info.Owner == p.Id;
            // Doc 03 §2 Bike: the rider reaches the cargo from the saddle, and only the rider.
            if (info.Vehicle != null) return info.Vehicle.Rider == p.Id && p.Move.Riding;
            float dx = p.Move.X - info.X, dz = p.Move.Z - info.Z;
            return dx * dx + dz * dz <= _serverInteractRange * _serverInteractRange;
        }

        private void CloseExternal(Player p, bool notify)
        {
            if (p.OpenContainer == 0) return;
            if (_containers.TryGetValue(p.OpenContainer, out var info)) info.Viewers.Remove(p.Id);
            if (notify) Events.Add(new ContainerClosedEvent { Audience = p.Id, ContainerId = p.OpenContainer });
            p.OpenContainer = 0;
        }

        private bool SlotTaken(byte slot)
        {
            foreach (var p in _players) if (p.Slot == slot) return true;
            return false;
        }
    }
}
