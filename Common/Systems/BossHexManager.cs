using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using BossHexes.Common.Config;

namespace BossHexes.Common.Systems;

/*
 * =============================================================================
 * HEX REQUIREMENTS & TODOs
 * =============================================================================
 * 
 * TESTING CHECKLIST:
 * To test a hex: Comment out all others in ImplementedFlashyHexes (etc.),
 * rebuild mod, reload in-game, fight bosses.
 * 
 * Hexes needing testing:
 *   [ ] Blackout - does darkness actually work?
 *   [ ] TinyFastBoss - test across multiple bosses, is the 1/3 physical size + boosted movement version fun?
 *   [ ] HugeBoss - test across multiple bosses, is the 3x physical size + boosted movement version noticeable/fun?
 *   [ ] UnstableGravity - does it feel right?
 *   [ ] MeteorShower - DONE, working well at 0.35 multiplier
 * 
 * =============================================================================
 * 
 * FLASHY HEXES:
 * 
 * InvisibleBoss - PARTIALLY IMPLEMENTED
 *   - Currently: Hides the boss sprite, boss-owned projectiles, boss-linked spawned NPCs, boss head / minimap icon, and boss UI bars / hover name surfaces
 *   - TODO: Hide dust/particles spawned by boss
 *   - TODO: Confirm coverage for linked entities that have neither clean spawn provenance nor explicit parent references
 * 
 * Blackout - REVIEWED / NEEDS TESTING
 *   - Uses vanilla Blackout on the player as the source of truth
 *   - Still needs gameplay verification for whether vanilla Blackout creates the intended darkness effect
 * 
 * TimeLimit - NEEDS TUNING
 *   - Currently: Flat 3 minutes for all bosses
 *   - TODO: Get real DPS estimates by boss tier based on gear realistically available before that boss
 *   - TODO: Calculate per-boss time limits from HP / expected pre-boss DPS, not a flat feel-based timer
 *   - TODO: Scale time with player count and difficulty mode (Expert/Master)
 *   - TODO: Adjust the time limit based on the other active hexes, especially damage-denial or DPS-reducing combinations
 *   - TODO: Hex conflict prevention - if the best realistic pre-boss gear relies on a specific
 *     damage type (e.g. Daedalus Stormbow for Destroyer = ranged), don't roll
 *     TimeLimit together with a hex that invalidates that route unless the timer math already accounts for it.
 *   - NOTE: This may be too hard/noisy for BossHexes and could become its own mod or be cut entirely.
 * 
 * Reversal - IMPLEMENTED
 *   - Uses ModPlayer.SetControls() on the owning player
 *   - Swaps left/right movement input only
 *   - Leaves up/down, jump, and mount semantics alone until there is a clearer principled design
 * 
 * Mirrored - Damaging clone spawns
 *   - Spawn a "shadow" copy of the boss that mirrors its movements
 *   - Clone deals reduced damage (maybe 50%)
 *   - Clone has no HP bar, disappears when real boss dies
 *   - Visual: semi-transparent or different color tint
 *   - Implementation: Spawn a custom NPC that copies boss AI/position mirrored
 * 
 * -----------------------------------------------------------------------------
 * 
 * MODIFIER HEXES:
 * 
 * TODO: Where possible, use built-in Terraria debuffs instead of manual stat
 * modification. Debuffs show an icon to the player and feel more native.
 * Apply as a 2-tick debuff every frame for "permanent" effect during boss fight.
 * 
 * Already using buffs: Sluggish (Slow), BrokenArmor
 * Reviewed: Sluggish is already principled via vanilla Slow; remaining question is gameplay feel/testing
 * 
 * ExtraPotionSickness - IMPLEMENTED
 *   - Uses the actual Potion Sickness buff on the owning player as the source of truth
 *   - Extends newly applied/refreshed Potion Sickness once, without retroactively stretching an existing timer when the fight starts
 *   - Should make potion timing much more critical
 * 
 * SlowAttack - IMPLEMENTED
 *   - Uses ModPlayer.UseSpeedMultiplier(Item) on the player side
 *   - Treats the actual item use speed as the source of truth instead of approximating with the Slow movement debuff
 *   - Applies to attack items as a fight-wide player debuff keyed to active modifier state
 * 
 * ManaDrain - IMPLEMENTED
 *   - Uses ModPlayer.ModifyManaCost on the player side
 *   - Treats ManaDrain as a fight-wide player debuff keyed to active modifier state
 *   - Applies once when any active boss fight has ManaDrain
 * 
 * Inaccurate - IMPLEMENTED
 *   - Uses ModPlayer.ModifyShootStats(Item, ...) on the player side
 *   - Treats the actual ranged attack item and outgoing shot velocity as the source of truth
 *   - Applies once per fired ranged shot as a fight-wide player debuff keyed to active modifier state
 * 
 * Marked - Boss deals +25% damage
 *   - IMPLEMENTED: Uses the actual attacking NPC / projectile as the source of truth
 *   - Direct boss hits use ModPlayer.ModifyHitByNPC on the player
 *   - Boss projectiles use boss-fight projectile provenance captured at spawn time
 * 
 * -----------------------------------------------------------------------------
 * 
 * CONSTRAINT HEXES:
 * 
 * Damage denial constraints (NoMeleeDamage / NoRangedDamage / NoMagicDamage)
 *   - Must classify the actual hitter, not the player's currently held item
 *   - Direct hits: use Item.CountsAsClass(...) in NPC.ModifyHitByItem
 *   - Projectile hits: use Projectile.CountsAsClass(...) in NPC.ModifyHitByProjectile
 *   - Do not use raw DamageType equality; use CountsAsClass(...) so derived/custom classes work
 * 
 * NoBuffPotions - Buff potions disabled (heal/mana OK)
 *   - Block use of buff potions (Ironskin, Swiftness, Regeneration, etc.)
 *   - Allow: Healing potions, Mana potions, Recall (if not otherwise disabled)
 *   - Hook: CanUseItem, check if item.buffType > 0 and isn't a heal/mana restore
 *   - Show message when blocked: "Buff potions are disabled!"
 * 
 * PacifistHealer - One player heals teammates instead of damaging
 *   - Randomly assign one player as the "healer" at fight start
 *   - That player's attacks deal 0 damage to the boss
 *   - Instead, their hits heal nearby teammates for a portion of the damage
 *   - Announce who is the healer at fight start
 *   - Hook: ModifyHitNPCWithProj/ModifyHitNPC to zero damage
 *   - Hook: OnHitNPC to trigger healing effect on nearby players
 *   - Visual: healing particles when "attacking"
 *   - Only meaningful with 2+ players (skip or reroll in singleplayer)
 * 
 * =============================================================================
 */

