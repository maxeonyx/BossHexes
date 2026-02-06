using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
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
 *   [ ] TinyFastBoss - test across multiple bosses, is 1/3 size + 2x speed fun?
 *   [ ] HugeBoss - test across multiple bosses, is 2x size noticeable/fun?
 *   [ ] UnstableGravity - does it feel right?
 *   [ ] MeteorShower - DONE, working well at 0.35 multiplier
 * 
 * =============================================================================
 * 
 * FLASHY HEXES:
 * 
 * InvisibleBoss - PARTIALLY IMPLEMENTED
 *   - Currently: Only hides the boss sprite (alpha = 0)
 *   - TODO: Hide boss projectiles (or make them semi-transparent)
 *   - TODO: Hide dust/particles spawned by boss
 *   - TODO: Hide minimap icon
 *   - TODO: Consider hiding the bottom health bar (or just the name?)
 *   - TODO: Handle multi-segment bosses (Eater of Worlds, Destroyer)
 *   - TODO: Handle boss minions (Skeletron hands, Plantera tentacles, etc.)
 * 
 * Blackout - NEEDS TESTING
 *   - Verify it actually works and creates the intended darkness effect
 * 
 * TimeLimit - NEEDS TUNING
 *   - Currently: Flat 3 minutes for all bosses
 *   - TODO: Get real DPS estimates by boss tier, based on gear available before that boss
 *   - TODO: Calculate per-boss time limits from HP / expected DPS
 *   - TODO: Scale time with player count and difficulty mode (Expert/Master)
 *   - TODO: Extend time if Pacifist Healer hex is active (one less damage dealer)
 *   - TODO: Hex conflict prevention - if the best pre-boss gear relies on a specific
 *     damage type (e.g. Daedalus Stormbow for Destroyer = ranged), don't roll
 *     TimeLimit + NoRangedDamage together. Need a conflict matrix.
 * 
 * Reversal - NOT IMPLEMENTED
 *   - Left/right movement keys are swapped
 *   - Possibly up/down too (jump vs down)
 *   - Should feel disorienting but learnable
 *   - Hook: ModifyPlayer input or velocity manipulation in PostUpdate
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
 * Need to check: Frail (is there a built-in?), SlowAttack, ManaDrain, etc.
 * 
 * ExtraPotionSickness - 3x potion sickness duration
 *   - When player uses a healing potion, multiply the PotionSickness debuff duration by 3
 *   - Hook: OnConsumeMana or detect buff application and extend it
 *   - Should make potion timing much more critical
 * 
 * SlowAttack - Reduced attack speed
 *   - Reduce player attack speed by ~30-40%
 *   - Hook: ModifyWeaponSpeed or similar
 *   - Affects all weapon types (melee swing, ranged fire rate, spell cast)
 * 
 * ManaDrain - Mana costs +50%
 *   - All mana costs increased by 50%
 *   - Hook: ModifyManaCost
 *   - Makes mage builds more challenging, need to manage mana carefully
 * 
 * Inaccurate - Ranged spread increased
 *   - Add random spread/deviation to all ranged projectiles
 *   - Hook: ModifyShootStats or Shoot override
 *   - Spread angle: maybe ±5-10 degrees
 *   - Makes precision shots unreliable
 * 
 * Marked - Boss deals +25% damage
 *   - All boss attacks deal 25% more damage to players
 *   - Hook: ModifyHitByNPC on the player, check if attacker is boss
 *   - Stacks with GlassCannon if both active? (probably not, different categories)
 * 
 * -----------------------------------------------------------------------------
 * 
 * CONSTRAINT HEXES:
 * 
 * NoMeleeDamage - POTENTIAL BUG
 *   - Currently only blocks melee *projectiles* via ModifyHitNPCWithProj
 *   - Direct sword swings (non-projectile melee) use ModifyHitNPC instead
 *   - TODO: Add ModifyHitNPC hook to also block direct melee hits
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
    TinyFastBoss,       // 1/3 size, 2x speed
    HugeBoss,           // 3x size, 1.75x speed
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
    SwiftBoss,            // Boss moves/attacks 25% faster
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
    
    // Current active hexes for the ongoing fight
    public static ActiveHexes Current { get; private set; } = new();
    
    // Track which boss type is currently being fought
    public static int CurrentBossType { get; private set; } = -1;

    public static void OnWorldLoad()
    {
        _persistedHexes.Clear();
        Current = new ActiveHexes();
        CurrentBossType = -1;
    }

    public static void OnBossSpawn(int bossType)
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        // Already fighting this boss type
        if (CurrentBossType == bossType && Current.HasAnyHex)
            return;

        CurrentBossType = bossType;

        // Check for persisted hexes for this boss
        if (_persistedHexes.TryGetValue(bossType, out var persisted))
        {
            Current = persisted;
            return;
        }

        // Roll new hexes based on player count
        int playerCount = CountActivePlayers();
        Current = RollHexes(playerCount, cfg);
        
        // Set up time limit if rolled
        if (Current.Flashy == FlashyHex.TimeLimit)
        {
            Current.TimeLimitMaxTicks = 3 * 60 * 60; // 3 minutes
            Current.TimeLimitTicks = Current.TimeLimitMaxTicks;
        }
        
        // Set up pacifist healer if rolled
        if (Current.Constraint == ConstraintHex.PacifistHealer && playerCount > 1)
        {
            Current.PacifistHealerIndex = Main.rand.Next(Main.maxPlayers);
            // Find a valid player
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                int idx = (Current.PacifistHealerIndex + i) % Main.maxPlayers;
                if (Main.player[idx]?.active == true && !Main.player[idx].dead)
                {
                    Current.PacifistHealerIndex = idx;
                    break;
                }
            }
        }

        // Persist for this boss type
        _persistedHexes[bossType] = Current;
    }

    public static void OnBossDefeated(int bossType)
    {
        // Clear persistence - next time this boss is fought, re-roll
        _persistedHexes.Remove(bossType);
        
        if (CurrentBossType == bossType)
        {
            Current = new ActiveHexes();
            CurrentBossType = -1;
        }
    }

    public static void OnAllBossesDead()
    {
        // Just clear current, keep persistence
        Current = new ActiveHexes();
        CurrentBossType = -1;
    }

    private static readonly FlashyHex[] ImplementedFlashyHexes = new[]
    {
        FlashyHex.InvisibleBoss,
        FlashyHex.WingClip,
        FlashyHex.Blackout,
        // FlashyHex.TimeLimit,  // DISABLED: Needs per-boss tuning, currently impossible for some bosses
        FlashyHex.TinyFastBoss,
        FlashyHex.HugeBoss,
        FlashyHex.UnstableGravity,
        FlashyHex.MeteorShower,
        // NOT implemented: Reversal, Mirrored
    };

    private static readonly ModifierHex[] ImplementedModifierHexes = new[]
    {
        ModifierHex.SwiftBoss,
        ModifierHex.Sluggish,
        ModifierHex.Frail,
        ModifierHex.BrokenArmor,
        ModifierHex.GlassCannon,
        // NOT implemented: ExtraPotionSickness, SlowAttack, ManaDrain, Inaccurate, Marked
    };

    private static readonly ConstraintHex[] ImplementedConstraintHexes = new[]
    {
        ConstraintHex.NoRangedDamage,
        ConstraintHex.NoMeleeDamage,
        ConstraintHex.NoMagicDamage,
        ConstraintHex.Grounded,
        ConstraintHex.NoGrapple,
        // NOT implemented: NoBuffPotions, PacifistHealer
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

    private static ActiveHexes RollHexes(int playerCount, BossHexesConfig cfg)
    {
        var hexes = new ActiveHexes();

        // Build list of enabled categories
        var enabledCategories = new List<int>();
        if (cfg.EnableFlashyHexes) enabledCategories.Add(0);
        if (cfg.EnableModifierHexes) enabledCategories.Add(1);
        if (cfg.EnableConstraintHexes) enabledCategories.Add(2);

        if (enabledCategories.Count == 0)
            return hexes; // No hexes enabled

        if (playerCount == 1)
        {
            // 1 player: 1 hex from any enabled category
            int category = enabledCategories[Main.rand.Next(enabledCategories.Count)];
            ApplyHexCategory(hexes, category);
        }
        else if (playerCount == 2)
        {
            // 2 players: 2 hexes from 2 different enabled categories
            if (enabledCategories.Count >= 2)
            {
                int idx1 = Main.rand.Next(enabledCategories.Count);
                int idx2 = (idx1 + Main.rand.Next(1, enabledCategories.Count)) % enabledCategories.Count;
                ApplyHexCategory(hexes, enabledCategories[idx1]);
                ApplyHexCategory(hexes, enabledCategories[idx2]);
            }
            else if (enabledCategories.Count == 1)
            {
                ApplyHexCategory(hexes, enabledCategories[0]);
            }
        }
        else
        {
            // 3+ players: 1 from each enabled category
            foreach (int cat in enabledCategories)
            {
                ApplyHexCategory(hexes, cat);
            }
        }

        return hexes;
    }

    private static void ApplyHexCategory(ActiveHexes hexes, int category)
    {
        switch (category)
        {
            case 0:
                hexes.Flashy = RollFlashyHex();
                break;
            case 1:
                hexes.Modifier = RollModifierHex();
                break;
            case 2:
                hexes.Constraint = RollConstraintHex();
                break;
        }
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
        packet.Write(CurrentBossType);
        packet.Write((byte)Current.Flashy);
        packet.Write((byte)Current.Modifier);
        packet.Write((byte)Current.Constraint);
        packet.Write(Current.TimeLimitTicks);
        packet.Write(Current.TimeLimitMaxTicks);
        packet.Write(Current.PacifistHealerIndex);
        packet.Send(toWho, ignoreClient);
    }

    /// <summary>
    /// Receive hex state from server. Called on clients.
    /// </summary>
    public static void ReceiveSync(
        int bossType,
        FlashyHex flashy,
        ModifierHex modifier,
        ConstraintHex constraint,
        int timeLimitTicks,
        int timeLimitMaxTicks,
        int pacifistHealerIndex)
    {
        CurrentBossType = bossType;
        Current.Flashy = flashy;
        Current.Modifier = modifier;
        Current.Constraint = constraint;
        Current.TimeLimitTicks = timeLimitTicks;
        Current.TimeLimitMaxTicks = timeLimitMaxTicks;
        Current.PacifistHealerIndex = pacifistHealerIndex;
    }
}
