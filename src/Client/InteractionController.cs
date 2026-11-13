using System;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
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
        /// <summary>How long a press of interact waits for something to act on.</summary>
        private const double PressLatchSeconds = 0.25;

        public InteractPrompt Prompt { get; private set; }

        private readonly ClientRuntime _client;
        private uint _awaiting;
        private bool _wasHeld;
        private double _pressLatch;
        private double _hold;
        private int _deliverAllDest = -1;

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
            if (_client.State != ClientState.Joined || world == null || !world.HasLocal || world.Paused)
            {
                Reset();
                return;
            }

            var prompt = FindTarget(world);
            switch (prompt.Kind)
            {
                case InteractKind.OpenIntake:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.OpenContainer(world.IntakeContainerId);
                    }
                    break;

                case InteractKind.CloseIntake:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (_pressLatch > 0 && _awaiting == 0)
                    {
                        _pressLatch = 0;
                        _awaiting = _client.CloseContainer();
                    }
                    break;

                case InteractKind.Deliver:
                    StepDeliver(world, ref prompt, dt, interactHeld, deliverAllPressed);
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
        }

        /// <summary>
        /// The interactable the player is looking at: the first one the view ray hits among those
        /// within the client interact range of the player (doc 09 §3.1: 2.5 m). An open Intake stays
        /// targeted while in range, because the mouse is on its panel, not on the world.
        /// </summary>
        private InteractPrompt FindTarget(ClientWorld world)
        {
            float range = world.Content.Balance.GetFloat("interactRange");
            float px = world.Local.X, pz = world.Local.Z;
            float rangeSq = range * range;
            var ray = ViewRay.FromPlayer(world.Local, _client.Input.YawRadians, _client.Input.PitchRadians);
            float best = range + AimSlack;
            var prompt = new InteractPrompt();

            var po = world.Map.PostOffice;
            if (DistSq(px, pz, po.IntakeX, po.IntakeZ) <= rangeSq)
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
            foreach (var dest in world.Map.Destinations)
            {
                if (DistSq(px, pz, dest.MailboxX, dest.MailboxZ) > rangeSq) continue;
                float t = ray.Hit(AimBox.Mailbox(world.Map, dest));
                if (t < 0f || t > best) continue;
                best = t;
                prompt.Kind = InteractKind.Deliver;
                prompt.Destination = dest;
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
            double holdSeconds = dest.Type.ManualInsertHold;
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

        private static float DistSq(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz;
        }
    }
}
