using System;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    public enum InteractKind : byte
    {
        None,
        OpenIntake,
        CloseIntake,
        /// <summary>At a mailbox (doc 09 §3.1).</summary>
        Deliver,
        /// <summary>At one of the map's chests.</summary>
        OpenChest,
        CloseChest,
        /// <summary>At a death bag (doc 01 §2.2).</summary>
        OpenBag,
        CloseBag,
        /// <summary>At the shop counter of the Post Office (doc 09 §2.8).</summary>
        OpenShop,
        CloseShop,
        /// <summary>At a tree, a bush or a boulder that has something left (doc 02 §3.9).</summary>
        Harvest,
        /// <summary>At a parked vehicle nobody rides (doc 04 §3.1): get on.</summary>
        Mount,
        /// <summary>At a vehicle that burns fuel with an Oil Can in the hand (doc 04 §3.1): fill its tank.</summary>
        Refuel,
        /// <summary>In the saddle with nothing else in view: the vehicle's cargo rack.</summary>
        OpenRack,
        CloseRack,
        /// <summary>At a belt, a splitter or a merger (doc 04 §2.1): held, its segment runs faster.</summary>
        PushBelt,
        /// <summary>At an Address Sorter, or a splitter in Build mode (doc 09 §2.7 "configure"): its filters up.</summary>
        Configure,
        /// <summary>Its filters are up: the key takes them down.</summary>
        CloseFilters,
        /// <summary>At a turret (doc 05 §4 "Player-operated"): take its controls.</summary>
        OperateTurret,
        /// <summary>At a turret's controls: the key lets go of them.</summary>
        LeaveTurret,
        /// <summary>At a ruin in Prep (doc 05 §5): rebuild it for half its cost.</summary>
        RebuildRuin,
        /// <summary>At a ruin in Delivery: clear it away.</summary>
        ClearRuin,
    }

    /// <summary>What the interact prompt shows right now (doc 09 §2.4).</summary>
    public struct InteractPrompt
    {
        public InteractKind Kind;
        /// <summary>Deliver: the mailbox in range.</summary>
        public DestinationDef? Destination;
        /// <summary>Deliver: the stack in the selected hotbar slot, null for hands or an empty slot.</summary>
        public GridEntry? Stack;
        public bool AddressMatches;
        /// <summary>False when this mailbox type never takes the selected kind.</summary>
        public bool KindAccepted;
        /// <summary>Deliver: 0..1 progress of the hold.</summary>
        public float HoldProgress;
        /// <summary>Deliver: carried stacks addressed to this mailbox ("deliver all matching").</summary>
        public int MatchingStacks;
        /// <summary>Deliver: mailboxes take mail only during the Delivery phase.</summary>
        public bool MailboxClosed;
        /// <summary>OpenChest / CloseChest: the chest of the map in range.</summary>
        public MapChestDef? Chest;
        /// <summary>OpenChest / CloseChest: the chest the carriers built in range, where it is not one of the map's.</summary>
        public Construct? Built;
        /// <summary>OpenBag / CloseBag: the bag in range.</summary>
        public ClientBag? Bag;
        /// <summary>OpenShop: the counter serves only in Prep and at Payday.</summary>
        public bool ShopClosed;
        /// <summary>Harvest: the node in range, and which prop of the map it is.</summary>
        public NodeDef? Node;
        public int NodeIndex;
        /// <summary>Harvest: Ok, or what stands in the way of a hit (NeedsTool, DoesNotFit).</summary>
        public OpResult NodeState;
        /// <summary>Harvest: units a hit gives the hand as it is, and the hits the node has left.</summary>
        public int NodeYield;
        public int NodeHitsLeft;
        /// <summary>Mount: the vehicle in range. OpenRack / CloseRack: the one ridden.</summary>
        public ClientVehicle? Vehicle;
        /// <summary>PushBelt: interact is held on it.</summary>
        public bool Pushing;
    }

    /// <summary>
    /// Turns "the player is standing here and holds interact" into requests (doc 09 §3.1): open or
    /// close the Intake, hold to deliver the selected stack, deliver every matching stack. Godot-free
    /// so the HUD, the input layer and tests share one definition of the prompt.
    /// </summary>
    public sealed class InteractionController
    {
        /// <summary>The range is measured on the ground; the eyes are above it, so the ray may be this much longer.</summary>
        private const float AimSlack = 1.0f;
        /// <summary>A counter of the Post Office is meant by whoever faces it within this much to either side (40°).</summary>
        public const float FacingCone = 0.7f;
        /// <summary>How long a press of interact waits for something to act on.</summary>
        private const double PressLatchSeconds = 0.25;

        public InteractPrompt Prompt { get; private set; }

        private readonly ClientRuntime _client;
        private uint _awaiting;
        private bool _wasHeld;
        private double _pressLatch;
        private double _hold;
        private int _deliverAllDest = -1;
        private int _harvestNode = -1;
        /// <summary>A push lasts 0.7 s on the server: one held is asked again this often.</summary>
        public const double PushRepeatSeconds = 0.4;
        private double _pushAgain;

        /// <summary>True while Build mode is up: E at a splitter then sets its filters instead of pushing it.</summary>
        public Func<bool>? Building;

        public InteractionController(ClientRuntime client)
        {
            _client = client;
            client.RequestCompleted += (request, _) => { if (request.RequestId == _awaiting) _awaiting = 0; };
        }

        /// <summary>
        /// Call once per frame. <paramref name="deliverAllPressed"/> is the edge of the "deliver all
        /// matching" action. <paramref name="interactTapped"/> reports a press of interact seen as an
        /// input event since the last call: a tap shorter than a frame never shows in
        /// <paramref name="interactHeld"/>, and it must still open the Intake.
        /// </summary>
        public void Update(double dt, bool interactHeld, bool deliverAllPressed, bool interactTapped = false)
        {
            bool pressed = (interactHeld && !_wasHeld) || interactTapped;
            _wasHeld = interactHeld;
            // A press is remembered for a moment, so one made while the last request is still on
            // its way (or a step before the Intake comes into view) is not lost.
            if (pressed) _pressLatch = PressLatchSeconds;
            else if (_pressLatch > 0) _pressLatch = Math.Max(0, _pressLatch - dt);

            var world = _client.World;
            // A paused server answers nothing but the resume: no prompt, and no request to queue up behind it.
            // Nor does it answer the hands of a player who is down.
            if (_client.State != ClientState.Joined || world == null || !world.HasLocal || world.Paused || world.Local.Dead)
            {
                Reset();
                return;
            }

            var prompt = FindTarget(world);
            // A swing begun at one thing is not finished on another.
            if (prompt.Kind != InteractKind.Harvest && _harvestNode >= 0)
            {
                _harvestNode = -1;
                _hold = 0;
            }
            switch (prompt.Kind)
            {
                case InteractKind.Mount:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.Mount(prompt.Vehicle!.Id);
                    }
                    break;

                case InteractKind.RebuildRuin:
                case InteractKind.ClearRuin:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        // A ruin that cannot be rebuilt yet is not cleared by the press that tried.
                        if (prompt.Kind == InteractKind.RebuildRuin && world.RebuildState(prompt.Built!) != OpResult.Ok) break;
                        _awaiting = _client.RebuildRuin(prompt.Built!.Tx, prompt.Built.Tz, prompt.Kind == InteractKind.RebuildRuin);
                    }
                    break;

                case InteractKind.OperateTurret:
                case InteractKind.LeaveTurret:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.OperateTurret(prompt.Built!.Tx, prompt.Built.Tz, prompt.Kind == InteractKind.OperateTurret);
                    }
                    break;

                case InteractKind.Refuel:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.Refuel(prompt.Vehicle!.Id);
                    }
                    break;

                case InteractKind.OpenRack:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.OpenContainer(prompt.Vehicle!.CargoContainer);
                    }
                    break;

                case InteractKind.CloseRack:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.CloseContainer();
                    }
                    break;

                case InteractKind.OpenIntake:
                case InteractKind.OpenChest:
                case InteractKind.OpenBag:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.OpenContainer(
                            prompt.Bag != null ? prompt.Bag.ContainerId
                            : prompt.Built != null ? prompt.Built.ContainerId
                            : prompt.Chest != null ? world.ChestContainerIds[prompt.Chest.Id] : world.IntakeContainerId);
                    }
                    break;

                case InteractKind.CloseIntake:
                case InteractKind.CloseChest:
                case InteractKind.CloseBag:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.CloseContainer();
                    }
                    break;

                case InteractKind.OpenShop:
                case InteractKind.CloseShop:
                    _hold = 0;
                    _deliverAllDest = -1;
                    // The cards are this client's own: nothing to wait for. A press on a closed counter is spent on it.
                    if (_pressLatch > 0)
                    {
                        _pressLatch = 0;
                        if (prompt.ShopClosed) break;
                        if (prompt.Kind == InteractKind.OpenShop) _client.OpenShop();
                        else _client.CloseShop();
                    }
                    break;

                case InteractKind.Configure:
                case InteractKind.CloseFilters:
                    _hold = 0;
                    _deliverAllDest = -1;
                    // The filters are this client's to show: the server only hears which are up.
                    if (_pressLatch > 0)
                    {
                        _pressLatch = 0;
                        if (prompt.Kind == InteractKind.Configure) _client.OpenFilters(prompt.Built!);
                        else _client.CloseFilters();
                    }
                    break;

                case InteractKind.Deliver:
                    StepDeliver(world, ref prompt, dt, interactHeld, deliverAllPressed);
                    break;

                case InteractKind.Harvest:
                    StepHarvest(world, ref prompt, dt, interactHeld);
                    break;

                case InteractKind.PushBelt:
                    _hold = 0;
                    _deliverAllDest = -1;
                    _pressLatch = 0;
                    _pushAgain = Math.Max(0, _pushAgain - dt);
                    prompt.Pushing = interactHeld || interactTapped;
                    if (prompt.Pushing && _pushAgain <= 0 && _client.PushBelt(prompt.Built!.Tx, prompt.Built.Tz) != 0) _pushAgain = PushRepeatSeconds;
                    break;

                default:
                    _hold = 0;
                    _deliverAllDest = -1;
                    break;
            }
            Prompt = prompt;
        }

        private void Reset()
        {
            Prompt = default;
            _pressLatch = 0;
            _hold = 0;
            _deliverAllDest = -1;
            _harvestNode = -1;
        }

        /// <summary>
        /// The interactable the player is looking at: the first one the view ray hits among those
        /// within the client interact range of the player (doc 09 §3.1: 2.5 m). An open Intake,
        /// chest or bag stays targeted while in range, because the mouse is on its panel, not on the world.
        /// A tree, a bush or a boulder is one while there is something to get from it. The Intake
        /// and the shop counter are also found by facing them, when the ray finds nothing.
        /// </summary>
        /// <summary>Whether the hotbar slot in the hand holds an Oil Can.</summary>
        private bool HoldsOilCan(ClientWorld world)
        {
            int slot = _client.Input.HeldSlot;
            var held = slot > 0 ? world.Hotbar.EntryAt(slot, 0) : null;
            return held != null && held.Stack.Kind == StackKind.Item && held.Stack.DefIndex < world.Content.Items.Count
                && world.Content.Items[held.Stack.DefIndex].Id == "oil_can";
        }

        private InteractPrompt FindTarget(ClientWorld world)
        {
            float range = world.Content.Balance.GetFloat("interactRange");
            float px = world.Local.X, pz = world.Local.Z;
            float rangeSq = range * range;
            var ray = ViewRay.FromPlayer(world.Local, _client.Input.YawRadians, _client.Input.PitchRadians);
            float best = range + AimSlack;
            var prompt = new InteractPrompt();

            // The rack is open from the saddle: the mouse is on its panel, and the key shuts it.
            var ride = world.LocalRide;
            if (ride != null && world.OpenContainerId != 0 && world.OpenContainerId == ride.CargoContainer)
            {
                prompt.Kind = InteractKind.CloseRack;
                prompt.Vehicle = ride;
                return prompt;
            }

            // At a turret's controls the key lets go of them, whatever else is in view.
            var manned = world.LocalTurret;
            if (manned != null)
            {
                prompt.Kind = InteractKind.LeaveTurret;
                prompt.Built = manned;
                return prompt;
            }

            var po = world.Map.PostOffice;
            bool intakeNear = DistSq(px, pz, po.IntakeX, po.IntakeZ) <= rangeSq;
            if (intakeNear)
            {
                if (world.OpenContainerId == world.IntakeContainerId)
                {
                    prompt.Kind = InteractKind.CloseIntake;
                    return prompt;
                }
                float t = ray.Hit(AimBox.Intake(world.Map));
                if (t >= 0f && t <= best)
                {
                    best = t;
                    prompt.Kind = InteractKind.OpenIntake;
                }
            }
            // The filters are up on the inventory screen: the key takes them down.
            if (world.FilterTarget != null)
            {
                prompt.Kind = InteractKind.CloseFilters;
                prompt.Built = world.FilterTarget;
                return prompt;
            }
            // The cards are on the screen wherever they went up (at Payday that is anywhere): the key takes them down.
            if (world.ShopOpen)
            {
                prompt.Kind = InteractKind.CloseShop;
                return prompt;
            }
            // The counter has a reach of its own, so that it is found from anywhere on the pad before it.
            float shopRange = world.Content.Balance.GetFloat("shopRange");
            bool shopNear = po.HasShop && DistSq(px, pz, po.ShopX, po.ShopZ) <= shopRange * shopRange;
            if (shopNear)
            {
                float t = ray.Hit(AimBox.Shop(world.Map));
                if (t >= 0f && t <= shopRange + AimSlack && (prompt.Kind == InteractKind.None || t <= best))
                {
                    best = t;
                    prompt.Kind = InteractKind.OpenShop;
                    prompt.ShopClosed = !world.ShopServes;
                }
            }
            foreach (var chest in world.Map.Chests)
            {
                if (DistSq(px, pz, chest.X, chest.Z) > rangeSq) continue;
                if (world.OpenContainerId == world.ChestContainerIds[chest.Id])
                {
                    prompt.Kind = InteractKind.CloseChest;
                    prompt.Chest = chest;
                    return prompt;
                }
                float t = ray.Hit(AimBox.Chest(world.Map, chest));
                if (t < 0f || t > best) continue;
                best = t;
                prompt.Kind = InteractKind.OpenChest;
                prompt.Chest = chest;
            }
            foreach (var built in world.Constructs.All)
            {
                // A Depot is reached from round its four tiles, measured to its middle.
                float reach = range + (built.Def.Width > 1 ? built.Def.Radius : 0f);
                if (built.ContainerId == 0 || DistSq(px, pz, built.CentreX, built.CentreZ) > reach * reach) continue;
                if (world.OpenContainerId == built.ContainerId)
                {
                    prompt.Kind = InteractKind.CloseChest;
                    prompt.Chest = null;
                    prompt.Built = built;
                    return prompt;
                }
                float t = ray.Hit(AimBox.Built(world.Map, built));
                if (t < 0f || t > best) continue;
                best = t;
                prompt.Kind = InteractKind.OpenChest;
                prompt.Chest = null;
                prompt.Built = built;
            }
            foreach (var bag in world.Bags.Values)
            {
                if (DistSq(px, pz, bag.X, bag.Z) > rangeSq) continue;
                if (world.OpenContainerId == bag.ContainerId)
                {
                    prompt.Kind = InteractKind.CloseBag;
                    prompt.Chest = null;
                    prompt.Built = null;
                    prompt.Bag = bag;
                    return prompt;
                }
                float t = ray.Hit(AimBox.Bag(world.Map, bag.X, bag.Z));
                if (t < 0f || t > best) continue;
                best = t;
                prompt.Kind = InteractKind.OpenBag;
                prompt.Chest = null;
                prompt.Built = null;
                prompt.Bag = bag;
            }
            if (HarvestRules.IsOpen(world.Global.Phase))
            {
                var props = world.Map.Props;
                for (int i = 0; i < props.Count; i++)
                {
                    var node = world.Nodes.Def(i);
                    if (node == null || world.Nodes.Depleted(i)) continue;
                    var prop = props[i];
                    // Measured to the trunk's middle, as the server does: a wide boulder is reached from its side.
                    float reach = range + prop.Radius;
                    if (DistSq(px, pz, prop.X, prop.Z) > reach * reach) continue;
                    float t = ray.Hit(AimBox.Node(world.Map, prop));
                    if (t < 0f || t > best) continue;
                    best = t;
                    prompt.Kind = InteractKind.Harvest;
                    prompt.Chest = null;
                    prompt.Built = null;
                    prompt.Bag = null;
                    prompt.Node = node;
                    prompt.NodeIndex = i;
                }
            }
            if (ride == null)
            {
                foreach (var v in world.Vehicles.Values)
                {
                    float reachV = range + v.Def.BodyOffset;
                    if (v.Ridden || DistSq(px, pz, v.X, v.Z) > reachV * reachV) continue;
                    float t = ray.Hit(AimBox.Vehicle(world.Map, v.Def, v.X, v.Z, v.Heading));
                    if (t < 0f || t > best) continue;
                    best = t;
                    prompt.Kind = v.Def.Fuelled && HoldsOilCan(world) ? InteractKind.Refuel : InteractKind.Mount;
                    prompt.Chest = null;
                    prompt.Built = null;
                    prompt.Bag = null;
                    prompt.Node = null;
                    prompt.Vehicle = v;
                }
            }
            // A belt is pushed (doc 04 §2.1); one under the feet too, looking down at it. A sorter
            // has its filters set, and so has a splitter in Build mode.
            bool building = Building?.Invoke() == true;
            foreach (var built in world.Constructs.All)
            {
                // A sorter or an inserter: what has filters and carries nothing.
                bool sorter = built.Def.FilterOutputs > 0 && !built.Def.Carries;
                if (!built.Def.Carries && !sorter) continue;
                float reach = range + (sorter ? built.Def.Radius : 0f);
                if (DistSq(px, pz, sorter ? built.CentreX : built.X, sorter ? built.CentreZ : built.Z) > reach * reach) continue;
                float t = ray.Hit(AimBox.Built(world.Map, built));
                if (t < 0f || t > best) continue;
                best = t;
                prompt.Kind = sorter || (building && built.Def.FilterOutputs > 0) ? InteractKind.Configure : InteractKind.PushBelt;
                prompt.Chest = null;
                prompt.Bag = null;
                prompt.Node = null;
                prompt.Vehicle = null;
                prompt.Built = built;
            }
            // A ruin (doc 05 §5): rebuilt from beside it in Prep, cleared away in Delivery.
            if (BuildRules.IsOpen(world.Global.Phase))
            {
                foreach (var ruin in world.Constructs.Ruins)
                {
                    float reach = range + (ruin.Def.Width > 1 ? ruin.Def.Radius : 0f);
                    if (DistSq(px, pz, ruin.CentreX, ruin.CentreZ) > reach * reach) continue;
                    float t = ray.Hit(AimBox.Built(world.Map, ruin));
                    if (t < 0f || t > best) continue;
                    best = t;
                    prompt.Kind = world.Global.Phase == RunPhase.Prep ? InteractKind.RebuildRuin : InteractKind.ClearRuin;
                    prompt.Chest = null;
                    prompt.Bag = null;
                    prompt.Node = null;
                    prompt.Vehicle = null;
                    prompt.Built = ruin;
                }
            }
            // A turret's controls are taken from beside it (doc 05 §4), by one carrier at a time, on foot.
            if (ride == null)
            {
                foreach (var built in world.Constructs.All)
                {
                    if (!built.Def.IsTurret || world.TurretOperators.ContainsKey(built.Tz * world.Map.Cols + built.Tx)) continue;
                    if (DistSq(px, pz, built.CentreX, built.CentreZ) > rangeSq) continue;
                    float t = ray.Hit(AimBox.Built(world.Map, built));
                    if (t < 0f || t > best) continue;
                    best = t;
                    prompt.Kind = InteractKind.OperateTurret;
                    prompt.Chest = null;
                    prompt.Bag = null;
                    prompt.Node = null;
                    prompt.Vehicle = null;
                    prompt.Built = built;
                }
            }
            foreach (var dest in world.Map.Destinations)
            {
                // A Business Dock takes Cargo from vehicles and belts, not from the hand.
                if (dest.IsDock) continue;
                if (DistSq(px, pz, dest.MailboxX, dest.MailboxZ) > rangeSq) continue;
                float t = ray.Hit(AimBox.Mailbox(world.Map, dest));
                if (t < 0f || t > best) continue;
                best = t;
                prompt.Kind = InteractKind.Deliver;
                prompt.Chest = null;
                prompt.Built = null;
                prompt.Bag = null;
                prompt.Node = null;
                prompt.Vehicle = null;
                prompt.Destination = dest;
            }
            // Nothing in the crosshair: a counter of the Post Office the carrier stands at and faces
            // is meant all the same, whatever the eyes are on (its sign, the wall, the floor before it).
            if (prompt.Kind == InteractKind.None)
            {
                float yaw = _client.Input.YawRadians;
                float off = intakeNear ? FacingOff(px, pz, yaw, po.IntakeX, po.IntakeZ) : float.MaxValue;
                // A counter that is shut says so to whoever looks at it, and to nobody who walks by.
                float shopOff = shopNear && world.ShopServes ? FacingOff(px, pz, yaw, po.ShopX, po.ShopZ) : float.MaxValue;
                if (shopOff < off && shopOff <= FacingCone) prompt.Kind = InteractKind.OpenShop;
                else if (off <= FacingCone) prompt.Kind = InteractKind.OpenIntake;
                // In the saddle with nothing else to see to: the rack behind, and how to get off.
                else if (ride != null)
                {
                    // A boat run dry on the water is filled from the seat (design choice 113).
                    prompt.Kind = ride.Def.Fuelled && ride.Fuel <= 0f && HoldsOilCan(world) ? InteractKind.Refuel : InteractKind.OpenRack;
                    prompt.Vehicle = ride;
                }
                return prompt;
            }
            if (prompt.Kind == InteractKind.Harvest)
            {
                int hand = _client.Input.HeldSlot;
                prompt.NodeState = world.NodeState(prompt.NodeIndex, hand);
                prompt.NodeYield = HarvestRules.Yield(prompt.Node!, HarvestRules.Held(world.Content, world.Hotbar, hand));
                prompt.NodeHitsLeft = world.Nodes.HitsLeft(prompt.NodeIndex);
                return prompt;
            }
            if (prompt.Kind != InteractKind.Deliver) return prompt;

            var target = prompt.Destination!;
            if (world.Global.Phase != RunPhase.Delivery)
            {
                prompt.MailboxClosed = true;
                return prompt;
            }
            prompt.MatchingStacks =CountMatching(world.Hotbar, target) + CountMatching(world.Inventory, target);
            int slot = _client.Input.HeldSlot;
            var entry = slot > 0 ? world.Hotbar.EntryAt(slot, 0) : null;
            if (entry != null && entry.Stack.Kind == StackKind.Mail)
            {
                prompt.Stack = entry;
                prompt.AddressMatches = entry.Stack.Address == target.Address;
                prompt.KindAccepted = DeliveryRules.KindAccepted(world.Content.MailKinds[entry.Stack.DefIndex], target.Type);
            }
            return prompt;
        }

        private void StepDeliver(ClientWorld world, ref InteractPrompt prompt, double dt, bool interactHeld, bool deliverAllPressed)
        {
            var dest = prompt.Destination!;
            if (prompt.MailboxClosed)
            {
                _hold = 0;
                _deliverAllDest = -1;
                return;
            }
            // As this carrier's perks have it (Quick Hands); the server allows for the same.
            double holdSeconds = Math.Max(0.0, world.LocalStat(Stat.InsertHold, dest.Type.ManualInsertHold));
            if (_deliverAllDest >= 0 && _deliverAllDest != dest.Id) _deliverAllDest = -1;
            if (deliverAllPressed && prompt.MatchingStacks > 0 && _deliverAllDest < 0)
            {
                _deliverAllDest = dest.Id;
                _hold = 0;
            }

            if (_deliverAllDest >= 0)
            {
                // One matching stack per hold time, without the player holding anything (doc 09 §3.1 step 4).
                if (_awaiting != 0) return;
                var (container, entry) = FirstMatching(world, dest);
                if (entry == null) { _deliverAllDest = -1; _hold = 0; return; }
                _hold += dt;
                if (_hold >= holdSeconds)
                {
                    _hold = 0;
                    _awaiting = _client.Deliver(container!.Id, entry.EntryId, dest.Id);
                }
                prompt.HoldProgress = (float)Math.Min(1.0, _hold / holdSeconds);
                return;
            }

            if (!interactHeld || prompt.Stack == null || !prompt.KindAccepted || _awaiting != 0)
            {
                _hold = 0;
                return;
            }
            _hold += dt;
            if (_hold >= holdSeconds)
            {
                _hold = 0;
                _awaiting = _client.Deliver(world.Hotbar.Id, prompt.Stack.EntryId, dest.Id);
            }
            prompt.HoldProgress = (float)Math.Min(1.0, _hold / holdSeconds);
        }

        /// <summary>
        /// Held interact swings at the node: a hit at the end of every swing time (doc 03 §3.4),
        /// for as long as the key is down and the last hit has been answered.
        /// </summary>
        private void StepHarvest(ClientWorld world, ref InteractPrompt prompt, double dt, bool interactHeld)
        {
            _deliverAllDest = -1;
            if (_harvestNode != prompt.NodeIndex)
            {
                _harvestNode = prompt.NodeIndex;
                _hold = 0;
            }
            if (!interactHeld || prompt.NodeState != OpResult.Ok)
            {
                _hold = 0;
                return;
            }
            double swingSeconds = HarvestRules.SwingSeconds(world.Content.Balance, prompt.Node!, HarvestRules.Held(world.Content, world.Hotbar, _client.Input.HeldSlot));
            // The arm comes back while the answer is on its way; it does not come down before it is here.
            _hold = Math.Min(swingSeconds, _hold + dt);
            if (_hold >= swingSeconds && _awaiting == 0)
            {
                _hold = 0;
                _awaiting = _client.Harvest(prompt.NodeIndex);
            }
            prompt.HoldProgress = swingSeconds > 0 ? (float)Math.Min(1.0, _hold / swingSeconds) : 0f;
        }

        private static bool Matches(ClientWorld world, GridEntry e, DestinationDef dest) =>
            e.Stack.Kind == StackKind.Mail && e.Stack.Address == dest.Address
            && DeliveryRules.KindAccepted(world.Content.MailKinds[e.Stack.DefIndex], dest.Type);

        private int CountMatching(GridContainer container, DestinationDef dest)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (Matches(_client.World!, e, dest)) n++;
            return n;
        }

        private static (GridContainer?, GridEntry?) FirstMatching(ClientWorld world, DestinationDef dest)
        {
            foreach (var container in new[] { world.Hotbar, world.Inventory })
            {
                GridEntry? best = null;
                foreach (var e in container.Entries)
                    if (Matches(world, e, dest) && (best == null || e.EntryId < best.EntryId)) best = e;
                if (best != null) return (container, best);
            }
            return (null, null);
        }

        /// <summary>
        /// How far the view is turned from a point on the ground, in radians, up and down aside:
        /// 0 faces it. Standing on the point faces it whichever way one looks.
        /// </summary>
        public static float FacingOff(float px, float pz, float yaw, float x, float z)
        {
            float dx = x - px, dz = z - pz;
            if (dx * dx + dz * dz < 1e-4f) return 0f;
            float off = MathF.Atan2(-dx, -dz) - yaw;
            off -= MathF.Round(off / (2f * MathF.PI)) * 2f * MathF.PI;
            return MathF.Abs(off);
        }

        private static float DistSq(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz;
        }
    }
}
