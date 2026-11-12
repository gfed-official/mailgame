using System;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.App
{
    /// <summary>
    /// The running game inside the Godot process (doc 07 §3). As a host it owns a ServerRuntime and
    /// a ClientRuntime joined by the loopback transport and drives both from the frame loop.
    /// </summary>
    public partial class GameSession : Node
    {
        public ContentDb Content { get; private set; } = null!;
        /// <summary>Null on a remote client.</summary>
        public ServerRuntime? Server { get; private set; }
        public ClientRuntime Client { get; private set; } = null!;
        public InteractionController Interaction { get; private set; } = null!;
        public EventFeed Feed { get; } = new EventFeed();

        /// <summary>Called every frame before the client ticks; the input layer fills <see cref="ClientRuntime.Input"/> here.</summary>
        public Action<double>? InputSource;
        /// <summary>Raised once, when the client has its world and its own player.</summary>
        public event Action? Joined;

        /// <summary>
        /// 0 = real time. Above 0 the session ignores the frame delta and runs exactly this many
        /// ticks of both ends per frame, which makes the headless smoke fast and repeatable.
        /// </summary>
        public int FixedTicksPerFrame;

        public bool HasJoined { get; private set; }

        public void StartHost(ContentDb content, uint seed, string playerName)
        {
            Content = content;
            var hub = new LoopbackHub();
            Server = new ServerRuntime(content, new SimSettings { Seed = seed }, hub) { Log = line => GD.Print("[server] ", line) };
            Client = new ClientRuntime(content, hub.CreateClient(), playerName);
            Interaction = new InteractionController(Client);
            Client.EventReceived += ev => Feed.OnEvent(Client.World!, ev);
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
            Feed.Update(delta);

            if (!HasJoined && Client.State == ClientState.Joined && Client.World!.HasLocal)
            {
                HasJoined = true;
                Joined?.Invoke();
            }
        }

        public override void _ExitTree() => Client?.Disconnect();
    }
}
