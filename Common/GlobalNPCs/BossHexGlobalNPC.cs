using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using BossHexes.Common.Config;
using BossHexes.Common.Systems;

namespace BossHexes.Common.GlobalNPCs;

/// <summary>
/// Applies hex effects to bosses during boss fights.
/// Handles spawning logic and visual/behavioral modifications.
/// </summary>
public sealed class BossHexGlobalNPC : GlobalNPC
{
    // Instance data per NPC - needed for per-NPC state
    public override bool InstancePerEntity => true;

    // Track if we've applied initial scale to this boss
    private bool _appliedInitialScale;
    private float _originalScale = 1f;

    public override void OnSpawn(NPC npc, Terraria.DataStructures.IEntitySource source)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        if (!npc.boss)
            return;

        // Trigger hex rolling via the manager
        BossHexManager.OnBossSpawn(npc.type);

        // Only announce once per boss spawn (server or singleplayer)
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        if (!BossHexManager.TryGetActiveHexes(npc.type, out var hexes) || !hexes.HasAnyHex)
            return;

        // Sync hex state to all clients
        if (Main.netMode == NetmodeID.Server)
        {
            BossHexManager.SendSync(Mod, -1, -1);
        }

        // Announce all active hexes
        var hexNames = hexes.GetActiveHexNames();
        if (hexNames.Count == 0)
            return;

