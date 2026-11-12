using System;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
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
    }

    /// <summary>
    /// Turns "the player is standing here and holds interact" into requests (doc 09 §3.1): open or
    /// close the Intake, hold to deliver the selected stack, deliver every matching stack. Godot-free
    /// so the HUD, the input layer and tests share one definition of the prompt.
    /// </summary>
    public sealed class InteractionController
    {
        public InteractPrompt Prompt { get; private set; }

        private readonly ClientRuntime _client;
        private uint _awaiting;
        private bool _wasHeld;
        private double _hold;
        private int _deliverAllDest = -1;

        public InteractionController(ClientRuntime client)
        {
            _client = client;
            client.RequestCompleted += (request, _) => { if (request.RequestId == _awaiting) _awaiting = 0; };
        }

        /// <summary>Call once per frame. <paramref name="deliverAllPressed"/> is the edge of the "deliver all matching" action.</summary>
        public void Update(double dt, bool interactHeld, bool deliverAllPressed)
        {
            bool pressed = interactHeld && !_wasHeld;
            _wasHeld = interactHeld;

            var world = _client.World;
            if (_client.State != ClientState.Joined || world == null || !world.HasLocal)
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
                    if (pressed && _awaiting == 0) _awaiting = _client.OpenContainer(world.IntakeContainerId);
                    break;

                case InteractKind.CloseIntake:
                    _hold = 0;
                    _deliverAllDest = -1;
                    if (pressed && _awaiting == 0) _awaiting = _client.CloseContainer();
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
            _hold = 0;
            _deliverAllDest = -1;
        }

        /// <summary>The nearest interactable within the client interact range (doc 09 §3.1: 2.5 m).</summary>
        private InteractPrompt FindTarget(ClientWorld world)
        {
            float range = world.Content.Balance.GetFloat("interactRange");
            float px = world.Local.X, pz = world.Local.Z;
            float best = range * range;
            var prompt = new InteractPrompt();

            var po = world.Map.PostOffice;
            float d = DistSq(px, pz, po.IntakeX, po.IntakeZ);
            if (d <= best)
            {
                best = d;
                prompt.Kind = world.OpenContainerId == world.IntakeContainerId ? InteractKind.CloseIntake : InteractKind.OpenIntake;
            }
            foreach (var dest in world.Map.Destinations)
            {
                d = DistSq(px, pz, dest.MailboxX, dest.MailboxZ);
                if (d > best) continue;
                best = d;
                prompt.Kind = InteractKind.Deliver;
                prompt.Destination = dest;
            }
            if (prompt.Kind != InteractKind.Deliver) return prompt;

            var target = prompt.Destination!;
            prompt.MatchingStacks = CountMatching(world.Hotbar, target) + CountMatching(world.Inventory, target);
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
