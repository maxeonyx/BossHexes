using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using BossHexes.Common.Systems;

namespace BossHexes.Common.GlobalProjectiles;

/// <summary>
/// Modifies meteor shower projectiles to deal reduced damage to bosses.
/// </summary>
public sealed class MeteorShowerProjectile : GlobalProjectile
{
    public override void ModifyHitNPC(Projectile projectile, NPC target, ref NPC.HitModifiers modifiers)
    {
        // Only affect falling stars during Meteor Shower hex
        if (projectile.type != ProjectileID.FallingStar)
            return;

        // Check if Meteor Shower hex is active
        var hexes = BossHexManager.Current;
        if (hexes.Flashy != FlashyHex.MeteorShower)
            return;

        // Only reduce damage to bosses - we want it to hurt players normally
        if (target.boss)
        {
            // Meteors do minimal damage to bosses (about 1/10th)
            // This is spectacle, not a free boss-killer
            modifiers.FinalDamage *= 0.1f;
        }
    }
}
