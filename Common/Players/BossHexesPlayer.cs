using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using BossHexes.Common.Config;
using BossHexes.Common.Systems;

namespace BossHexes.Common.Players;

/// <summary>
/// Applies hex effects to players during boss fights.
/// </summary>
public sealed class BossHexesPlayer : ModPlayer
{
    private int _denyUseTextCooldown;

    public override void PostUpdate()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        if (_denyUseTextCooldown > 0)
            _denyUseTextCooldown--;

        ApplyBossHexes();
    }

    public override bool CanUseItem(Item item)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return base.CanUseItem(item);

        if (!AnyBossAlive())
            return base.CanUseItem(item);

        var hexes = BossHexManager.Current;
        
        // NoGrapple constraint - block grappling hooks
        if (hexes.Constraint == ConstraintHex.NoGrapple && IsGrapplingHook(item))
        {
            if (Main.myPlayer == Player.whoAmI && _denyUseTextCooldown <= 0)
            {
                Main.NewText("Grappling hooks are disabled by the No Grapple hex!", Color.Orange);
                _denyUseTextCooldown = 60;
            }
            return false;
        }

        return base.CanUseItem(item);
    }

    /// <summary>
    /// Check if an item is a grappling hook by checking if it shoots a grapple projectile.
    /// </summary>
    private static bool IsGrapplingHook(Item item)
    {
        if (item.shoot <= 0)
            return false;
        
        // Check if the projectile has grapple AI (aiStyle 7)
        try
        {
            var proj = new Projectile();
            proj.SetDefaults(item.shoot);
            return proj.aiStyle == 7; // Grapple AI
        }
        catch
        {
            // Defensive: if anything goes wrong, don't block the item
            return false;
        }
    }

    /// <summary>
    /// Modify damage dealt to NPCs based on active hexes.
    /// This handles melee weapon hits (not projectiles).
    /// </summary>
    public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
    {
        if (!target.boss)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        var hexes = BossHexManager.Current;
        
        // NoMeleeDamage - melee attacks deal no damage to bosses
        if (hexes.Constraint == ConstraintHex.NoMeleeDamage)
        {
            modifiers.FinalDamage *= 0f;
        }
    }

    /// <summary>
    /// Modify projectile damage dealt to NPCs based on active hexes.
    /// </summary>
    public override void ModifyHitNPCWithProj(Projectile proj, NPC target, ref NPC.HitModifiers modifiers)
    {
        if (!target.boss)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        var hexes = BossHexManager.Current;
        
        // Determine damage class of the projectile
        bool isRanged = proj.DamageType == DamageClass.Ranged || proj.DamageType.CountsAsClass(DamageClass.Ranged);
        bool isMagic = proj.DamageType == DamageClass.Magic || proj.DamageType.CountsAsClass(DamageClass.Magic);
        bool isMelee = proj.DamageType == DamageClass.Melee || proj.DamageType.CountsAsClass(DamageClass.Melee);

        // NoRangedDamage - ranged projectiles deal no damage to bosses
        if (hexes.Constraint == ConstraintHex.NoRangedDamage && isRanged)
        {
            modifiers.FinalDamage *= 0f;
        }

        // NoMagicDamage - magic projectiles deal no damage to bosses
        if (hexes.Constraint == ConstraintHex.NoMagicDamage && isMagic)
        {
            modifiers.FinalDamage *= 0f;
        }

        // NoMeleeDamage - melee projectiles deal no damage to bosses
        if (hexes.Constraint == ConstraintHex.NoMeleeDamage && isMelee)
        {
            modifiers.FinalDamage *= 0f;
        }
    }

    /// <summary>
    /// Applies active boss hexes to the player. Only runs during boss fights.
    /// </summary>
    private void ApplyBossHexes()
    {
        var hexes = BossHexManager.Current;
        if (!hexes.HasAnyHex)
            return;

        // Only apply during active boss fights
        if (!AnyBossAlive())
            return;

        // Apply flashy hexes (player-side effects)
        ApplyFlashyHex(hexes.Flashy);
        
        // Apply modifier hexes
        ApplyModifierHex(hexes.Modifier);
        
        // Apply constraint hexes
        ApplyConstraintHex(hexes.Constraint);
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
            case ModifierHex.Frail:
                // -20% max HP
                Player.statLifeMax2 = (int)(Player.statLifeMax2 * 0.8f);
                break;
                
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
            // - GlassCannon: damage modifier (partially in BossHexGlobalNPC)
            // - Marked: boss damage boost (in BossHexGlobalNPC)
            // - SwiftBoss: handled in BossHexGlobalNPC
        }
    }

    private void ApplyConstraintHex(ConstraintHex hex)
    {
        switch (hex)
        {
            case ConstraintHex.Grounded:
                // No jumping - cancel any upward velocity from jumps
                // We detect jump attempts and zero out
                if (Player.velocity.Y < 0 && Player.controlJump)
                {
                    Player.velocity.Y = 0;
                }
                // Also disable rocket boots, cloud jumps, etc.
                Player.jumpSpeedBoost = 0;
                Player.jumpBoost = false;
                break;
                
            case ConstraintHex.NoGrapple:
                // Handled separately in CanUseItem
                break;
                
            // TODO: Implement remaining constraints:
            // - NoBuffPotions: check item use
            // - PacifistHealer: special role assignment
        }
    }

    private static bool AnyBossAlive()
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var n = Main.npc[i];
            if (n.active && n.boss)
                return true;
        }
        return false;
    }
}
