using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using BossHexes.Common.Systems;

namespace BossHexes.Common.GlobalProjectiles;

/// <summary>
/// Modifies meteor shower projectiles to deal reduced damage to bosses.
/// </summary>
public sealed class MeteorShowerProjectile : GlobalProjectile
{
    public override bool InstancePerEntity => true;

    private int _sourceBossType = -1;
    private int _sourceEncounterId = -1;

    public bool IsFromCurrentMeteorShower =>
        _sourceBossType >= 0 &&
        _sourceEncounterId >= 0 &&
        BossHexManager.IsBossFightActive(_sourceBossType, _sourceEncounterId);

    public void MarkAsMeteorShowerProjectile(int bossType, int encounterId)
    {
        _sourceBossType = bossType;
        _sourceEncounterId = encounterId;
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

    public override void ModifyHitNPC(Projectile projectile, NPC target, ref NPC.HitModifiers modifiers)
    {
        if (!IsFromCurrentMeteorShower)
            return;

        if (!BossHexManager.IsPartOfBossFight(target, _sourceBossType, _sourceEncounterId))
            return;

        modifiers.FinalDamage *= 0.1f;
    }
}
