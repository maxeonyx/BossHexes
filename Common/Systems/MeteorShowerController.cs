using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace BossHexes.Common.Systems;

/// <summary>
/// Controls the meteor shower hex with clustered spawning and an engagement curve.
/// </summary>
public class MeteorShowerController
{
    // Fight duration tracking
    private int _fightTicks = 0;
    
    // Pending clusters to spawn (scheduled with their spawn tick)
    private readonly List<ScheduledCluster> _pendingClusters = new();
    
    // Cluster spawning state
    private int _nextWindowTick = 0;
    private const int WindowDurationTicks = 180; // 3 second windows
    
    public void Reset()
    {
        _fightTicks = 0;
        _pendingClusters.Clear();
        _nextWindowTick = 0;
    }

    public void Update()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        _fightTicks++;

        // Schedule new clusters at the start of each window
        if (_fightTicks >= _nextWindowTick)
        {
            ScheduleClustersForWindow();
            _nextWindowTick = _fightTicks + WindowDurationTicks;
        }

        // Spawn any clusters that are due
        SpawnDueClusters();
    }

    private void ScheduleClustersForWindow()
    {
        // Get current intensity from engagement curve
        float intensity = GetEngagementIntensity(_fightTicks);
        
        // Risk calibration:
        // - Spawn spread: 1600px, player+meteor hit zone: ~56px
        // - P(hit per meteor) = 56/1600 = 3.5%
        // - Target: 20% hit chance over 10 seconds at baseline
        // - Need ~6 meteors per 10 seconds at baseline (intensity ~1.5)
        // - 10 seconds = 3.3 windows, so ~2 meteors per window at baseline
        // - With 5 meteors per cluster, need ~0.4 clusters per window at baseline
        // 
        // Scale: clusters = intensity * 0.25 (so intensity 1.5 → 0.4, intensity 6 → 1.5)
        // Use fractional probability for < 1 cluster
        
        float expectedClusters = intensity * 0.35f;
        int guaranteedClusters = (int)expectedClusters;
        float fractionalChance = expectedClusters - guaranteedClusters;
        
        int clusterCount = guaranteedClusters;
        if (Main.rand.NextFloat() < fractionalChance)
            clusterCount++;
        
        for (int i = 0; i < clusterCount; i++)
        {
            // Random time within the window
            int spawnTick = _fightTicks + Main.rand.Next(WindowDurationTicks);
            
            // Pick a random active player to target
            Player target = GetRandomActivePlayer();
            if (target == null)
                continue;

            // Random base angle - 0 degrees = straight down in our coordinate system
            // Allow ±60 degrees variation (so -60 to +60, always downward)
            float baseAngleDeg = (Main.rand.NextFloat() - 0.5f) * 120f;
            
            // Random spawn position above and to the side of player
            float spawnOffsetX = (Main.rand.NextFloat() - 0.5f) * 1600f; // Wide spread
            
            _pendingClusters.Add(new ScheduledCluster
            {
                SpawnTick = spawnTick,
                TargetPlayerIndex = target.whoAmI,
                BaseAngleDegrees = baseAngleDeg,
                SpawnOffsetX = spawnOffsetX,
            });
        }
    }

    private void SpawnDueClusters()
    {
        for (int i = _pendingClusters.Count - 1; i >= 0; i--)
        {
            var cluster = _pendingClusters[i];
            if (_fightTicks >= cluster.SpawnTick)
            {
                SpawnCluster(cluster);
                _pendingClusters.RemoveAt(i);
            }
        }
    }

    private void SpawnCluster(ScheduledCluster cluster)
    {
        var player = Main.player[cluster.TargetPlayerIndex];
        if (player?.active != true || player.dead)
        {
            // Try to find another player
            player = GetRandomActivePlayer();
            if (player == null)
                return;
        }

        const int meteorsPerCluster = 5;
        const float spacingAlongTravel = 40f; // Pixels between meteors along travel direction
        const float perpendicularJitter = 15f; // Max jitter perpendicular to travel
        const float spacingJitter = 10f; // Jitter in spacing along travel
        const float angleJitterDeg = 1f; // Max angle difference in degrees

        // Convert base angle to radians and get direction vector
        float baseAngleRad = MathHelper.ToRadians(cluster.BaseAngleDegrees);
        Vector2 travelDir = new Vector2(
            (float)Math.Sin(baseAngleRad),
            (float)Math.Cos(baseAngleRad)
        );
        Vector2 perpDir = new Vector2(-travelDir.Y, travelDir.X); // Perpendicular

        // Base spawn position (above player, offset by cluster's X offset)
        Vector2 baseSpawn = new Vector2(
            player.Center.X + cluster.SpawnOffsetX,
            player.Center.Y - 700
        );

        // Spawn meteors in sequence along the travel direction
        for (int i = 0; i < meteorsPerCluster; i++)
        {
            // Distance along travel direction (evenly spaced with jitter)
            float distanceAlong = i * spacingAlongTravel + (Main.rand.NextFloat() - 0.5f) * spacingJitter * 2f;
            
            // Perpendicular jitter
            float perpOffset = (Main.rand.NextFloat() - 0.5f) * perpendicularJitter * 2f;
            
            // Position for this meteor
            Vector2 pos = baseSpawn - travelDir * distanceAlong + perpDir * perpOffset;

            // Angle jitter - if jittered right (positive perp), angle left (negative angle adjust)
            // This makes them converge toward a focal point
            float angleAdjustDeg = -perpOffset / perpendicularJitter * angleJitterDeg;
            float finalAngleRad = baseAngleRad + MathHelper.ToRadians(angleAdjustDeg);

            // Velocity (speed 14-18, in the adjusted direction)
            float speed = 14f + Main.rand.NextFloat() * 4f;
            Vector2 velocity = new Vector2(
                (float)Math.Sin(finalAngleRad) * speed,
                (float)Math.Cos(finalAngleRad) * speed
            );

            // Spawn the meteor
            int projIdx = Projectile.NewProjectile(
                Terraria.Entity.GetSource_NaturalSpawn(),
                pos.X, pos.Y,
                velocity.X, velocity.Y,
                ProjectileID.FallingStar,
                30, // Damage (reduced to bosses via GlobalProjectile)
                1f,
                255, // No owner
                0f, 0f);

            if (projIdx >= 0 && projIdx < Main.maxProjectiles)
            {
                Main.projectile[projIdx].hostile = true;
                Main.projectile[projIdx].friendly = true;
            }
        }
    }

    /// <summary>
    /// Engagement curve: oscillates up and down, but peaks and base rate increase over time.
    /// Pattern like: 5,1,2,1,3,2,6,2,4,1,5... with increasing intensity.
    /// Caps at a chaotic equilibrium around 2-3 minutes to avoid insanity in long fights.
    /// </summary>
    private float GetEngagementIntensity(int ticks)
    {
        // Time in seconds
        float seconds = ticks / 60f;

        // Base intensity increases over time but caps out
        // Starts at 1, grows to max of 4 over ~60 seconds, then plateaus
        // Using asymptotic approach: base = 1 + 3 * (1 - e^(-seconds/30))
        float maxBase = 4f;
        float baseIntensity = 1f + (maxBase - 1f) * (1f - (float)Math.Exp(-seconds / 30f));

        // Oscillation: multiple sine waves at different frequencies for organic feel
        // Amplitude also grows slightly over time but caps
        float ampScale = 1f + 0.5f * (1f - (float)Math.Exp(-seconds / 60f)); // 1.0 to 1.5
        float wave1 = (float)Math.Sin(seconds * 0.8) * 1.5f * ampScale;  // Slow wave
        float wave2 = (float)Math.Sin(seconds * 2.1) * 1.0f * ampScale;  // Medium wave  
        float wave3 = (float)Math.Sin(seconds * 4.7) * 0.5f;  // Fast ripple (constant amp)

        // Combine waves - they interfere to create varied peaks/valleys
        float oscillation = wave1 + wave2 + wave3;

        // Flashy entrance: big spike in first 3 seconds
        float entranceBoost = seconds < 3f ? (3f - seconds) * 2f : 0f;

        // Final intensity (minimum 0.5 so there's always some activity)
        float intensity = Math.Max(0.5f, baseIntensity + oscillation + entranceBoost);

        return intensity;
    }

    private static Player GetRandomActivePlayer()
    {
        // Collect active players
        var activePlayers = new List<Player>();
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            if (p?.active == true && !p.dead && !string.IsNullOrEmpty(p.name))
                activePlayers.Add(p);
        }

        if (activePlayers.Count == 0)
            return null;

        return activePlayers[Main.rand.Next(activePlayers.Count)];
    }

    private struct ScheduledCluster
    {
        public int SpawnTick;
        public int TargetPlayerIndex;
        public float BaseAngleDegrees;
        public float SpawnOffsetX;
    }
}
