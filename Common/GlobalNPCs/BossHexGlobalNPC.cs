using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using BossHexes.Common.Config;
using BossHexes.Common.GlobalProjectiles;
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

    // Track if we've applied the one-time size mutation to this NPC
    private bool _appliedInitialScale;
    private float _originalScale = 1f;
    private int _originalWidth;
    private int _originalHeight;
    private int _sourceBossType = -1;
    private int _sourceEncounterId = -1;

    public int SourceBossType => _sourceBossType;
    public int SourceEncounterId => _sourceEncounterId;

    public override void OnSpawn(NPC npc, Terraria.DataStructures.IEntitySource source)
    {
        (_sourceBossType, _sourceEncounterId) = ResolveSourceFight(source);
        if (_sourceBossType < 0 && TryResolveFightFromNpcReferences(npc, out int referencedBossType, out int referencedEncounterId))
        {
            _sourceBossType = referencedBossType;
            _sourceEncounterId = referencedEncounterId;
        }

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        if (!npc.boss)
            return;

        if (!BossHexManager.TryGetBossRoot(npc, out var root) || root.whoAmI != npc.whoAmI)
            return;

        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        // Trigger hex rolling via the manager only where fight state is authoritative.
        bool activatedFight = BossHexManager.OnBossSpawn(root.type, root.whoAmI);
        if (!activatedFight)
            return;

        if (Main.netMode == NetmodeID.Server)
            BossHexManager.SendSync(Mod, -1, -1);

        if (BossHexManager.TryGetActiveHexes(root.type, out var hexes))
            BossHexesState.AnnounceActivatedFight(hexes);
    }

    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        bool hasSourceBossType = _sourceBossType >= 0;
        bool hasSourceEncounterId = hasSourceBossType && _sourceEncounterId >= 0;

        bitWriter.WriteBit(hasSourceBossType);
        bitWriter.WriteBit(hasSourceEncounterId);

        if (hasSourceBossType)
        {
            binaryWriter.Write(_sourceBossType);

            if (hasSourceEncounterId)
                binaryWriter.Write(_sourceEncounterId);
        }
    }

    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
    {
        bool hasSourceBossType = bitReader.ReadBit();
        bool hasSourceEncounterId = bitReader.ReadBit();

        if (!hasSourceBossType)
        {
            _sourceBossType = -1;
            _sourceEncounterId = -1;
            return;
        }

        _sourceBossType = binaryReader.ReadInt32();
        _sourceEncounterId = hasSourceEncounterId
            ? binaryReader.ReadInt32()
            : -1;
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

        // Apply size changes (only once per boss)
        if (!_appliedInitialScale && (hexes.Flashy == FlashyHex.TinyFastBoss || hexes.Flashy == FlashyHex.HugeBoss))
        {
            _originalScale = npc.scale;
            _originalWidth = npc.width;
            _originalHeight = npc.height;
            _appliedInitialScale = true;

            if (hexes.Flashy == FlashyHex.TinyFastBoss)
            {
                ApplySizeMultiplier(npc, 0.33f); // 1/3 size
            }
            else if (hexes.Flashy == FlashyHex.HugeBoss)
            {
                ApplySizeMultiplier(npc, 3f); // 3x size
            }
        }
    }

    public override bool PreDraw(NPC npc, Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return true;

        if (!TryGetVisibilityHexes(npc, out var hexes))
            return true;

        // InvisibleBoss: don't draw the boss at all
        if (hexes.Flashy == FlashyHex.InvisibleBoss)
        {
            return false; // Skip drawing
        }

        return true;
    }

    public override void BossHeadSlot(NPC npc, ref int index)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        if (!TryGetVisibilityHexes(npc, out var hexes))
            return;

        if (hexes.Flashy == FlashyHex.InvisibleBoss)
            index = -1;
    }

    public override bool? DrawHealthBar(NPC npc, byte hbPosition, ref float scale, ref Vector2 position)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return null;

        if (!TryGetVisibilityHexes(npc, out var hexes))
            return null;

        if (hexes.Flashy == FlashyHex.InvisibleBoss)
            return false;

        return null;
    }

    public override bool PreHoverInteract(NPC npc, bool mouseIntersects)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return true;

        if (!TryGetVisibilityHexes(npc, out var hexes))
            return true;

        if (hexes.Flashy == FlashyHex.InvisibleBoss)
            return false;

        return true;
    }

    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
    {
        if (!ShouldApplyBossTargetHitEffects(npc, out var hexes))
            return;

        ApplyBossDamageTakenModifier(ref modifiers, hexes);

        if (ShouldBlockProjectileDamage(projectile, hexes.Constraint))
            modifiers.FinalDamage *= 0f;
    }

    public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
    {
        if (!ShouldApplyBossTargetHitEffects(npc, out var hexes))
            return;

        ApplyBossDamageTakenModifier(ref modifiers, hexes);

        if (ShouldBlockItemDamage(item, hexes.Constraint))
            modifiers.FinalDamage *= 0f;
    }

    private static bool ShouldApplyBossTargetHitEffects(NPC npc, out ActiveHexes hexes)
    {
        hexes = null;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return false;

        return BossHexManager.TryGetActiveBossFight(npc, out _, out hexes);
    }

    private bool TryGetVisibilityHexes(NPC npc, out ActiveHexes hexes)
    {
        return TryGetCurrentFightHexes(npc, out _, out hexes);
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
        // TinyFastBoss: boosted movement speed (size handled in PostAI)
        if (hexes.Flashy == FlashyHex.TinyFastBoss)
        {
            ApplySpeedMultiplier(npc, 2f, 25f);
        }
        // HugeBoss: boosted movement speed (size handled in PostAI)
        else if (hexes.Flashy == FlashyHex.HugeBoss)
        {
            ApplySpeedMultiplier(npc, 1.75f, 22f);
        }
    }

    private void ApplySizeMultiplier(NPC npc, float sizeMultiplier)
    {
        Vector2 center = npc.Center;

        npc.scale = _originalScale * sizeMultiplier;
        npc.width = Math.Max(1, (int)Math.Round(_originalWidth * sizeMultiplier));
        npc.height = Math.Max(1, (int)Math.Round(_originalHeight * sizeMultiplier));
        npc.Center = center;
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

        if (!BossHexManager.TryGetBossRoot(npc, out var root) || root.whoAmI != npc.whoAmI)
            return;

        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        // Only process on server/singleplayer
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        bool clearedHexes = !BossHexManager.HasOtherActiveBossRootOfType(root.type, root.whoAmI);
        int endedEncounterId = -1;

        if (clearedHexes && BossHexManager.TryGetActiveHexes(root.type, out var activeHexes))
            endedEncounterId = activeHexes.EncounterId;

        // Clear hex persistence for this boss type
        BossHexManager.OnBossDefeated(root.type, root.whoAmI);

        if (clearedHexes)
            ModContent.GetInstance<BossHexesState>().OnBossFightEnded(root.type, endedEncounterId);

        // Sync cleared state to clients
        if (Main.netMode == NetmodeID.Server)
        {
            BossHexManager.SendSync(Mod, -1, -1);
        }

        if (!clearedHexes)
            return;

        string clearMessage = BossHexManager.HasAnyActiveHexes
            ? "Boss defeated! Their hexes cleared."
            : "Boss defeated! All active hexes cleared.";

        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(clearMessage), Color.LimeGreen);
        else
            Main.NewText(clearMessage, Color.LimeGreen);
    }

    private static (int BossType, int EncounterId) ResolveSourceFight(IEntitySource source)
    {
        if (TryGetSourceFight(source, out int bossType, out int encounterId))
            return (bossType, encounterId);

        if (source is IEntitySource_OnHit onHit && TryGetSourceFight(onHit.Attacker, out bossType, out encounterId))
            return (bossType, encounterId);

        if (source is IEntitySource_OnHurt onHurt && TryGetSourceFight(onHurt.Attacker, out bossType, out encounterId))
            return (bossType, encounterId);

        return (-1, -1);
    }

    private static bool TryGetSourceFight(IEntitySource source, out int bossType, out int encounterId)
    {
        bossType = -1;
        encounterId = -1;

        if (source is not EntitySource_Parent parent)
            return false;

        return TryGetSourceFight(parent.Entity, out bossType, out encounterId);
    }

    private static bool TryGetSourceFight(Entity entity, out int bossType, out int encounterId)
    {
        bossType = -1;
        encounterId = -1;

        if (entity is NPC npc)
            return TryGetSourceFight(npc, out bossType, out encounterId);

        if (entity is Projectile projectile)
        {
            var bossSource = projectile.GetGlobalProjectile<BossFightSourceProjectile>();
            if (!bossSource.IsFromCurrentBossFight)
                return false;

            bossType = bossSource.SourceBossType;
            encounterId = bossSource.SourceEncounterId;
            return true;
        }

        return false;
    }

    private static bool TryGetSourceFight(NPC npc, out int bossType, out int encounterId)
    {
        bossType = -1;
        encounterId = -1;

        if (TryGetCurrentFightHexes(npc, out bossType, out encounterId, out _))
            return true;

        if (!BossHexManager.TryGetBossRoot(npc, out var root))
            return false;

        if (!ModContent.GetInstance<BossHexesState>().TryEnsureActiveFight(root.type, root.whoAmI, out encounterId, out _))
            return false;

        bossType = root.type;
        return true;
    }

    public static bool TryGetCurrentFightHexes(NPC npc, out int bossType, out ActiveHexes hexes)
    {
        return TryGetCurrentFightHexes(npc, out bossType, out _, out hexes);
    }

    public static bool TryGetCurrentFightHexes(NPC npc, out int bossType, out int encounterId, out ActiveHexes hexes)
    {
        if (BossHexManager.TryGetActiveBossFight(npc, out bossType, out encounterId, out hexes))
            return true;

        return npc.GetGlobalNPC<BossHexGlobalNPC>().TryGetLinkedFightHexes(out bossType, out encounterId, out hexes);
    }

    public bool TryGetLinkedFightHexes(out int bossType, out ActiveHexes hexes)
    {
        return TryGetLinkedFightHexes(out bossType, out _, out hexes);
    }

    public bool TryGetLinkedFightHexes(out int bossType, out int encounterId, out ActiveHexes hexes)
    {
        bossType = -1;
        encounterId = -1;
        hexes = null;

        if (_sourceBossType < 0 || _sourceEncounterId < 0)
            return false;

        if (!BossHexManager.IsBossFightActive(_sourceBossType, _sourceEncounterId))
            return false;

        if (!BossHexManager.TryGetActiveHexes(_sourceBossType, _sourceEncounterId, out hexes))
            return false;

        bossType = _sourceBossType;
        encounterId = _sourceEncounterId;
        return true;
    }

    private static bool TryResolveFightFromNpcReferences(NPC npc, out int bossType, out int encounterId)
    {
        bossType = -1;
        encounterId = -1;

        var candidateFights = new HashSet<(int BossType, int EncounterId)>();

        TryAddReferencedFight(candidateFights, npc.realLife, npc.whoAmI);

        foreach (float aiValue in npc.ai)
        {
            if (!TryGetReferencedNpcIndexFromAi(aiValue, out int npcIndex))
                continue;

            TryAddReferencedFight(candidateFights, npcIndex, npc.whoAmI);
        }

        if (candidateFights.Count != 1)
            return false;

        foreach (var candidateFight in candidateFights)
        {
            bossType = candidateFight.BossType;
            encounterId = candidateFight.EncounterId;
            return true;
        }

        return false;
    }

    private static void TryAddReferencedFight(HashSet<(int BossType, int EncounterId)> candidateFights, int npcIndex, int selfIndex)
    {
        if (npcIndex < 0 || npcIndex >= Main.maxNPCs || npcIndex == selfIndex)
            return;

        NPC referencedNpc = Main.npc[npcIndex];
        if (!referencedNpc.active)
            return;

        if (TryGetSourceFight(referencedNpc, out int bossType, out int encounterId))
            candidateFights.Add((bossType, encounterId));
    }

    private static bool TryGetReferencedNpcIndexFromAi(float aiValue, out int npcIndex)
    {
        npcIndex = -1;

        if (float.IsNaN(aiValue) || float.IsInfinity(aiValue))
            return false;

        if (Math.Abs(aiValue) < 0.001f)
            return false;

        int roundedIndex = (int)Math.Round(aiValue);
        if (Math.Abs(aiValue - roundedIndex) > 0.001f)
            return false;

        if (roundedIndex < 0 || roundedIndex >= Main.maxNPCs)
            return false;

        npcIndex = roundedIndex;
        return true;
    }
}
