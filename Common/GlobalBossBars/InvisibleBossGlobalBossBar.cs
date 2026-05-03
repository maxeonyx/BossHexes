using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using BossHexes.Common.Config;
using BossHexes.Common.Systems;

namespace BossHexes.Common.GlobalBossBars;

public sealed class InvisibleBossGlobalBossBar : GlobalBossBar
{
    public override bool PreDraw(SpriteBatch spriteBatch, NPC npc, ref BossBarDrawParams drawParams)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return true;

        if (!BossHexManager.TryGetActiveHexes(npc, out var hexes))
            return true;

        return hexes.Flashy != FlashyHex.InvisibleBoss;
    }
}