public enum FlashyHex
{
    None = 0,
    InvisibleBoss,      // Boss is literally invisible
    WingClip,           // No flight
    Blackout,           // Extreme darkness
    TimeLimit,          // 3 minutes or everyone dies
    Reversal,           // Inverted controls
    TinyFastBoss,       // 1/3 size, boosted movement
    HugeBoss,           // 3x size, boosted movement
    Mirrored,           // Damaging clone spawns
    UnstableGravity,    // Gravity flips periodically
    MeteorShower,       // Falling stars damage players
}

public enum ModifierHex
{
    None = 0,
    ExtraPotionSickness,  // 3x potion sickness duration
    SlowAttack,           // Reduced attack speed
    ManaDrain,            // Mana costs +50%
    Inaccurate,           // Ranged spread increased
    SwiftBoss,            // Boss movement is boosted
    Sluggish,             // Player movement -25%
    Frail,                // Max HP -20%
    BrokenArmor,          // Defense halved
    GlassCannon,          // +50% damage dealt and taken
    Marked,               // Boss deals +25% damage
}

public enum ConstraintHex
{
    None = 0,
    NoBuffPotions,    // Buff potions disabled (heal/mana OK)
    NoRangedDamage,   // Ranged weapons deal 0
    NoMeleeDamage,    // Melee weapons deal 0
    NoMagicDamage,    // Magic weapons deal 0
    Grounded,         // No jumping
    NoGrapple,        // Hooks disabled
    PacifistHealer,   // One player heals teammates instead of damaging
}

/// <summary>
/// Holds the active hexes for a boss fight.
/// </summary>
public class ActiveHexes
{
    public FlashyHex Flashy { get; set; } = FlashyHex.None;
    public ModifierHex Modifier { get; set; } = ModifierHex.None;
    public ConstraintHex Constraint { get; set; } = ConstraintHex.None;
    
    // For time limit hex
    public int TimeLimitTicks { get; set; } = 0;
    public int TimeLimitMaxTicks { get; set; } = 0;
    
    // For unstable gravity
    public int GravityFlipTicks { get; set; } = 0;
    public int NextGravityFlipAt { get; set; } = 0;  // Target tick for next flip (0 = not set)
    
