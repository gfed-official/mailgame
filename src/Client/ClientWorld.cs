using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>A player as the client knows it: identity plus a short buffer of snapshot samples to interpolate.</summary>
    public sealed class ClientPlayer
    {
        private const int BufferSize = 8;

        private struct Sample
        {
            public uint Tick;
            public float X, Y, Z;
            public ushort Yaw;
        }

        public EntityId Id { get; }
        public string Name { get; }
        public byte Slot { get; }
        public byte HeldSlot { get; private set; }
        public bool HasSamples => _count > 0;

        private readonly Sample[] _samples = new Sample[BufferSize];
        private int _head;
        private int _count;

        public ClientPlayer(EntityId id, string name, byte slot)
        {
            Id = id; Name = name; Slot = slot;
        }

        public void Push(uint tick, in PlayerSnap snap)
        {
            // Snapshots are unreliable and unordered: keep only ones newer than the newest held.
            if (_count > 0 && tick <= _samples[(_head - 1 + BufferSize) % BufferSize].Tick) return;
            _samples[_head] = new Sample { Tick = tick, X = snap.X, Y = snap.Y, Z = snap.Z, Yaw = snap.Yaw };
            _head = (_head + 1) % BufferSize;
            if (_count < BufferSize) _count++;
            HeldSlot = snap.HeldSlot;
        }

        /// <summary>Interpolated pose at a (fractional) server tick; clamps to the oldest / newest sample.</summary>
        public bool Interpolate(double tick, out float x, out float y, out float z, out float yawRadians)
        {
            x = y = z = yawRadians = 0f;
            if (_count == 0) return false;
            Sample newer = _samples[(_head - 1 + BufferSize) % BufferSize];
            Sample older = newer;
            for (int i = 1; i <= _count; i++)
            {
                var s = _samples[(_head - i + BufferSize) % BufferSize];
                older = s;
                if (s.Tick <= tick) break;
                newer = s;
            }
            float t = newer.Tick == older.Tick ? 0f : (float)Math.Clamp((tick - older.Tick) / (newer.Tick - older.Tick), 0.0, 1.0);
            x = older.X + (newer.X - older.X) * t;
            y = older.Y + (newer.Y - older.Y) * t;
            z = older.Z + (newer.Z - older.Z) * t;
            // Shortest-arc yaw: the difference of two u16 angles wraps correctly as a signed 16-bit value.
            short dyaw = (short)(newer.Yaw - older.Yaw);
            yawRadians = PlayerInput.YawToRadians((ushort)(older.Yaw + (int)(dyaw * t)));
            return true;
        }
    }

    /// <summary>
    /// The client's presentation model (doc 07 §5): the subset of sim state this client knows.
    /// Written only by <see cref="ClientRuntime"/>; views and UI read it.
    /// </summary>
    public sealed class ClientWorld
    {
        public ContentDb Content { get; }
        public WorldMap Map { get; internal set; }
        public EntityId LocalId { get; internal set; }
        public uint Seed { get; internal set; }

        /// <summary>Every connected player including the local one.</summary>
        public Dictionary<EntityId, ClientPlayer> Players { get; } = new Dictionary<EntityId, ClientPlayer>();

        /// <summary>Predicted movement state of the local player.</summary>
        public PlayerMoveState Local;
        public bool HasLocal { get; internal set; }
        public float LocalSpeedMult { get; internal set; } = 1f;

        /// <summary>Mirrors of the containers this client is allowed to see: body containers plus the open one.</summary>
        public Dictionary<uint, GridContainer> Containers { get; } = new Dictionary<uint, GridContainer>();
        public GridContainer Hotbar { get; internal set; } = null!;
        public GridContainer Inventory { get; internal set; } = null!;
        public uint IntakeContainerId { get; internal set; }
        /// <summary>The external container being viewed, 0 if none.</summary>
        public uint OpenContainerId { get; internal set; }

        public GlobalState Global { get; internal set; }
        /// <summary>Estimated current server tick (fractional), advanced locally between snapshots.</summary>
        public double ServerTick { get; internal set; }

        public ClientWorld(ContentDb content, WorldMap map)
        {
            Content = content;
            Map = map;
        }

        public GridContainer? OpenContainer =>
            OpenContainerId != 0 && Containers.TryGetValue(OpenContainerId, out var c) ? c : null;

        /// <summary>Seconds left on the phase clock, derived from ticks (doc 07 §4.1).</summary>
        public double PhaseSecondsLeft =>
            Math.Max(0.0, SimConstants.TicksToSeconds((long)Global.PhaseEndTick - (long)Math.Floor(ServerTick)));
    }
}
