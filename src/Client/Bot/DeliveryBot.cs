using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Bot
{
    /// <summary>
    /// Scripted player (doc 06 §9): walks to the Intake, takes a few stacks, walks each to its
    /// mailbox and delivers, forever. It only uses what a real client has — the ClientWorld mirror,
    /// movement input and requests — so it exercises the whole network path.
    /// </summary>
    public sealed class DeliveryBot
    {
        private enum Step { ToIntake, OpenIntake, Load, CloseIntake, PickTarget, ToMailbox, Hold, Deliver }

        /// <summary>Stacks to carry per trip.</summary>
        public int MaxStacksPerTrip = 4;
        public bool Sprint = true;
        /// <summary>Deliver every Nth stack to a wrong mailbox (0 = never); exercises the penalty path.</summary>
        public int MisdeliverEvery;
        /// <summary>Press "go" between shifts: start the run and leave Results as host, ready up in Prep.</summary>
        public bool AutoRun = true;

        public int StacksDelivered { get; private set; }
        public int StacksMisdelivered { get; private set; }
        public int RequestsRejected { get; private set; }
        public OpResult LastRejection { get; private set; }

        private readonly ClientRuntime _client;
        private readonly List<(float X, float Z)> _path = new List<(float, float)>();
        private readonly HashSet<uint> _skip = new HashSet<uint>();
        private Step _step = Step.ToIntake;
        private int _waypoint;
        private bool _hasPath;
        private uint _awaiting;
        private OpResult _lastResult;
        private int _holdTicks;
        private int _stuckTicks;
        private float _stuckX, _stuckZ;
        private uint _targetEntry;
        private uint _targetContainer;
        private DestinationDef? _targetDest;
        private bool _targetIsWrong;
        private int _stackCounter;
        private int _goCooldown;

        public DeliveryBot(ClientRuntime client)
        {
            _client = client;
            client.RequestCompleted += OnRequestCompleted;
        }

        private void OnRequestCompleted(SimRequest request, OpResult result)
        {
            if (request.RequestId != _awaiting) return;
            _awaiting = 0;
            _lastResult = result;
            // A request that crosses a phase change (the shift ended while the hand was in the mailbox) is not a fault.
            if (result != OpResult.Ok && result != OpResult.WrongPhase)
            {
                RequestsRejected++;
                LastRejection = result;
            }
        }

        /// <summary>Decides this tick's input and requests. Call once per client tick, before the client ticks.</summary>
        public void Tick()
        {
            var world = _client.World;
            _client.Input.MoveX = 0f;
            _client.Input.MoveZ = 0f;
            _client.Input.Buttons = InputButtons.None;
            if (_client.State != ClientState.Joined || world == null || !world.HasLocal) return;
            if (_awaiting != 0 || world.Paused) return;

            if (world.Global.Phase != RunPhase.Delivery)
            {
                StepOutsideDelivery(world);
                return;
            }

            var po = world.Map.PostOffice;
            float interact = world.Content.Balance.GetFloat("interactRange");
            switch (_step)
            {
                case Step.ToIntake:
                    if (WalkTo(world, po.IntakeX, po.IntakeZ, interact - 0.5f))
                    {
                        LookAt(world, AimBox.Intake(world.Map));
                        _awaiting = _client.OpenContainer(world.IntakeContainerId);
                        _step = Step.OpenIntake;
                    }
                    break;

                case Step.OpenIntake:
                    _skip.Clear();
                    _step = _lastResult == OpResult.Ok ? Step.Load : Step.ToIntake;
                    break;

                case Step.Load:
                {
                    var intake = world.OpenContainer;
                    if (intake == null) { _step = Step.ToIntake; break; }
                    int carried = world.Hotbar.EntryCount + world.Inventory.EntryCount;
                    var next = carried < MaxStacksPerTrip ? FirstEntry(intake, _skip) : null;
                    if (next == null)
                    {
                        // Nothing (more) to take: leave if we carry something, otherwise wait for a batch.
                        if (carried > 0)
                        {
                            _awaiting = _client.CloseContainer();
                            _step = Step.CloseIntake;
                        }
                        break;
                    }
                    _skip.Add(next.EntryId);
                    _awaiting = _client.QuickMove(intake.Id, next.EntryId);
                    break;
                }

                case Step.CloseIntake:
                    _step = Step.PickTarget;
                    break;

                case Step.PickTarget:
                {
                    var entry = FirstEntry(world.Hotbar, null);
                    var container = world.Hotbar;
                    if (entry == null) { entry = FirstEntry(world.Inventory, null); container = world.Inventory; }
                    if (entry == null) { _step = Step.ToIntake; _hasPath = false; break; }
                    var dest = world.Map.FindDestination(entry.Stack.Address);
                    _targetIsWrong = MisdeliverEvery > 0 && ++_stackCounter % MisdeliverEvery == 0;
                    if (dest != null && _targetIsWrong)
                        dest = world.Map.Destinations[(dest.Id + 1) % world.Map.Destinations.Count];
                    if (dest == null) { _step = Step.ToIntake; break; }
                    _targetEntry = entry.EntryId;
                    _targetContainer = container.Id;
                    _targetDest = dest;
                    // Select the stack like a player would (slot 0 is hands), so the prompt shows its address check.
                    _client.Input.HeldSlot = ReferenceEquals(container, world.Hotbar) ? (byte)entry.X : (byte)0;
                    _hasPath = false;
                    _step = Step.ToMailbox;
                    break;
                }

                case Step.ToMailbox:
                    if (WalkTo(world, _targetDest!.MailboxX, _targetDest.MailboxZ, interact - 1f))
                    {
                        LookAt(world, AimBox.Mailbox(world.Map, _targetDest));
                        // Manual insert is a hold (doc 03 §2.1), timed on the client.
                        _holdTicks = (int)Math.Ceiling(_targetDest.Type.ManualInsertHold * 30f) + 1;
                        _step = Step.Hold;
                    }
                    break;

                case Step.Hold:
                    _client.Input.Buttons = InputButtons.Interact;
                    if (--_holdTicks > 0) break;
                    _awaiting = _client.Deliver(_targetContainer, _targetEntry, _targetDest!.Id);
                    _step = Step.Deliver;
                    break;

                case Step.Deliver:
                    if (_lastResult == OpResult.Ok)
                    {
                        if (_targetIsWrong) StacksMisdelivered++;
                        else StacksDelivered++;
                        _step = Step.PickTarget;
                    }
                    else if (_lastResult == OpResult.TooFast || _lastResult == OpResult.OutOfRange)
                    {
                        _hasPath = false;
                        _step = Step.ToMailbox;
                    }
                    else _step = Step.PickTarget;
                    break;
            }
        }

        /// <summary>
        /// Between shifts there is nothing to deliver: the bot presses "go" when that moves the run
        /// along (start as host, ready in Prep, continue as host) and otherwise stands still. Mail it
        /// still carries is taken to its mailbox when the next Delivery starts.
        /// </summary>
        private void StepOutsideDelivery(ClientWorld world)
        {
            _step = Step.PickTarget;
            _hasPath = false;
            if (world.OpenContainerId != 0)
            {
                _awaiting = _client.CloseContainer();
                return;
            }
            if (_goCooldown > 0) { _goCooldown--; return; }
            if (!AutoRun) return;

            bool go;
            switch (world.Global.Phase)
            {
                case RunPhase.Lobby: go = world.IsHost || !world.LocalReady; break;
                case RunPhase.Prep: go = !world.LocalReady; break;
                case RunPhase.Results: go = world.IsHost; break;
                default: go = false; break;
            }
            if (!go) return;
            _awaiting = _client.PressGo();
            // Ready and the phase arrive by snapshot; do not press again before they can have.
            _goCooldown = 15;
        }

        private static GridEntry? FirstEntry(GridContainer container, HashSet<uint>? skip)
        {
            GridEntry? best = null;
            foreach (var e in container.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                if (skip != null && skip.Contains(e.EntryId)) continue;
                if (best == null || e.EntryId < best.EntryId) best = e;
            }
            return best;
        }

        /// <summary>Turns the view onto an interactable, as a player has to before the prompt appears.</summary>
        private void LookAt(ClientWorld world, in AimBox box) =>
            ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out _client.Input.YawRadians, out _client.Input.PitchRadians);

        /// <summary>Steers along a tile path toward a point. Returns true once within <paramref name="stopDistance"/>.</summary>
        private bool WalkTo(ClientWorld world, float x, float z, float stopDistance)
        {
            float px = world.Local.X, pz = world.Local.Z;
            float dx = x - px, dz = z - pz;
            if (dx * dx + dz * dz <= stopDistance * stopDistance)
            {
                _hasPath = false;
                return true;
            }

            // Re-plan when there is no path yet or we have not moved for a second.
            float moved = MathF.Abs(px - _stuckX) + MathF.Abs(pz - _stuckZ);
            if (moved > 0.05f) { _stuckTicks = 0; _stuckX = px; _stuckZ = pz; }
            else _stuckTicks++;
            if (!_hasPath || _stuckTicks > 30)
            {
                _hasPath = TilePathfinder.Find(world.Map, px, pz, x, z, _path);
                _waypoint = 0;
                _stuckTicks = 0;
                if (!_hasPath) return false;
            }

            while (_waypoint < _path.Count - 1)
            {
                float wx = _path[_waypoint].X - px, wz = _path[_waypoint].Z - pz;
                if (wx * wx + wz * wz > 0.6f * 0.6f) break;
                _waypoint++;
            }
            float tx = _path[_waypoint].X - px, tz = _path[_waypoint].Z - pz;
            float len = MathF.Sqrt(tx * tx + tz * tz);
            if (len < 1e-4f) return false;
            _client.Input.MoveX = tx / len;
            _client.Input.MoveZ = tz / len;
            _client.Input.YawRadians = MathF.Atan2(-tx, -tz);
            _client.Input.PitchRadians = 0f;
            if (Sprint) _client.Input.Buttons = InputButtons.Sprint;
            return false;
        }
    }
}