    // For meteor shower
    public int MeteorTicks { get; set; } = 0;
    
    // For pacifist healer - which player index is the healer
    public int PacifistHealerIndex { get; set; } = -1;

    public bool HasAnyHex => Flashy != FlashyHex.None || Modifier != ModifierHex.None || Constraint != ConstraintHex.None;

    public ActiveHexes CloneHexesOnly()
    {
        return new ActiveHexes
        {
            Flashy = Flashy,
            Modifier = Modifier,
            Constraint = Constraint,
        };
    }

    public void Clear()
    {
        Flashy = FlashyHex.None;
        Modifier = ModifierHex.None;
        Constraint = ConstraintHex.None;
        TimeLimitTicks = 0;
        TimeLimitMaxTicks = 0;
        GravityFlipTicks = 0;
        NextGravityFlipAt = 0;
        MeteorTicks = 0;
        PacifistHealerIndex = -1;
    }

    public List<string> GetActiveHexNames()
    {
        var names = new List<string>();
        if (Flashy != FlashyHex.None) names.Add(FormatHexName(Flashy.ToString()));
        if (Modifier != ModifierHex.None) names.Add(FormatHexName(Modifier.ToString()));
        if (Constraint != ConstraintHex.None) names.Add(FormatHexName(Constraint.ToString()));
        return names;
    }

    private static string FormatHexName(string name)
    {
        // Convert PascalCase to "Pascal Case"
        var result = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && result.Length > 0)
                result.Append(' ');
            result.Append(c);
        }
        return result.ToString();
    }
}

/// <summary>
/// Manages hex rolling and persistence per boss type.
/// </summary>
public static class BossHexManager
{
    // Persisted hexes per boss type (by NPC type ID)
    private static readonly Dictionary<int, ActiveHexes> _persistedHexes = new();

    // Active hexes for currently alive boss fights, keyed by boss root type.
    private static readonly Dictionary<int, ActiveHexes> _activeHexesByBossType = new();

    public static bool HasAnyActiveHexes => _activeHexesByBossType.Count > 0;

    public static void OnWorldLoad()
    {
        _persistedHexes.Clear();
        _activeHexesByBossType.Clear();
    }

    public static void OnBossSpawn(int bossType)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        // Already fighting this boss type
        if (_activeHexesByBossType.ContainsKey(bossType))
            return;

        ActiveHexes persistedHexes;

        // Check for persisted hexes for this boss
        if (_persistedHexes.TryGetValue(bossType, out var persisted))
        {
            persistedHexes = persisted;
        }
        else
        {
            persistedHexes = RollHexes(cfg).CloneHexesOnly();
            _persistedHexes[bossType] = persistedHexes;
        }

