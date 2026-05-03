using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using BossHexes.Common.Systems;

namespace BossHexes.Common.GlobalProjectiles;

/// <summary>
/// Tracks which active boss fight a hostile projectile originated from.
/// </summary>
public sealed class BossFightSourceProjectile : GlobalProjectile
{
    public override bool InstancePerEntity => true;

    private int _sourceBossType = -1;

    public int SourceBossType => _sourceBossType;

    public bool IsFromCurrentBossFight =>
        _sourceBossType >= 0 &&
        BossHexManager.IsBossFightActive(_sourceBossType);

    public bool IsFromInvisibleBossFight =>
        TryGetSourceFightHexes(out var hexes) &&
        hexes.Flashy == FlashyHex.InvisibleBoss;

    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        _sourceBossType = ResolveSourceBossType(source);
    }

    public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        bitWriter.WriteBit(_sourceBossType >= 0);
        if (_sourceBossType >= 0)
            binaryWriter.Write(_sourceBossType);
    }

    public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
    {
        _sourceBossType = bitReader.ReadBit()
            ? binaryReader.ReadInt32()
            : -1;
    }

    public override bool PreDrawExtras(Projectile projectile)
    {
        return !IsFromInvisibleBossFight;
    }

    public override bool PreDraw(Projectile projectile, ref Color lightColor)
    {
        return !IsFromInvisibleBossFight;
    }

    public bool TryGetSourceFightHexes(out ActiveHexes hexes)
    {
        hexes = null;

        if (_sourceBossType < 0)
            return false;

        if (!BossHexManager.IsBossFightActive(_sourceBossType))
            return false;

        return BossHexManager.TryGetActiveHexes(_sourceBossType, out hexes);
    }

    private static int ResolveSourceBossType(IEntitySource source)
    {
        if (TryGetCurrentFightBossType(source, out int bossType))
            return bossType;

        if (source is IEntitySource_OnHit onHit && TryGetCurrentFightBossType(onHit.Attacker, out bossType))
            return bossType;

        if (source is IEntitySource_OnHurt onHurt && TryGetCurrentFightBossType(onHurt.Attacker, out bossType))
            return bossType;

        return -1;
    }

    private static bool TryGetCurrentFightBossType(IEntitySource source, out int bossType)
    {
        bossType = -1;

        if (source is not EntitySource_Parent parent)
            return false;

        return TryGetCurrentFightBossType(parent.Entity, out bossType);
    }

    private static bool TryGetCurrentFightBossType(Entity entity, out int bossType)
    {
        bossType = -1;

        if (entity is NPC npc)
            return TryGetCurrentFightBossType(npc, out bossType);

        if (entity is Projectile projectile)
        {
            var bossSource = projectile.GetGlobalProjectile<BossFightSourceProjectile>();
            if (bossSource._sourceBossType < 0)
                return false;

            bossType = bossSource._sourceBossType;
            return true;
        }

        return false;
    }

    private static bool TryGetCurrentFightBossType(NPC npc, out int bossType)
    {
        bossType = -1;

        if (!BossHexManager.TryGetBossRoot(npc, out var root))
            return false;

        if (!BossHexManager.IsBossFightActive(root.type))
            return false;

        bossType = root.type;
        return true;
    }
}
