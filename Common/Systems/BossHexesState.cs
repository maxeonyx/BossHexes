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
    private bool _wasAnyBossAlive;
    private int _lastBossType = -1;
    
    // Meteor shower controller
    private readonly MeteorShowerController _meteorController = new();

    public override void OnWorldLoad()
    {
        _wasAnyBossAlive = AnyBossAlive();
        _lastBossType = -1;
        BossHexManager.OnWorldLoad();
        _meteorController.Reset();
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

    private void UpdateBossHexes()
    {
        bool anyBossAlive = AnyBossAlive(out int bossType);
        
        if (anyBossAlive && !_wasAnyBossAlive)
        {
            // Boss just spawned - this is handled by GlobalNPC.OnSpawn
            _lastBossType = bossType;
            // Reset meteor controller for fresh engagement curve
            _meteorController.Reset();
        }
        else if (_wasAnyBossAlive && !anyBossAlive)
        {
            // All bosses gone - could be defeat (handled by OnKill) or despawn/player death
            // Don't clear hexes here - OnKill handles actual defeats
            // Just clear the current fight state so next spawn re-rolls if needed
            _lastBossType = -1;
        }

        // Update active hex effects
        if (anyBossAlive && BossHexManager.Current.HasAnyHex)
        {
            UpdateActiveHexEffects();
        }

        _wasAnyBossAlive = anyBossAlive;
    }

    private void UpdateActiveHexEffects()
    {
        var hexes = BossHexManager.Current;

        // Time Limit
        if (hexes.Flashy == FlashyHex.TimeLimit)
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
        if (hexes.Flashy == FlashyHex.UnstableGravity)
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
        if (hexes.Flashy == FlashyHex.MeteorShower)
        {
            _meteorController.Update();
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

    private static bool AnyBossAlive() => AnyBossAlive(out _);

    private static bool AnyBossAlive(out int bossType)
    {
        bossType = -1;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var n = Main.npc[i];
            if (n.active && n.boss)
            {
                bossType = n.type;
                return true;
            }
        }
        return false;
    }
}