        var activeHexes = CreateActiveFightState(persistedHexes);
        if (activeHexes.HasAnyHex)
        {
            _activeHexesByBossType[bossType] = activeHexes;
        }
    }

    public static void OnBossDefeated(int bossType)
    {
        // Clear persistence - next time this boss is fought, re-roll
        _persistedHexes.Remove(bossType);

        _activeHexesByBossType.Remove(bossType);
    }

    public static void OnAllBossesDead()
    {
        // Just clear active fights, keep persistence
        _activeHexesByBossType.Clear();
    }

    public static bool TryGetBossRoot(NPC npc, out NPC root)
    {
        root = npc;

        if (!npc.active)
            return false;

        if (npc.boss)
            return true;

        if (npc.realLife < 0 || npc.realLife >= Main.maxNPCs)
            return false;

        var candidateRoot = Main.npc[npc.realLife];
        if (!candidateRoot.active || !candidateRoot.boss)
            return false;

        root = candidateRoot;
        return true;
    }

    public static bool TryGetActiveHexes(int bossType, out ActiveHexes hexes)
    {
        return _activeHexesByBossType.TryGetValue(bossType, out hexes);
    }

    public static bool TryGetActiveHexes(NPC npc, out ActiveHexes hexes)
    {
        hexes = null;

        if (!TryGetBossRoot(npc, out var root))
            return false;

        return TryGetActiveHexes(root.type, out hexes);
    }

    public static bool TryGetActiveBossFight(NPC npc, out int bossType, out ActiveHexes hexes)
    {
        bossType = -1;
        hexes = null;

        if (!TryGetBossRoot(npc, out var root))
            return false;

        bossType = root.type;
        return TryGetActiveHexes(bossType, out hexes);
    }

    public static bool IsPartOfCurrentBossFight(NPC npc)
    {
        return TryGetActiveHexes(npc, out _);
    }

    public static bool IsPartOfBossFight(NPC npc, int bossType)
    {
        return TryGetBossRoot(npc, out var root) && root.type == bossType;
    }

    public static bool IsBossFightActive(int bossType)
    {
        if (!_activeHexesByBossType.ContainsKey(bossType))
            return false;

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var npc = Main.npc[i];
            if (npc.active && IsPartOfBossFight(npc, bossType))
                return true;
        }

        return false;
    }

    public static bool IsCurrentBossFightActive()
    {
        foreach (var bossType in _activeHexesByBossType.Keys)
        {
            if (IsBossFightActive(bossType))
                return true;
        }

        return false;
    }

    public static bool IsFlashyActive(FlashyHex hex)
    {
        foreach (var (bossType, activeHexes) in _activeHexesByBossType)
        {
            if (activeHexes.Flashy == hex && IsBossFightActive(bossType))
                return true;
        }

        return false;
    }

    public static bool IsModifierActive(ModifierHex hex)
    {
        foreach (var (bossType, activeHexes) in _activeHexesByBossType)
        {
            if (activeHexes.Modifier == hex && IsBossFightActive(bossType))
                return true;
        }

        return false;
    }

    public static bool IsConstraintActive(ConstraintHex hex)
    {
        foreach (var (bossType, activeHexes) in _activeHexesByBossType)
        {
            if (activeHexes.Constraint == hex && IsBossFightActive(bossType))
                return true;
        }

        return false;
    }

    public static List<KeyValuePair<int, ActiveHexes>> GetActiveBossFights()
    {
        return new List<KeyValuePair<int, ActiveHexes>>(_activeHexesByBossType);
    }

    public static bool ReconcileActiveBossFights()
    {
        var inactiveBossTypes = new List<int>();

        foreach (var bossType in _activeHexesByBossType.Keys)
        {
            if (!IsBossFightActive(bossType))
                inactiveBossTypes.Add(bossType);
        }

        foreach (var bossType in inactiveBossTypes)
        {
            _activeHexesByBossType.Remove(bossType);
        }

        return inactiveBossTypes.Count > 0;
    }

    private static readonly FlashyHex[] ImplementedFlashyHexes = new[]
    {
        FlashyHex.InvisibleBoss,
        FlashyHex.WingClip,
        FlashyHex.Blackout,
        // FlashyHex.TimeLimit,  // DISABLED: Needs per-boss tuning, currently impossible for some bosses
        FlashyHex.Reversal,
        FlashyHex.TinyFastBoss,
        FlashyHex.HugeBoss,
        FlashyHex.UnstableGravity,
        FlashyHex.MeteorShower,
        // NOT implemented: Mirrored
    };

    private static readonly ModifierHex[] ImplementedModifierHexes = new[]
    {
        ModifierHex.ExtraPotionSickness,
        ModifierHex.SwiftBoss,
        ModifierHex.Sluggish,
        ModifierHex.Frail,
        ModifierHex.BrokenArmor,
        ModifierHex.GlassCannon,
        ModifierHex.Marked,
        ModifierHex.ManaDrain,
        ModifierHex.SlowAttack,
        ModifierHex.Inaccurate,
    };

    private static readonly ConstraintHex[] ImplementedConstraintHexes = new[]
    {
        ConstraintHex.NoBuffPotions,
        ConstraintHex.NoRangedDamage,
        ConstraintHex.NoMeleeDamage,
        ConstraintHex.NoMagicDamage,
        ConstraintHex.Grounded,
        ConstraintHex.NoGrapple,
        // NOT implemented: PacifistHealer
    };

    private static FlashyHex RollFlashyHex()
    {
        return ImplementedFlashyHexes[Main.rand.Next(ImplementedFlashyHexes.Length)];
    }

    private static ModifierHex RollModifierHex()
    {
        return ImplementedModifierHexes[Main.rand.Next(ImplementedModifierHexes.Length)];
    }

    private static ConstraintHex RollConstraintHex()
    {
        return ImplementedConstraintHexes[Main.rand.Next(ImplementedConstraintHexes.Length)];
    }

    private static ActiveHexes RollHexes(BossHexesConfig cfg)
    {
        var hexes = new ActiveHexes();

        // Always roll one hex per enabled category
        if (cfg.EnableFlashyHexes) hexes.Flashy = RollFlashyHex();
        if (cfg.EnableModifierHexes) hexes.Modifier = RollModifierHex();
        if (cfg.EnableConstraintHexes) hexes.Constraint = RollConstraintHex();

        return hexes;
    }

    private static int CountActivePlayers()
    {
        int count = 0;
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            // Check active AND that the player has a name (real player, not empty slot)
            if (p?.active == true && !string.IsNullOrEmpty(p.name))
                count++;
        }
        return Math.Max(1, count);
    }

    /// <summary>
    /// Send hex state to clients. Called from server after rolling hexes.
    /// </summary>
    public static void SendSync(Mod mod, int toWho = -1, int ignoreClient = -1)
    {
        if (Main.netMode == NetmodeID.SinglePlayer)
            return;

        ModPacket packet = mod.GetPacket();
        packet.Write((byte)BossHexes.MessageType.SyncHexes);
        packet.Write(_activeHexesByBossType.Count);

        foreach (var (bossType, activeHexes) in _activeHexesByBossType)
        {
            packet.Write(bossType);
            packet.Write((byte)activeHexes.Flashy);
            packet.Write((byte)activeHexes.Modifier);
            packet.Write((byte)activeHexes.Constraint);
            packet.Write(activeHexes.TimeLimitTicks);
            packet.Write(activeHexes.TimeLimitMaxTicks);
            packet.Write(activeHexes.PacifistHealerIndex);
        }

        packet.Send(toWho, ignoreClient);
    }

    /// <summary>
    /// Receive hex state from server. Called on clients.
    /// </summary>
    public static void ReceiveSync(BinaryReader reader)
    {
        _activeHexesByBossType.Clear();

        int activeFightCount = reader.ReadInt32();
        for (int i = 0; i < activeFightCount; i++)
        {
            int bossType = reader.ReadInt32();
            var activeHexes = new ActiveHexes
            {
                Flashy = (FlashyHex)reader.ReadByte(),
                Modifier = (ModifierHex)reader.ReadByte(),
                Constraint = (ConstraintHex)reader.ReadByte(),
                TimeLimitTicks = reader.ReadInt32(),
                TimeLimitMaxTicks = reader.ReadInt32(),
                PacifistHealerIndex = reader.ReadInt32(),
            };

            _activeHexesByBossType[bossType] = activeHexes;
        }
    }

    private static ActiveHexes CreateActiveFightState(ActiveHexes template)
    {
        var activeHexes = template.CloneHexesOnly();

        if (activeHexes.Flashy == FlashyHex.TimeLimit)
        {
            activeHexes.TimeLimitMaxTicks = 3 * 60 * 60; // 3 minutes
            activeHexes.TimeLimitTicks = activeHexes.TimeLimitMaxTicks;
        }

        if (activeHexes.Constraint == ConstraintHex.PacifistHealer && CountActivePlayers() > 1)
        {
            activeHexes.PacifistHealerIndex = Main.rand.Next(Main.maxPlayers);
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                int idx = (activeHexes.PacifistHealerIndex + i) % Main.maxPlayers;
                if (Main.player[idx]?.active == true && !Main.player[idx].dead)
                {
                    activeHexes.PacifistHealerIndex = idx;
                    break;
                }
            }
        }

        return activeHexes;
    }

    /// <summary>
    /// Save persisted hex rolls to world data.
    /// Only saves the three hex enum values per boss — runtime state (timers etc.) is per-fight.
    /// </summary>
    public static void SaveWorldData(TagCompound tag)
    {
        var list = new List<TagCompound>();
        foreach (var (bossType, hexes) in _persistedHexes)
        {
            list.Add(new TagCompound
            {
                ["bossType"] = bossType,
                ["flashy"] = (byte)hexes.Flashy,
                ["modifier"] = (byte)hexes.Modifier,
                ["constraint"] = (byte)hexes.Constraint,
            });
        }
        tag["bossHexes"] = list;
    }

    /// <summary>
    /// Load persisted hex rolls from world data.
    /// </summary>
    public static void LoadWorldData(TagCompound tag)
    {
        _persistedHexes.Clear();
        if (!tag.ContainsKey("bossHexes"))
            return;

        var list = tag.GetList<TagCompound>("bossHexes");
        foreach (var entry in list)
        {
            int bossType = entry.GetInt("bossType");
            var hexes = new ActiveHexes
            {
                Flashy = (FlashyHex)entry.GetByte("flashy"),
                Modifier = (ModifierHex)entry.GetByte("modifier"),
                Constraint = (ConstraintHex)entry.GetByte("constraint"),
            };
            _persistedHexes[bossType] = hexes;
        }
    }
}
