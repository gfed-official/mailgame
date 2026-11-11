using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    public sealed class SimSettings
    {
        public uint Seed = 1;
        public string MapId = "test_street";
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
            public readonly List<EntityId> Viewers = new List<EntityId>();
        }

        private struct PendingRequest
        {
            public EntityId Player;
            public SimRequest Request;
        }

        public ContentDb Content { get; }
        public WorldMap Map { get; }
        public SimSettings Settings { get; }
        public uint Tick { get; private set; }
        public Economy Economy { get; }
        public RunState Run { get; } = new RunState();
        public MailSpawner Spawner { get; }
        public GridContainer Intake { get; }
        public IReadOnlyList<Player> Players => _players;
        /// <summary>Events produced since the host last drained them.</summary>
        public List<SimEvent> Events { get; } = new List<SimEvent>();

        private readonly List<Player> _players = new List<Player>();
        private readonly Dictionary<uint, ContainerInfo> _containers = new Dictionary<uint, ContainerInfo>();
        private readonly List<PendingRequest> _requests = new List<PendingRequest>();
        private readonly EntityIdAllocator _entityIds = new EntityIdAllocator();
        private readonly EntryIdAllocator _entryIds = new EntryIdAllocator();
        private readonly MoveConstants _move;
        private readonly int _maxPlayers;
        private readonly float _serverInteractRange;
        private readonly int _rewindTicks;
        private readonly uint _minDeliverIntervalTicks;
        private readonly int _weightLight, _weightMedium, _weightHeavy;
        private readonly float _weightPerPoint, _weightFloor;
        private uint _nextContainerId = 1;

        public SimWorld(ContentDb content, SimSettings? settings = null)
        {
            Content = content;
            Settings = settings ?? new SimSettings();
            Map = content.Map(Settings.MapId);
            var b = content.Balance;
            _move = MoveConstants.From(b);
            _maxPlayers = b.GetInt("maxPlayers");
            _serverInteractRange = b.GetFloat("serverInteractRange");
            _rewindTicks = (int)SimConstants.SecondsToTicks(b.GetDouble("interactRewindSeconds"));
            _weightLight = b.GetInt("weightPointsLight");
            _weightMedium = b.GetInt("weightPointsMedium");
            _weightHeavy = b.GetInt("weightPointsHeavy");
            _weightPerPoint = b.GetFloat("weightSpeedPerPoint");
            _weightFloor = b.GetFloat("weightSpeedFloor");

            // The hold is timed on the client; the server only refuses deliveries that come
            // implausibly fast (hold minus 0.1 s of network slack).
            double hold = 0.4;
            foreach (var t in content.DestinationTypes) hold = Math.Min(hold, t.ManualInsertHold);
            _minDeliverIntervalTicks = SimConstants.SecondsToTicks(Math.Max(0, hold - 0.1));

            Economy = new Economy(b);
            Intake = CreateContainer("po_intake", EntityId.None, Map.PostOffice.IntakeX, Map.PostOffice.IntakeZ).Container;
            Spawner = new MailSpawner(content, Map, Intake, _entryIds, Settings.Seed);
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

        /// <summary>Adds a player on the spawn pad. Returns null if the server is full.</summary>
        public Player? AddPlayer(string name)
        {
            if (IsFull) return null;
            byte slot = 0;
            while (SlotTaken(slot)) slot++;
            var id = _entityIds.Allocate(EntityClass.Player);

            // Seats fan out around the pad so players do not spawn inside each other.
            float px = Map.PostOffice.SpawnPadX + (slot % 2) * 1.0f - 0.5f;
            float pz = Map.PostOffice.SpawnPadZ + (slot / 2) * 1.0f - 1.5f;
            var hotbar = CreateContainer("hotbar", id, 0, 0);
            var inventory = CreateContainer("player_inventory", id, 0, 0);
            hotbar.Viewers.Add(id);
            inventory.Viewers.Add(id);

            var player = new Player(id, name, slot, hotbar.Container, inventory.Container);
            player.Move = new PlayerMoveState { X = px, Z = pz, Y = Map.HeightAt(px, pz), Grounded = true };
            _players.Add(player);
            Events.Add(new PlayerJoinedEvent { Player = id, Name = name, Slot = slot });
            return player;
        }

        /// <summary>
        /// Removes a player. M0 has no death bags, so mail on the body goes back to the Intake
        /// instead of being lost (same rule doc 03 §3.3 uses for despawning world mail).
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
            _requests.RemoveAll(r => r.Player == id);
            Events.Add(new PlayerLeftEvent { Player = id });
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
        };

        // --- tick -----------------------------------------------------------------------------

        /// <summary>
        /// Advances one tick in the order of doc 06 §3: requests and inputs → player movement →
        /// mail spawn → economy → run state. Events accumulate in <see cref="Events"/>.
        /// </summary>
        public void Step()
        {
            Tick++;
            ApplyRequests();
            StepPlayers();
            StepMail();
            StepRun();
        }

        private void StepPlayers()
        {
            foreach (var p in _players)
            {
                p.RecordPosition();

                // Inputs are spent from a credit that refills at 1.15 per tick: this is the
                // "maxSpeed × 1.15 × dt" clamp of doc 06 §7 for a server that simulates movement
                // itself. A late burst catches up; a client running fast is held to +15 %.
                p.InputCredit = Math.Min(p.InputCredit + 1.15f, 4f);
                int budget = Math.Min(3, (int)p.InputCredit);
                while (budget > 0 && p.Inputs.Count > 0)
                {
                    var input = p.Inputs.Dequeue();
                    PlayerMovement.Step(ref p.Move, input, Map, p.SpeedMult, _move);
                    p.HeldSlot = input.HeldSlot < p.Hotbar.Cols ? input.HeldSlot : (byte)0;
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

        /// <summary>
        /// M0 stand-in for the run state machine (M1): Delivery of shift 1 on a repeating clock.
        /// The clock starts when the first player is present and re-samples the player count each restart.
        /// </summary>
        private void StepRun()
        {
            if (Run.Phase == RunPhase.Lobby)
            {
                if (_players.Count > 0) BeginDelivery();
            }
            else if (Run.Phase == RunPhase.Delivery && Tick >= Run.PhaseEndTick)
            {
                Spawner.EndShift();
                BeginDelivery();
            }
        }

        private void BeginDelivery()
        {
            var b = Content.Balance;
            Run.Phase = RunPhase.Delivery;
            Run.ClockIndex++;
            Run.PlayerCount = Math.Max(1, _players.Count);
            Run.Quota = QuotaFormula.Quota(b, Run.Shift, Run.PlayerCount);
            Run.PhaseStartTick = Tick;
            uint duration = SimConstants.SecondsToTicks(b.GetForShift("deliverySeconds", Run.Shift));
            Run.PhaseEndTick = Tick + duration;
            Economy.BeginShift();
            int spawnValue = (int)Math.Round(Run.Quota * Content.MailMix.SpawnOverhead);
            Spawner.BeginShift(Run.Shift, Tick + 1, duration, spawnValue);
            Events.Add(new PhaseChangedEvent
            {
                Shift = (byte)Run.Shift, Phase = Run.Phase, PhaseEndTick = Run.PhaseEndTick, Quota = Run.Quota,
            });
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

        private void RecomputeWeight(Player p)
        {
            int points = InventoryOps.WeightPoints(Content, p.Hotbar, _weightLight, _weightMedium, _weightHeavy)
                       + InventoryOps.WeightPoints(Content, p.Inventory, _weightLight, _weightMedium, _weightHeavy);
            p.SpeedMult = Math.Clamp(1f - _weightPerPoint * points, _weightFloor, 1f);
        }

        private bool InRange(Player p, ContainerInfo info)
        {
            if (!info.Owner.IsNone) return info.Owner == p.Id;
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
