using System;
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
    private int _lastPotionSicknessTime;

    private static bool HasPlayerMovementAuthority(Player player)
    {
        return Main.netMode == NetmodeID.SinglePlayer || Main.myPlayer == player.whoAmI;
    }

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

    public override void ModifyManaCost(Item item, ref float reduce, ref float mult)
    {
        if (!ShouldApplyManaDrain(item))
            return;

        mult *= 1.5f;
    }

    public override void PostUpdateBuffs()
    {
        ApplyExtraPotionSickness();
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
        if (!ShouldApplyIncomingBossDamageModifiers(npc, out var hexes))
            return;

        ApplyIncomingBossDamageModifiers(ref modifiers, hexes);
    }

    public override void ModifyHitByProjectile(Projectile proj, ref Player.HurtModifiers modifiers)
    {
        if (!ShouldApplyIncomingBossDamageModifiers(proj, out var hexes))
            return;

        ApplyIncomingBossDamageModifiers(ref modifiers, hexes);
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
                ApplyWingClip();
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
            // - Inaccurate: spread projectiles
            // - SwiftBoss: handled in BossHexGlobalNPC
        }
    }

    private void ApplyExtraPotionSickness()
    {
        int buffIndex = Player.FindBuffIndex(BuffID.PotionSickness);
        if (buffIndex == -1)
        {
            _lastPotionSicknessTime = 0;
            return;
        }

        int currentTime = Player.buffTime[buffIndex];
        if (!HasPlayerStateAuthority(Player) || !ShouldApplyExtraPotionSickness())
        {
            _lastPotionSicknessTime = currentTime;
            return;
        }

        if (ShouldExtendPotionSickness(currentTime))
        {
            long extendedTime = (long)currentTime * 3;
            Player.buffTime[buffIndex] = (int)Math.Min(extendedTime, int.MaxValue);
            currentTime = Player.buffTime[buffIndex];
        }

        _lastPotionSicknessTime = currentTime;
    }

    private bool ShouldExtendPotionSickness(int currentTime)
    {
        if (_lastPotionSicknessTime <= 0)
            return true;

        return currentTime > _lastPotionSicknessTime + 1;
    }

    private void ApplyWingClip()
    {
        if (!HasPlayerMovementAuthority(Player))
            return;

        Player.wingTime = 0f;
        Player.rocketTime = 0;

        if (Player.mount.Active && IsBlockedFlightMount(Player.mount.Type))
        {
            Player.mount.Dismount(Player);
        }
    }

    private static bool IsBlockedFlightMount(int mountType)
    {
        return mountType switch
        {
            0 => true,  // Rudolph
            2 => true,  // Pigron
            5 => true,  // Bee
            7 => true,  // UFO
            8 => true,  // Drill Containment Unit
            12 => true, // Cute Fishron
            23 => true, // Witch's Broom
            44 => true, // Pirate Ship
            48 => true, // Dark Mage's Tome
            50 => true, // Winged Slime
            _ => false,
        };
    }

    private static bool ShouldApplyIncomingBossDamageModifiers(NPC npc, out ActiveHexes hexes)
    {
        hexes = null;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.TryGetActiveHexes(npc, out hexes);
    }

    private static bool ShouldApplyIncomingBossDamageModifiers(Projectile projectile, out ActiveHexes hexes)
    {
        hexes = null;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return projectile.GetGlobalProjectile<BossFightSourceProjectile>().TryGetSourceFightHexes(out hexes);
    }

    private static void ApplyIncomingBossDamageModifiers(ref Player.HurtModifiers modifiers, ActiveHexes hexes)
    {
        if (hexes.Modifier == ModifierHex.GlassCannon)
            modifiers.FinalDamage *= 1.5f;

        if (hexes.Modifier == ModifierHex.Marked)
            modifiers.FinalDamage *= 1.25f;
    }

    private static bool ShouldApplyFrail()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.IsModifierActive(ModifierHex.Frail);
    }

    private static bool ShouldApplyManaDrain(Item item)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return item.mana > 0 && BossHexManager.IsModifierActive(ModifierHex.ManaDrain);
    }

    private static bool HasPlayerStateAuthority(Player player)
    {
        return Main.netMode == NetmodeID.SinglePlayer || Main.myPlayer == player.whoAmI;
    }

    private static bool ShouldApplyExtraPotionSickness()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.IsModifierActive(ModifierHex.ExtraPotionSickness);
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
        if (!HasPlayerMovementAuthority(Player))
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
