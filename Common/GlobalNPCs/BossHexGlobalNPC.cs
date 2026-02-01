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

        var hexes = BossHexManager.Current;
        if (!hexes.HasAnyHex)
            return;

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
        if (!npc.boss)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        var hexes = BossHexManager.Current;
        if (!hexes.HasAnyHex)
            return;

        // Apply boss-side effects
        ApplyBossFlashyEffects(npc, hexes);
        ApplyBossModifierEffects(npc, hexes);
    }

    public override void PostAI(NPC npc)
    {
        if (!npc.boss)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        var hexes = BossHexManager.Current;

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
        if (!npc.boss)
            return true;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return true;

        var hexes = BossHexManager.Current;

        // InvisibleBoss: don't draw the boss at all
        if (hexes.Flashy == FlashyHex.InvisibleBoss)
        {
            return false; // Skip drawing
        }

        return true;
    }

    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
    {
        if (!npc.boss)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        var hexes = BossHexManager.Current;

        // GlassCannon: boss takes 50% more damage
        if (hexes.Modifier == ModifierHex.GlassCannon)
        {
            modifiers.FinalDamage *= 1.5f;
        }
    }

    public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
    {
        if (!npc.boss)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        var hexes = BossHexManager.Current;

        // GlassCannon: boss takes 50% more damage
        if (hexes.Modifier == ModifierHex.GlassCannon)
        {
            modifiers.FinalDamage *= 1.5f;
        }
    }

    private void ApplyBossFlashyEffects(NPC npc, ActiveHexes hexes)
    {
        // TinyFastBoss: 2x speed (scale handled in OnSpawn)
        if (hexes.Flashy == FlashyHex.TinyFastBoss)
        {
            ApplySpeedMultiplier(npc, 2f, 25f);
        }
        // HugeBoss: 1.75x speed (scale handled in OnSpawn)
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
        // SwiftBoss: boss moves/attacks 25% faster
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

        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral("Boss defeated! Hex cleared."), Color.LimeGreen);
        else
            Main.NewText("Boss defeated! Hex cleared.", Color.LimeGreen);
    }
}
