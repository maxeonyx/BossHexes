using Terraria;
using Terraria.ModLoader;
using BossHexes.Common.Players;

namespace BossHexes.Common.GlobalProjectiles;

/// <summary>
/// Enforces the NoGrapple constraint through Terraria's grapple hooks.
/// </summary>
public sealed class NoGrappleGlobalProjectile : GlobalProjectile
{
    public override bool? CanUseGrapple(int type, Player player)
    {
        if (!Main.projHook[type] || !BossHexesPlayer.ShouldBlockGrapple(player))
            return null;

        player.GetModPlayer<BossHexesPlayer>().ShowNoGrappleDeniedMessage();
        return false;
    }

    public override bool? GrappleCanLatchOnTo(Projectile projectile, Player player, int x, int y)
    {
        if (!Main.projHook[projectile.type] || !BossHexesPlayer.ShouldBlockGrapple(player))
            return null;

        player.GetModPlayer<BossHexesPlayer>().ShowNoGrappleDeniedMessage();
        return false;
    }
}
