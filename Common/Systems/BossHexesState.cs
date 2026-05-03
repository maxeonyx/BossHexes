using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using BossHexes.Common.Config;

namespace BossHexes.Common.Systems;

/// <summary>
/// Manages world-level hex effects like TimeLimit, UnstableGravity, and MeteorShower.
/// </summary>
public sealed class BossHexesState : ModSystem
{
    private readonly Dictionary<int, MeteorShowerController> _meteorControllers = new();

    private static bool HasWorldEffectAuthority()
    {
        return Main.netMode != NetmodeID.MultiplayerClient;
    }

    public override void OnWorldLoad()
    {
        BossHexManager.OnWorldLoad();
        _meteorControllers.Clear();
    }

    public override void SaveWorldData(TagCompound tag)
    {
        BossHexManager.SaveWorldData(tag);
    }

    public override void LoadWorldData(TagCompound tag)
    {
        BossHexManager.LoadWorldData(tag);
    }

    public override void PostUpdateWorld()
    {
        var cfg = ModContent.GetInstance<BossHexesConfig>();
        if (cfg == null || !cfg.EnableBossHexes)
            return;

        UpdateBossHexes();
    }

    public bool TryEnsureActiveFight(int bossType, out int encounterId, out ActiveHexes hexes)
    {
        return TryEnsureActiveFight(bossType, -1, out encounterId, out hexes);
    }

    public bool TryEnsureActiveFight(int bossType, int spawningBossRootWhoAmI, out int encounterId, out ActiveHexes hexes)
    {
        encounterId = -1;
        hexes = null;

        if (!BossHexManager.TryEnsureActiveHexes(bossType, spawningBossRootWhoAmI, out hexes, out bool activatedFight))
            return false;

        encounterId = hexes.EncounterId;

        if (activatedFight)
        {
            if (Main.netMode == NetmodeID.Server)
                BossHexManager.SendSync(Mod, -1, -1);

            AnnounceActivatedFight(hexes);
        }

        return true;
    }

    private void UpdateBossHexes()
    {
        bool hasWorldEffectAuthority = HasWorldEffectAuthority();
        List<KeyValuePair<int, ActiveHexes>> activatedMissingFights = hasWorldEffectAuthority
            ? BossHexManager.ActivateMissingBossFights()
            : null;
        bool removedInactiveFights = hasWorldEffectAuthority && BossHexManager.ReconcileActiveBossFights();
        bool activatedAnyMissingFight = activatedMissingFights?.Count > 0;
          
        if (activatedAnyMissingFight)
        {
            if (Main.netMode == NetmodeID.Server)
                BossHexManager.SendSync(Mod, -1, -1);

            foreach (var (_, hexes) in activatedMissingFights)
            {
                AnnounceActivatedFight(hexes);
            }
        }

        if (removedInactiveFights)
        {
            ReconcileMeteorControllers();

            if (Main.netMode == NetmodeID.Server)
                BossHexManager.SendSync(Mod, -1, -1);
        }

        // Update active hex effects
        UpdateActiveHexEffects();
    }

    private void UpdateActiveHexEffects()
    {
        bool hasWorldEffectAuthority = HasWorldEffectAuthority();

        foreach (var (bossType, hexes) in BossHexManager.GetActiveBossFights())
        {
            if (!BossHexManager.IsBossFightActive(bossType) || !hexes.HasAnyHex)
                continue;

            // Time Limit
            if (hasWorldEffectAuthority && hexes.Flashy == FlashyHex.TimeLimit)
            {
                hexes.TimeLimitTicks--;

                // Announce at certain thresholds
                if (hexes.TimeLimitTicks == 60 * 60 * 2) // 2 minutes
                    AnnounceTimeLeft("2 minutes remaining!");
                else if (hexes.TimeLimitTicks == 60 * 60) // 1 minute
                    AnnounceTimeLeft("1 minute remaining!");
                else if (hexes.TimeLimitTicks == 60 * 30) // 30 seconds
                    AnnounceTimeLeft("30 seconds remaining!", Color.Orange);
                else if (hexes.TimeLimitTicks == 60 * 10) // 10 seconds
                    AnnounceTimeLeft("10 seconds!", Color.Red);

                if (hexes.TimeLimitTicks <= 0)
                {
                    // Time's up - kill everyone
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        for (int i = 0; i < Main.maxPlayers; i++)
                        {
                            var p = Main.player[i];
                            if (p?.active == true && !p.dead)
                            {
                                string deathReason = $"{p.name} ran out of time.";

                                if (Main.netMode == NetmodeID.Server)
                                {
                                    // Multiplayer: send packet to client
                                    BossHexes.SendKillPlayer(Mod, i, deathReason);
                                }
                                else
                                {
                                    // Singleplayer: kill directly
                                    p.KillMe(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(
                                        NetworkText.FromLiteral(deathReason)), 9999.0, 0);
                                }
                            }
                        }
                    }
                }
            }

            // Unstable Gravity - flips at random intervals (4-8 seconds with jitter)
            if (hasWorldEffectAuthority && hexes.Flashy == FlashyHex.UnstableGravity)
            {
                hexes.GravityFlipTicks++;

                // Set next flip time if not set
                if (hexes.NextGravityFlipAt == 0)
                {
                    // 4-8 seconds (240-480 ticks)
                    hexes.NextGravityFlipAt = hexes.GravityFlipTicks + 240 + Main.rand.Next(240);
                }

                if (hexes.GravityFlipTicks >= hexes.NextGravityFlipAt)
                {
                    // Schedule next flip with jitter (4-8 seconds)
                    hexes.NextGravityFlipAt = hexes.GravityFlipTicks + 240 + Main.rand.Next(240);

                    // Flip all players' gravity
                    for (int i = 0; i < Main.maxPlayers; i++)
                    {
                        var p = Main.player[i];
                        if (p?.active == true && !p.dead)
                        {
                            if (Main.netMode == NetmodeID.Server)
                            {
                                // Multiplayer: send packet to client
                                BossHexes.SendFlipGravity(Mod, i);
                            }
                            else
                            {
                                // Singleplayer: flip directly
                                p.gravDir *= -1;
                            }
                        }
                    }

                    if (Main.netMode == NetmodeID.Server)
                        ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral("Gravity shifts!"), Color.Purple);
                    else if (Main.netMode != NetmodeID.MultiplayerClient)
                        Main.NewText("Gravity shifts!", Color.Purple);
                }
            }

