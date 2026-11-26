using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The Slingshot (doc 05 §1.2; "ranged uses server-side hitscan"; design choice 143): held, the
    /// attack looses a Stone along the carrier's facing at the first raider whose body is on its line
    /// within reach, those in the air too (doc 05 §2 Balloon "Only ranged weapons and turrets can hit
    /// it"). One Stone from the pockets loads five shots; without one it does nothing.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>The Slingshot in the carrier's hand, or null.</summary>
        public ItemDef? HeldSlingshot(Player p) => SlingRules.Held(Content, p.Hotbar, p.HeldSlot);

        private void FireSlingshot(Player p, ItemDef sling)
        {
            if (p.SlingLoaded == 0)
            {
                var ammo = sling.Ammo!;
                if (SlingRules.Ammo(ammo, p.Hotbar, p.Inventory) == 0) return;
                TakeMaterials(p, ammo, 1);
                CommitContainer(p.Inventory.Id);
                CommitContainer(p.Hotbar.Id);
                p.SlingLoaded = sling.ShotsPerAmmo;
            }
            p.SlingLoaded--;
            p.SlingReadyTick = Tick + Math.Max(1u, SimConstants.SecondsToTicks(sling.ShotCooldown));
            float yaw = PlayerInput.YawToRadians(p.Move.Yaw);
            float fx = -MathF.Sin(yaw), fz = -MathF.Cos(yaw);
            Enemy? hit = null;
            float best = float.MaxValue;
            foreach (var e in _enemies)
            {
                float t = SlingRules.Along(p.Move.X, p.Move.Z, fx, fz, sling.ShotRange, e.X, e.Z, e.Radius);
                if (t >= 0f && t < best) { best = t; hit = e; }
            }
            float reach = hit != null ? best : sling.ShotRange;
            var ev = new SlingshotFiredEvent
            {
                Player = p.Id, FromX = p.Move.X, FromZ = p.Move.Z, ToX = p.Move.X + fx * reach, ToZ = p.Move.Z + fz * reach,
                Hit = hit == null ? SlingHit.None : hit.Def.Airborne ? SlingHit.Air : SlingHit.Ground, Loaded = (byte)p.SlingLoaded,
            };
            Events.Add(ev);
            if (hit != null) DamageEnemy(hit, sling.ShotDamage, p);
        }
    }
}