        string hexList = string.Join(", ", hexNames);
        string message = hexNames.Count == 1
            ? $"Boss Hex: {hexList}"
            : $"Boss Hexes: {hexList}";

        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), Color.Orange);
        else
            Main.NewText(message, Color.Orange);

        // Announce special conditions
        if (hexes.Flashy == FlashyHex.TimeLimit)
        {
            string timeMsg = "You have 3 minutes to defeat the boss!";
            if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(timeMsg), Color.Red);
            else
                Main.NewText(timeMsg, Color.Red);
        }

        if (hexes.Constraint == ConstraintHex.PacifistHealer && hexes.PacifistHealerIndex >= 0)
        {
            var healer = Main.player[hexes.PacifistHealerIndex];
            if (healer?.active == true)
            {
                string healerMsg = $"{healer.name} is the Pacifist Healer! They cannot damage the boss but heal allies.";
                if (Main.netMode == NetmodeID.Server)
                    ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(healerMsg), Color.LightGreen);
                else
                    Main.NewText(healerMsg, Color.LightGreen);
            }
        }
    }

    public override void AI(NPC npc)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        if (!BossHexManager.TryGetActiveHexes(npc, out var hexes))
            return;

        if (!hexes.HasAnyHex)
            return;

        // Apply boss-side effects
        ApplyBossFlashyEffects(npc, hexes);
        ApplyBossModifierEffects(npc, hexes);
    }

    public override void PostAI(NPC npc)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        if (!BossHexManager.TryGetActiveHexes(npc, out var hexes))
            return;

        // Apply scale changes (only once per boss)
        if (!_appliedInitialScale && hexes.HasAnyHex)
        {
            _originalScale = npc.scale;
            _appliedInitialScale = true;

            if (hexes.Flashy == FlashyHex.TinyFastBoss)
            {
                npc.scale = _originalScale * 0.33f;  // 1/3 size
            }
            else if (hexes.Flashy == FlashyHex.HugeBoss)
            {
                npc.scale = _originalScale * 3f;  // 3x size
            }
        }
    }

    public override bool PreDraw(NPC npc, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return true;

        if (!BossHexManager.TryGetActiveHexes(npc, out var hexes))
            return true;

        // InvisibleBoss: don't draw the boss at all
        if (hexes.Flashy == FlashyHex.InvisibleBoss)
        {
            return false; // Skip drawing
        }

        return true;
    }

    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
    {
        if (!ShouldApplyHitEffects(npc, out var hexes))
            return;

        ApplyBossDamageTakenModifier(ref modifiers, hexes);

        if (ShouldBlockProjectileDamage(projectile, hexes.Constraint))
            modifiers.FinalDamage *= 0f;
    }

    public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
    {
        if (!ShouldApplyHitEffects(npc, out var hexes))
            return;

        ApplyBossDamageTakenModifier(ref modifiers, hexes);

        if (ShouldBlockItemDamage(item, hexes.Constraint))
            modifiers.FinalDamage *= 0f;
    }

    private static bool ShouldApplyHitEffects(NPC npc, out ActiveHexes hexes)
    {
        hexes = null;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.TryGetActiveHexes(npc, out hexes);
    }

    private static void ApplyBossDamageTakenModifier(ref NPC.HitModifiers modifiers, ActiveHexes hexes)
    {
        if (hexes.Modifier == ModifierHex.GlassCannon)
            modifiers.FinalDamage *= 1.5f;
    }

    private static bool ShouldBlockItemDamage(Item item, ConstraintHex constraint)
    {
        return constraint switch
        {
            ConstraintHex.NoMeleeDamage => item.CountsAsClass(DamageClass.Melee),
            ConstraintHex.NoRangedDamage => item.CountsAsClass(DamageClass.Ranged),
            ConstraintHex.NoMagicDamage => item.CountsAsClass(DamageClass.Magic),
            _ => false,
        };
    }

    private static bool ShouldBlockProjectileDamage(Projectile projectile, ConstraintHex constraint)
    {
        return constraint switch
        {
            ConstraintHex.NoMeleeDamage => projectile.CountsAsClass(DamageClass.Melee),
            ConstraintHex.NoRangedDamage => projectile.CountsAsClass(DamageClass.Ranged),
            ConstraintHex.NoMagicDamage => projectile.CountsAsClass(DamageClass.Magic),
            _ => false,
        };
    }

    private void ApplyBossFlashyEffects(NPC npc, ActiveHexes hexes)
    {
        // TinyFastBoss: boosted movement speed (scale handled in PostAI)
        if (hexes.Flashy == FlashyHex.TinyFastBoss)
        {
            ApplySpeedMultiplier(npc, 2f, 25f);
        }
        // HugeBoss: boosted movement speed (scale handled in PostAI)
        else if (hexes.Flashy == FlashyHex.HugeBoss)
        {
            ApplySpeedMultiplier(npc, 1.75f, 22f);
        }
    }

    private void ApplySpeedMultiplier(NPC npc, float speedMult, float maxSpeed)
    {
        if (npc.velocity.Length() > 0.1f && npc.velocity.Length() < maxSpeed)
        {
            float currentSpeed = npc.velocity.Length();
            float targetSpeed = Math.Min(currentSpeed * speedMult, maxSpeed);
            // Only apply a small boost per frame to avoid jitter
            npc.velocity = Vector2.Normalize(npc.velocity) * 
                MathHelper.Lerp(currentSpeed, targetSpeed, 0.02f);
        }
    }

    private void ApplyBossModifierEffects(NPC npc, ActiveHexes hexes)
    {
        // SwiftBoss: boosted boss movement speed
        if (hexes.Modifier == ModifierHex.SwiftBoss)
        {
            float speedMult = 1.25f;
            float maxSpeed = 18f;

            if (npc.velocity.Length() > 0.1f && npc.velocity.Length() < maxSpeed)
            {
                float currentSpeed = npc.velocity.Length();
                float targetSpeed = Math.Min(currentSpeed * speedMult, maxSpeed);
                npc.velocity = Vector2.Normalize(npc.velocity) * 
                    MathHelper.Lerp(currentSpeed, targetSpeed, 0.02f);
            }
        }
    }

    /// <summary>
    /// Called when an NPC is killed (not when it despawns).
    /// This is the correct place to detect boss defeat.
    /// </summary>
    public override void OnKill(NPC npc)
    {
        if (!npc.boss)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        // Only process on server/singleplayer
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        // Clear hex persistence for this boss type
        BossHexManager.OnBossDefeated(npc.type);

        // Sync cleared state to clients
        if (Main.netMode == NetmodeID.Server)
        {
            BossHexManager.SendSync(Mod, -1, -1);
        }

        string clearMessage = BossHexManager.HasAnyActiveHexes
            ? "Boss defeated! Their hexes cleared."
            : "Boss defeated! All active hexes cleared.";

        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(clearMessage), Color.LimeGreen);
        else
            Main.NewText(clearMessage, Color.LimeGreen);
    }
}