            // Meteor Shower - uses dedicated controller for clustered spawning
            if (hasWorldEffectAuthority && hexes.Flashy == FlashyHex.MeteorShower)
            {
                GetMeteorController(bossType)?.Update();
            }
        }
    }

    private MeteorShowerController GetMeteorController(int bossType)
    {
        if (!BossHexManager.TryGetActiveHexes(bossType, out var hexes))
            return null;

        if (_meteorControllers.TryGetValue(bossType, out var controller) && controller.EncounterId == hexes.EncounterId)
            return controller;

        controller = new MeteorShowerController(bossType, hexes.EncounterId);
        _meteorControllers[bossType] = controller;
        return controller;
    }

    public void OnBossFightEnded(int bossType, int encounterId)
    {
        if (bossType < 0 || encounterId < 0)
            return;

        if (!_meteorControllers.TryGetValue(bossType, out var controller))
            return;

        if (controller.EncounterId != encounterId)
            return;

        _meteorControllers.Remove(bossType);
    }

    private void ReconcileMeteorControllers()
    {
        var inactiveBossTypes = new List<int>();

        foreach (var bossType in _meteorControllers.Keys)
        {
            if (!BossHexManager.IsBossFightActive(bossType))
                inactiveBossTypes.Add(bossType);
        }

        foreach (var bossType in inactiveBossTypes)
        {
            _meteorControllers.Remove(bossType);
        }
    }

    private static void AnnounceTimeLeft(string message, Color? color = null)
    {
        Color c = color ?? Color.Yellow;
        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), c);
        else if (Main.netMode != NetmodeID.MultiplayerClient)
            Main.NewText(message, c);
    }

    public static void AnnounceActivatedFight(ActiveHexes hexes)
    {
        if (hexes == null || !hexes.HasAnyHex)
            return;

        var hexNames = hexes.GetActiveHexNames();
        if (hexNames.Count == 0)
            return;

        string hexList = string.Join(", ", hexNames);
        string message = hexNames.Count == 1
            ? $"Boss Hex: {hexList}"
            : $"Boss Hexes: {hexList}";

        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), Color.Orange);
        else if (Main.netMode != NetmodeID.MultiplayerClient)
            Main.NewText(message, Color.Orange);

        if (hexes.Flashy == FlashyHex.TimeLimit)
        {
            const string timeMsg = "You have 3 minutes to defeat the boss!";
            if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(timeMsg), Color.Red);
            else if (Main.netMode != NetmodeID.MultiplayerClient)
                Main.NewText(timeMsg, Color.Red);
        }

        if (hexes.Constraint != ConstraintHex.PacifistHealer || hexes.PacifistHealerIndex < 0)
            return;

        var healer = Main.player[hexes.PacifistHealerIndex];
        if (healer?.active != true)
            return;

        string healerMsg = $"{healer.name} is the Pacifist Healer! They cannot damage the boss but heal allies.";
        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(healerMsg), Color.LightGreen);
        else if (Main.netMode != NetmodeID.MultiplayerClient)
            Main.NewText(healerMsg, Color.LightGreen);
    }
}
