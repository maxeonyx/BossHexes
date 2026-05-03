using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using BossHexes.Common.Config;
using BossHexes.Common.GlobalProjectiles;
using BossHexes.Common.Systems;

namespace BossHexes.Common.Players;

/// <summary>
/// Applies hex effects to players during boss fights.
/// </summary>
public sealed class BossHexesPlayer : ModPlayer
{
    private int _denyUseTextCooldown;

    private bool ShouldApplyGrounded()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.IsConstraintActive(ConstraintHex.Grounded);
    }

    public static bool ShouldBlockGrapple(Player player)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return player.active && BossHexManager.IsConstraintActive(ConstraintHex.NoGrapple);
    }

    public void ShowNoGrappleDeniedMessage()
    {
        if (Main.myPlayer != Player.whoAmI || _denyUseTextCooldown > 0)
            return;

        Main.NewText("Grappling hooks are disabled by the No Grapple hex!", Color.Orange);
        _denyUseTextCooldown = 60;
    }

    public override void ModifyMaxStats(out StatModifier health, out StatModifier mana)
    {
        health = StatModifier.Default;
        mana = StatModifier.Default;

        if (ShouldApplyFrail())
        {
            health *= 0.8f;
        }
    }

    public override void SyncPlayer(int toWho, int fromWho, bool newPlayer)
    {
        // When a player joins, sync the current hex state to them
        if (Main.netMode == NetmodeID.Server && BossHexManager.HasAnyActiveHexes)
        {
            BossHexManager.SendSync(Mod, toWho, fromWho);
        }
    }

    public override void SetControls()
    {
        if (!ShouldApplyGrounded())
            return;

        Player.controlJump = false;
    }

    public override void PostUpdate()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        if (_denyUseTextCooldown > 0)
            _denyUseTextCooldown--;

        ApplyBossHexes();
    }

    public override void ModifyHitByNPC(NPC npc, ref Player.HurtModifiers modifiers)
    {
        if (!ShouldApplyGlassCannon() || !BossHexManager.IsPartOfCurrentBossFight(npc))
            return;

        modifiers.FinalDamage *= 1.5f;
    }

    public override void ModifyHitByProjectile(Projectile proj, ref Player.HurtModifiers modifiers)
    {
        if (!ShouldApplyGlassCannon())
            return;

        if (!proj.GetGlobalProjectile<BossFightSourceProjectile>().IsFromCurrentBossFight)
            return;

        modifiers.FinalDamage *= 1.5f;
    }

    public override bool CanStartExtraJump(ExtraJump jump)
    {
        if (ShouldApplyGrounded())
            return false;

        return base.CanStartExtraJump(jump);
    }

    /// <summary>
    /// Applies active boss hexes to the player. Only runs during boss fights.
    /// </summary>
    private void ApplyBossHexes()
    {
        if (!BossHexManager.IsCurrentBossFightActive())
            return;

        foreach (var (bossType, hexes) in BossHexManager.GetActiveBossFights())
        {
            if (!BossHexManager.IsBossFightActive(bossType) || !hexes.HasAnyHex)
                continue;

            // Apply flashy hexes (player-side effects)
            ApplyFlashyHex(hexes.Flashy);

            // Apply modifier hexes
            ApplyModifierHex(hexes.Modifier);

            // Apply constraint hexes
            ApplyConstraintHex(hexes.Constraint);
        }
    }

    private void ApplyFlashyHex(FlashyHex hex)
    {
        switch (hex)
        {
            case FlashyHex.WingClip:
                // Disable flight completely
                Player.wingTime = 0f;
                Player.rocketTime = 0;
                break;
                
            case FlashyHex.Blackout:
                // Extreme darkness - apply Blackout debuff
                Player.AddBuff(BuffID.Blackout, 2);
                break;
                
            // Other flashy hexes are handled elsewhere:
            // - InvisibleBoss: BossHexGlobalNPC.PreDraw
            // - TinyFastBoss/HugeBoss: BossHexGlobalNPC.PostAI
            // - TimeLimit/UnstableGravity/MeteorShower: BossHexesState
            // - Reversal: TODO
            // - Mirrored: TODO
        }
    }

    private void ApplyModifierHex(ModifierHex hex)
    {
        switch (hex)
        {
            case ModifierHex.BrokenArmor:
                // Defense halved (apply Broken Armor debuff)
                Player.AddBuff(BuffID.BrokenArmor, 2);
                break;
                
            case ModifierHex.Sluggish:
                // Slow debuff (reapplied every frame so it appears permanent)
                Player.AddBuff(BuffID.Slow, 2);
                break;
                
            case ModifierHex.SlowAttack:
                // Reduced attack speed (Slow debuff approximates this)
                Player.AddBuff(BuffID.Slow, 2);
                break;
                
            // TODO: Implement remaining modifiers:
            // - ExtraPotionSickness: needs hook in potion consumption
            // - ManaDrain: modify mana costs
            // - Inaccurate: spread projectiles
            // - Marked: boss damage boost (in BossHexGlobalNPC)
            // - SwiftBoss: handled in BossHexGlobalNPC
        }
    }

    private static bool ShouldApplyGlassCannon()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.IsModifierActive(ModifierHex.GlassCannon);
    }

    private static bool ShouldApplyFrail()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.IsModifierActive(ModifierHex.Frail);
    }

    private void ApplyConstraintHex(ConstraintHex hex)
    {
        switch (hex)
        {
            case ConstraintHex.Grounded:
                CancelGroundedJumpState();
                break;
                
            case ConstraintHex.NoGrapple:
                ClearOwnedGrapples();
                break;
                
            // TODO: Implement remaining constraints:
            // - NoBuffPotions: check item use
            // - PacifistHealer: special role assignment
        }
    }

    private void CancelGroundedJumpState()
    {
        if (Main.netMode != NetmodeID.SinglePlayer && Main.myPlayer != Player.whoAmI)
            return;

        Player.jump = 0;
        Player.StopExtraJumpInProgress();
    }

    private void ClearOwnedGrapples()
    {
        if (Main.myPlayer != Player.whoAmI)
            return;

        bool killedAny = false;

        foreach (var projectile in Main.ActiveProjectiles)
        {
            if (projectile.owner != Player.whoAmI || !Main.projHook[projectile.type])
                continue;

            projectile.Kill();
            killedAny = true;
        }

        if (killedAny)
            ShowNoGrappleDeniedMessage();
    }
}
