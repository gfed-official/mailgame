using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client
{
    /// <summary>
    /// HUD event feed (doc 09 §2.4): the last 5 events, 6 s each. A stack goes into a mailbox item
    /// by item, so consecutive deliveries by one player to one mailbox are compressed into one line.
    /// </summary>
    public sealed class EventFeed
    {
        /// <summary>The tile of the ruin just rebuilt, whose construct's own line is not repeated; (-1, -1) for none.</summary>
        private (int, int) _rebuilt = (-1, -1);
        /// <summary>The tile a Tank just shoved a construct onto: its putting up there is not news of a building.</summary>
        private (int, int) _pushed = (-1, -1);
        /// <summary>When the feed last said the Post Office was under attack; it says so again after a quiet 10 s.</summary>
        private double _postOfficeNews = double.MinValue;
        private const double PostOfficeNewsTicks = 10 * Sim.Core.SimConstants.TickRate;

        public const int MaxEntries = 5;
        public const double EntrySeconds = 6.0;

        public sealed class Entry
        {
            public string Text = "";
            public bool Bad;
            public double Age;

            internal EntityId Player;
            internal ushort DestId, Kind;
            internal DeliveryMachine Machine;
            internal bool Delivery, Kill, Harvest, Build;
            internal int Count, Amount;
        }

        public IReadOnlyList<Entry> Entries => _entries;
        /// <summary>Bumped whenever the visible lines change.</summary>
        public int Revision { get; private set; }

        private readonly List<Entry> _entries = new List<Entry>();
        /// <summary>What a request's answer is read against; set by the session, else from the first event.</summary>
        public ContentDb? Content;

        public void Update(double dt)
        {
            foreach (var e in _entries) e.Age += dt;
            if (_entries.RemoveAll(e => e.Age >= EntrySeconds) > 0) Revision++;
        }

        public void Add(string text, bool bad = false) => Push(new Entry { Text = text, Bad = bad });

        public void OnEvent(ClientWorld world, SimEvent ev)
        {
            Content ??= world.Content;
            switch (ev)
            {
                case DeliveredEvent e: AddDelivery(world, e.Player, e.DestId, e.Kind, e.Paid, bad: false, e.Machine); break;
                case MisdeliveredEvent e: AddDelivery(world, e.Player, e.DestId, e.Kind, e.Penalty, bad: true); break;
                case PlayerJoinedEvent e when e.Player != world.LocalId: Add($"{e.Name} joined"); break;
                case PlayerLeftEvent e when world.LastLeft != null && world.LastLeft.Id == e.Player:
                    Add(world.LastLeft.Away ? $"{world.LastLeft.Name} did not come back" : $"{world.LastLeft.Name} left", bad: world.LastLeft.Away);
                    break;
                case PlayerAwayEvent e when world.Players.TryGetValue(e.Player, out var away): Add(HudText.WentAway(world, away), bad: true); break;
                case PlayerBackEvent e when e.Player != world.LocalId: Add($"{Who(world, e.Player)} is back"); break;
                case MailSpawnedEvent e: Add($"{HudText.CountAndKind("item", e.Count)} of mail arrived at the Intake"); break;
                case ShiftSummaryEvent e:
                {
                    var s = e.Summary;
                    string books = $"{HudText.Gold(s.Earned)} of {HudText.Gold(s.Quota)}";
                    Add(s.QuotaMet ? $"Shift {s.Shift} is over: quota met ({books})" : $"Shift {s.Shift} is over: quota missed ({books})", bad: !s.QuotaMet);
                    break;
                }
                case PhaseChangedEvent e:
                    OnPhase(e);
                    // Doc 01 §1.2: Prep is when the shift's district opens.
                    if (e.Phase == RunPhase.Prep)
                    {
                        string opened = HudText.DistrictOpened(world.Map, e.Shift);
                        if (opened.Length > 0) Add(opened);
                    }
                    break;
                case EnemyDiedEvent e when !e.Killer.IsNone: AddKill(world, e); break;
                case RaidChangedEvent e: OnRaid(world, e.Raid); break;
                case PlayerDiedEvent e:
                    Add($"{Who(world, e.Player)} went down to a {HudText.EnemyName(world.Content, e.Killer, false)}", bad: true);
                    break;
                case PlayerRespawnedEvent e:
                    Add(e.Player == world.LocalId ? "You are back on your feet" : $"{Who(world, e.Player)} is back on their feet");
                    break;
                case BagDroppedEvent e:
                    Add(e.Owner == world.LocalId
                        ? "Your bag lies where you fell: get to it before it is gone"
                        : $"{HudText.Capitalised(HudText.BagName(world, e.Owner))} lies where they "
                            + (world.Players.TryGetValue(e.Owner, out var owner) && owner.Away ? "stood" : "fell"));
                    break;
                case BagGoneEvent e: OnBagGone(world, e); break;
                case DraftOfferEvent e when e.Reroll:
                    Add(HudText.Rerolled(e.RerollsLeft));
                    break;
                case PerkPickedEvent e when e.Perk < world.Content.Perks.Count:
                    Add(HudText.PerkPicked(world, e.Player, world.Content.Perks[e.Perk], e.Auto));
                    break;
                case ShopOfferEvent:
                {
                    string specials = HudText.ShopSpecials(world);
                    if (specials.Length > 0) Add(specials);
                    break;
                }
                case ShopBoughtEvent e when e.Item < world.Content.ShopItems.Count:
                    Add(HudText.ShopBought(world, e.Player, world.Content.ShopItems[e.Item], e.Price));
                    if (world.Content.ShopItems[e.Item].Kind == ShopKind.Blueprint) Add(HudText.BlueprintBought(world, world.Content.ShopItems[e.Item]));
                    break;
                case ShopSoldEvent e: Add(HudText.ShopSold(world, e.Player, e.Item, e.Count, e.Paid)); break;
                case NodeHarvestedEvent e: AddHarvest(world, e); break;
                case NodesRegrownEvent e when !e.All: Add("The bushes have grown back"); break;
                case VehicleAddedEvent e when e.Def < world.Content.Vehicles.Count:
                    Add(HudText.VehicleAdded(world.Content.Vehicles[e.Def]));
                    break;
                case WorldChangedEvent e:
                    Add(HudText.WorldChanged(world, e));
                    break;
                case KitGivenEvent e:
                {
                    string line = HudText.KitGiven(world, e);
                    if (line.Length > 0) Add(line);
                    break;
                }
                case PingEvent e:
                    Add(HudText.PingNews(world, e), bad: e.Kind == PingKind.Danger);
                    break;
                case DriverEvent e:
                {
                    int key = e.Y * world.Map.Cols + e.X;
                    _drivers.TryGetValue(key, out var was);
                    _drivers[key] = e;
                    string line = HudText.DriverNews(world, e, was);
                    if (line.Length > 0) Add(line, bad: e.State == DriverState.Fleeing || e.State == DriverState.Stranded);
                    break;
                }
                case VehicleMountedEvent e when world.Vehicles.TryGetValue(e.Vehicle, out var mounted):
                    Add(HudText.Mounted(world, e.Player, mounted.Def));
                    break;
                case VehicleParkedEvent e when e.Player == world.LocalId && world.Vehicles.TryGetValue(e.Vehicle, out var parked):
                    Add($"You got off the {parked.Def.Name}");
                    break;
                // Doc 05 §5: once at the first blow of a while, and when it falls.
                case PostOfficeDamagedEvent e:
                    if (e.Hp == 0) Add(HudText.PostOfficeFallen, bad: true);
                    else if (world.ServerTick - _postOfficeNews > PostOfficeNewsTicks)
                    {
                        _postOfficeNews = world.ServerTick;
                        Add(HudText.PostOfficeUnderAttack, bad: true);
                    }
                    break;
                case ConstructPushedEvent e:
                {
                    var shoved = world.Constructs.At(e.FromX, e.FromY) ?? world.Constructs.At(e.X, e.Y);
                    string who = e.Enemy < world.Content.Enemies.Count ? HudText.WithArticle(world.Content.Enemies[e.Enemy].Name) : "A raider";
                    if (shoved != null) Add($"{char.ToUpperInvariant(who[0])}{who.Substring(1)} shoved {HudText.WithArticle(shoved.Def.Name)} out of its way", bad: true);
                    _pushed = (e.X, e.Y);
                    break;
                }
                case ConstructPlacedEvent e when _pushed == (e.X, e.Y):
                    _pushed = (-1, -1);
                    break;
                // A ruin rebuilt said so already (the ruin's line comes first).
                case ConstructPlacedEvent e when _rebuilt == (e.X, e.Y):
                    _rebuilt = (-1, -1);
                    break;
                case ConstructPlacedEvent e when e.Building < world.Content.Buildings.Count:
                {
                    // A drag line is many in a row: one line, "You built 5 Conveyor Belts".
                    var last = _entries.Count > 0 ? _entries[_entries.Count - 1] : null;
                    if (last != null && last.Build && last.Player == e.Player && last.Kind == e.Building && last.Age < 1.0)
                    {
                        last.Count++;
                        last.Age = 0;
                        last.Text = HudText.Built(world, e.Player, world.Content.Buildings[e.Building], last.Count);
                        Revision++;
                        break;
                    }
                    Push(new Entry { Text = HudText.Built(world, e.Player, world.Content.Buildings[e.Building]), Build = true, Player = e.Player, Kind = e.Building, Count = 1 });
                    break;
                }
                // Cleared away with the run, nobody's doing: not worth a line.
                case RuinEvent e when e.Gone && !e.Player.IsNone && e.Building < world.Content.Buildings.Count:
                    Add(HudText.RuinGone(world, e, world.Content.Buildings[e.Building]));
                    if (e.Rebuilt) _rebuilt = (e.X, e.Y);
                    break;
                case ConstructRemovedEvent e when world.LastRemoved != null && (e.Destroyed || !e.Player.IsNone):
                    Add(HudText.Unbuilt(world, e.Player, world.LastRemoved.Def, e.Destroyed), bad: e.Destroyed);
                    break;
            }
        }

        /// <summary>What each depot's driver did last, for the news of the next word.</summary>
        private readonly Dictionary<int, DriverEvent> _drivers = new Dictionary<int, DriverEvent>();

        /// <summary>
        /// The server's answer to a request of the local player. What was asked with a key and not
        /// on a panel has nowhere else to say why it did not happen: using what is in the hand.
        /// </summary>
        public void OnRequest(SimRequest request, OpResult result)
        {
            if (result != OpResult.Ok && request.Kind == RequestKind.Use) Add(HudText.ShopRefused(result), bad: true);
            if (result != OpResult.Ok && (request.Kind == RequestKind.PlaceConstruct || request.Kind == RequestKind.RemoveConstruct))
            {
                string why = HudText.BuildRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
            if (result != OpResult.Ok && request.Kind == RequestKind.Mount)
            {
                string why = HudText.MountRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
            if (result != OpResult.Ok && request.Kind == RequestKind.ConfigureConstruct && request.Count == SimRequest.ConfigureHire)
            {
                string why = HudText.HireRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
            if (result != OpResult.Ok && request.Kind == RequestKind.RebuildRuin)
            {
                string why = HudText.RuinRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
            if (result != OpResult.Ok && request.Kind == RequestKind.OperateTurret && request.Count == 1)
            {
                string why = HudText.OperateRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
            if (result != OpResult.Ok && request.Kind == RequestKind.Refuel)
            {
                string why = HudText.RefuelRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
            if (request.Kind == RequestKind.SetKit && Content != null && request.Count < Content.Kits.Count)
            {
                if (result == OpResult.Ok) Add(HudText.KitChosen(Content.Kits[request.Count]));
                else if (HudText.KitRefused(result).Length > 0) Add(HudText.KitRefused(result), bad: true);
            }
            if (result != OpResult.Ok && request.Kind == RequestKind.Reroll)
            {
                string why = HudText.RerollRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
            if (result != OpResult.Ok && request.Kind == RequestKind.Harvest)
            {
                // A swing a moment too soon is simply swung again.
                string why = HudText.HarvestRefused(result);
                if (why.Length > 0) Add(why, bad: true);
            }
        }

        /// <summary>A node is many hits: one player's run of one thing is one line.</summary>
        private void AddHarvest(ClientWorld world, NodeHarvestedEvent e)
        {
            Entry? entry = null;
            if (_entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.Harvest && last.Player == e.Player && last.Kind == e.Item) entry = last;
            }
            if (entry == null)
            {
                entry = new Entry { Harvest = true, Player = e.Player, Kind = e.Item };
                Push(entry);
            }
            entry.Count += e.Count;
            entry.Age = 0;
            entry.Text = HudText.Gathered(world, e.Player, e.Item, entry.Count);
            Revision++;
        }

        /// <summary>The bag is still in <see cref="ClientWorld.Bags"/> here: the line says whose it was.</summary>
        private void OnBagGone(ClientWorld world, BagGoneEvent e)
        {
            var owner = world.Bags.TryGetValue(e.ContainerId, out var bag) ? bag.Owner : EntityId.None;
            string name = HudText.Capitalised(HudText.BagName(world, owner));
            if (!e.Returned) Add($"{name} has been picked up");
            else if (e.Mail > 0) Add($"{name} is gone: {HudText.CountAndKind("item", e.Mail)} of mail went back to the Intake", bad: true);
            else if (!owner.IsNone && owner == world.LocalId) Add("Your bag is gone: what fits is back in your pockets");
        }

        private void OnRaid(ClientWorld world, RaidState raid)
        {
            switch (raid)
            {
                case RaidState.Warning: Add(HudText.RaidAnnounced(world), bad: true); break;
                case RaidState.Active: Add("The raiders are through the gate", bad: true); break;
                case RaidState.None: Add("The raid is over"); break;
            }
        }

        private static string Who(ClientWorld world, EntityId player) =>
            player.IsNone ? "A belt" : player == world.LocalId ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";

        /// <summary>A raid is many kills in a row: one player's run of one kind of enemy is one line.</summary>
        private void AddKill(ClientWorld world, EnemyDiedEvent e)
        {
            ushort kind = (ushort)(e.Kind | (e.Mega ? 0x100 : 0));
            Entry? entry = null;
            if (_entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.Kill && last.Player == e.Killer && last.Kind == kind) entry = last;
            }
            if (entry == null)
            {
                entry = new Entry { Kill = true, Player = e.Killer, Kind = kind };
                Push(entry);
            }
            entry.Count++;
            entry.Age = 0;
            string name = HudText.EnemyName(world.Content, e.Kind, e.Mega);
            entry.Text = entry.Count == 1 ? $"{Who(world, e.Killer)} felled {HudText.WithArticle(name)}" : $"{Who(world, e.Killer)} felled {HudText.CountAndKind(name, entry.Count)}";
            Revision++;
        }

        private void OnPhase(PhaseChangedEvent e)
        {
            switch (e.Phase)
            {
                case RunPhase.Lobby: Add("Back in the lobby"); break;
                case RunPhase.Prep: Add($"Shift {e.Shift}: get ready — the quota will be about {HudText.Gold(e.Quota)}"); break;
                case RunPhase.Delivery: Add($"Shift {e.Shift} has started: deliver {HudText.Gold(e.Quota)} of mail"); break;
                case RunPhase.Draft: Add($"The Draft: one perk each before shift {e.Shift + 1}"); break;
                case RunPhase.Results: Add(e.Outcome == RunOutcome.Victory ? "The run is won" : "The run is over", bad: e.Outcome != RunOutcome.Victory); break;
            }
        }

        private void AddDelivery(ClientWorld world, EntityId player, ushort destId, ushort kind, int amount, bool bad, DeliveryMachine machine = DeliveryMachine.Hand)
        {
            Entry? entry = null;
            if (_entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.Delivery && last.Player == player && last.Machine == machine && last.DestId == destId && last.Kind == kind && last.Bad == bad)
                    entry = last;
            }
            if (entry == null)
            {
                entry = new Entry { Delivery = true, Player = player, Machine = machine, DestId = destId, Kind = kind, Bad = bad };
                Push(entry);
            }
            entry.Count++;
            entry.Amount += amount;
            entry.Age = 0;

            string who = !player.IsNone ? Who(world, player) : machine switch
            {
                DeliveryMachine.Inserter => "An inserter",
                DeliveryMachine.Pipe => "A pipe",
                DeliveryMachine.Driver => "A driver",
                DeliveryMachine.Dock => "The dock",
                DeliveryMachine.Captain => "A captain",
                _ => Who(world, player),
            };
            string what = HudText.CountAndKind(kind < world.Content.MailKinds.Count ? world.Content.MailKinds[kind].Name : "item", entry.Count);
            string where = world.Map.GetDestination(destId)?.Label ?? "?";
            entry.Text = bad
                ? $"{who} misdelivered {what} at {where}  -{HudText.Gold(entry.Amount)}"
                : machine == DeliveryMachine.Dock && player.IsNone
                    ? $"The dock at {where} took {what}  +{HudText.Gold(entry.Amount)}"
                    : $"{who} delivered {what} to {where}  +{HudText.Gold(entry.Amount)}";
            Revision++;
        }

        private void Push(Entry entry)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            Revision++;
        }
    }
}
