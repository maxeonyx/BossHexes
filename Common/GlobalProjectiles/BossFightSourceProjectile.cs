using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using BossHexes.Common.GlobalNPCs;
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
    private int _sourceEncounterId = -1;

    public int SourceBossType => _sourceBossType;
    public int SourceEncounterId => _sourceEncounterId;

    public bool IsFromCurrentBossFight =>
        _sourceBossType >= 0 &&
        _sourceEncounterId >= 0 &&
        BossHexManager.IsBossFightActive(_sourceBossType, _sourceEncounterId);

    public bool IsFromInvisibleBossFight =>
        TryGetSourceFightHexes(out var hexes) &&
        hexes.Flashy == FlashyHex.InvisibleBoss;

    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        (_sourceBossType, _sourceEncounterId) = ResolveSourceFight(source);
    }

    public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
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

    public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
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

        if (_sourceBossType < 0 || _sourceEncounterId < 0)
            return false;

        if (!BossHexManager.IsBossFightActive(_sourceBossType, _sourceEncounterId))
            return false;

        return BossHexManager.TryGetActiveHexes(_sourceBossType, _sourceEncounterId, out hexes);
    }

    private static (int BossType, int EncounterId) ResolveSourceFight(IEntitySource source)
    {
        if (TryGetCurrentFightIdentity(source, out int bossType, out int encounterId))
            return (bossType, encounterId);

        if (source is IEntitySource_OnHit onHit && TryGetCurrentFightIdentity(onHit.Attacker, out bossType, out encounterId))
            return (bossType, encounterId);

        if (source is IEntitySource_OnHurt onHurt && TryGetCurrentFightIdentity(onHurt.Attacker, out bossType, out encounterId))
            return (bossType, encounterId);

        return (-1, -1);
    }

    private static bool TryGetCurrentFightIdentity(IEntitySource source, out int bossType, out int encounterId)
    {
        bossType = -1;
        encounterId = -1;

        if (source is not EntitySource_Parent parent)
            return false;

        return TryGetCurrentFightIdentity(parent.Entity, out bossType, out encounterId);
    }

    private static bool TryGetCurrentFightIdentity(Entity entity, out int bossType, out int encounterId)
    {
        bossType = -1;
        encounterId = -1;

        if (entity is NPC npc)
            return TryGetCurrentFightIdentity(npc, out bossType, out encounterId);

        if (entity is Projectile projectile)
        {
            var bossSource = projectile.GetGlobalProjectile<BossFightSourceProjectile>();
            if (!bossSource.IsFromCurrentBossFight)
                return false;

            bossType = bossSource._sourceBossType;
            encounterId = bossSource._sourceEncounterId;
            return true;
        }

        return false;
    }

    private static bool TryGetCurrentFightIdentity(NPC npc, out int bossType, out int encounterId)
    {
        bossType = -1;
        encounterId = -1;

        if (BossHexGlobalNPC.TryGetCurrentFightHexes(npc, out bossType, out encounterId, out _))
            return true;

        if (!BossHexManager.TryGetBossRoot(npc, out var root))
            return false;

        bossType = root.type;
        return ModContent.GetInstance<BossHexesState>().TryEnsureActiveFight(bossType, root.whoAmI, out encounterId, out _);
    }
}
