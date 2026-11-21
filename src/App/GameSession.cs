using System;
using Godot;
using PerformativeMail.App.Net;
using PerformativeMail.Client;
using PerformativeMail.Client.Meta;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Net;

namespace PerformativeMail.App
{
    /// <summary>
    /// The running game inside the Godot process (doc 07 §3). As a host it owns a ServerRuntime and
    /// a ClientRuntime joined by the loopback transport, listens for remote clients over ENet and
    /// drives both ends from the frame loop. As a remote client it owns only the ClientRuntime.
    /// </summary>
    public partial class GameSession : Node
    {
        /// <summary>UDP port a host listens on unless told another (design choice, PROGRESS.md).</summary>
        public const int DefaultPort = 24565;

        public ContentDb Content { get; private set; } = null!;
        /// <summary>Null on a remote client.</summary>
        public ServerRuntime? Server { get; private set; }
        public ClientRuntime Client { get; private set; } = null!;
        public InteractionController Interaction { get; private set; } = null!;
        /// <summary>Build mode (doc 09 §2.7): the recipe, the ghost and what it is on.</summary>
        public BuildController Build { get; private set; } = null!;
        public EventFeed Feed { get; } = new EventFeed();
        /// <summary>The local player's Postal Rank and stats, saved at every Payday and at Results.</summary>
        public ProfileTracker Profile { get; private set; } = null!;
        /// <summary>Host: the UDP port remote clients join on, 0 when the host does not listen.</summary>
        public int ListenPort => _enetServer?.Port ?? 0;

        /// <summary>Called every frame before the client ticks; the input layer fills <see cref="ClientRuntime.Input"/> here.</summary>
        public Action<double>? InputSource;
        /// <summary>Raised once, when the client has its world and its own player.</summary>
        public event Action? Joined;
        /// <summary>Raised once, with a line for the player, when the connection ends: refused, never answered, or lost.</summary>
        public event Action<string>? ConnectionEnded;

        /// <summary>
        /// 0 = real time. Above 0 the session ignores the frame delta and runs exactly this many
        /// ticks of both ends per frame, which makes the headless smoke fast and repeatable.
        /// </summary>
        public int FixedTicksPerFrame;
        /// <summary>What the local client asks the server for when it joins; set before StartHost or StartRemote.</summary>
        public PlaytestFlags Playtest;
        /// <summary>The shift the local client asks runs to start at, 0 = the first; set like <see cref="Playtest"/>.</summary>
        public int StartShift;

        public bool HasJoined { get; private set; }

        private EnetServerTransport? _enetServer;
        private EnetClientTransport? _enetClient;
        private bool _ended;

        /// <param name="quickStart">Skip Lobby and Prep (<see cref="SimSettings.QuickStart"/>): straight into a shift.</param>
        /// <param name="profile">The local player's profile; it gives the player's name and is saved to <paramref name="store"/>.</param>
        /// <param name="listenPort">UDP port for remote clients; 0 = any free port, below 0 = do not listen.</param>
        /// <param name="mapId">The map to play: a file of content/maps, or "small_island" for the seed's own town (doc 02).</param>
        public void StartHost(ContentDb content, uint seed, MetaProfile profile, IProfileStore store, bool quickStart = false, int listenPort = -1,
            string mapId = "test_street")
        {
            Content = content;
            var hub = new LoopbackHub();
            IServerTransport transport = hub;
            if (listenPort >= 0)
            {
                _enetServer = EnetServerTransport.Listen(listenPort, out var error);
                // A port in use must not stop a solo game: the host plays on, nobody can join.
                if (_enetServer == null) GD.PrintErr($"[server] cannot listen on UDP port {listenPort} ({error}); nobody can join this game");
                else transport = new CompositeServerTransport(hub, _enetServer);
            }
            // About 20 ms here instead of a 7 ms tick when the first shift starts.
            SimWarmUp.Run(content, seed);
            Server = new ServerRuntime(content, new SimSettings { Seed = seed, QuickStart = quickStart, MapId = mapId }, transport);
            // Before anybody can join and before the log listens: the refusal of the knock is not news.
            if (_enetServer != null && !EnetWarmUp.Run(_enetServer, transport, Server, Server.ProtocolHash))
                GD.Print("[server] the ENet warm-up did not finish; the first join will cost a slow tick");
            Server.Log = line => GD.Print("[server] ", line);
            if (_enetServer != null) GD.Print($"[server] listening on UDP port {_enetServer.Port}");
            StartClient(content, hub.CreateClient(), profile, store);
        }

        /// <summary>Joins a host over ENet. False when no socket could be made; a host that never answers ends in <see cref="ConnectionEnded"/>.</summary>
        public bool StartRemote(ContentDb content, string address, int port, MetaProfile profile, IProfileStore store)
        {
            Content = content;
            _enetClient = EnetClientTransport.Connect(address, port, out var error);
            if (_enetClient == null)
            {
                GD.PrintErr($"[client] cannot connect to {address}:{port} ({error})");
                return false;
            }
            StartClient(content, _enetClient, profile, store);
            return true;
        }

        private void StartClient(ContentDb content, IClientTransport transport, MetaProfile profile, IProfileStore store)
        {
            Client = new ClientRuntime(content, transport, profile.DisplayName, Playtest, StartShift, profile.AccountId);
            Interaction = new InteractionController(Client);
            Build = new BuildController(Client) { OnRefused = why => Feed.Add(HudText.PlaceReason(Client.World!, Client.World!.Content.Recipes[0], why), bad: true) };
            Client.EventReceived += ev => Feed.OnEvent(Client.World!, ev);
            Client.RequestCompleted += Feed.OnRequest;
            Profile = new ProfileTracker(Client, profile, store);
        }

        public override void _Process(double delta)
        {
            if (Client == null) return;
            InputSource?.Invoke(delta);
            if (FixedTicksPerFrame > 0)
            {
                for (int i = 0; i < FixedTicksPerFrame; i++)
                {
                    Client.Tick();
                    Server?.Tick();
                }
            }
            else
            {
                Client.Update(delta);
                Server?.Update(delta);
            }
            // What this frame queued goes out now, not at the next frame's poll.
            _enetClient?.Flush();
            _enetServer?.Flush();
            Feed.Update(delta);

            if (!HasJoined && Client.State == ClientState.Joined && Client.World!.HasLocal)
            {
                HasJoined = true;
                Joined?.Invoke();
            }
            if (!_ended && (Client.State == ClientState.Rejected || Client.State == ClientState.Disconnected))
            {
                _ended = true;
                ConnectionEnded?.Invoke(HudText.ConnectionEnded(Client, HasJoined));
            }
        }

        public override void _ExitTree()
        {
            _ended = true;
            Client?.Disconnect();
            _enetServer?.Close();
        }
    }
}
