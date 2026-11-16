using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Combat
{
    /// <summary>What an enemy is doing; replicated in two bits for the animation.</summary>
    public enum EnemyState : byte
    {
        /// <summary>Walking at its target (or standing, with nobody to walk at).</summary>
        Chase = 0,
        /// <summary>Winding up a blow: it lands when the windup ends if the target is still in reach.</summary>
        Windup = 1,
        /// <summary>The shift is over: back to the gate (doc 05 §3.4).</summary>
        Flee = 2,
    }

    /// <summary>Where the night's raid stands (doc 05 §3.1).</summary>
    public enum RaidState : byte
    {
        None = 0,
        /// <summary>"Raid incoming from the east": the first pulse is seconds away.</summary>
        Warning = 1,
        Active = 2,
        /// <summary>The shift clock ran out; the raiders run for the gate.</summary>
        Fleeing = 3,
    }

    /// <summary>A server-side enemy (doc 05 §2). Ground melee: walks at the nearest carrier and hits.</summary>
    public sealed class Enemy
    {
        public EntityId Id;
        public EnemyDef Def = null!;
        public bool Mega;
        public float X, Z;
        /// <summary>Facing in radians, 0 looks along −Z (the same convention as a player's yaw).</summary>
        public float Yaw;
        public float Hp, MaxHp;
        public float Damage, Speed, Radius;
        public EnemyState State;
        /// <summary>Windup: the tick the blow lands.</summary>
        public uint StateEndTick;
        public uint NextAttackTick;
        public EntityId Target;
        public uint RepathTick;
        public readonly List<(float X, float Z)> Path = new List<(float X, float Z)>();
        public int Waypoint;
        /// <summary>Knockback still to travel.</summary>
        public float PushX, PushZ;
        /// <summary>The player whose blow landed last, for the kill.</summary>
        public EntityId LastHitBy;

        /// <summary>Health as the snapshot carries it: 1..255.</summary>
        public byte HpByte => (byte)System.Math.Clamp((int)System.MathF.Ceiling(Hp / MaxHp * 255f), 1, 255);
    }

    /// <summary>One item line of a loot bundle.</summary>
    public struct LootItem
    {
        public ushort Item;
        public byte Count;
    }

    /// <summary>What an enemy left on the ground (doc 05 §2.2): coins and item drops, taken by walking up.</summary>
    public sealed class LootBundle
    {
        public uint Id;
        public float X, Z;
        public byte EnemyKind;
        public bool Mega;
        public int Coins;
        public readonly List<LootItem> Items = new List<LootItem>();
        /// <summary>Not before this tick: the drop is seen to fall first, and a full inventory is not retried every tick.</summary>
        public uint PickupTick;
        public uint DespawnTick;
    }

    /// <summary>A package in flight from the mail cannon.</summary>
    public sealed class Shot
    {
        public EntityId Owner;
        public float X, Z, DirX, DirZ;
        public float Travelled;
    }
}
