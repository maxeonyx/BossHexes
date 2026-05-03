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

    public bool IsFromCurrentMeteorShower =>
        _sourceBossType >= 0 &&
        BossHexManager.IsBossFightActive(_sourceBossType);

    public void MarkAsMeteorShowerProjectile(int bossType)
    {
        _sourceBossType = bossType;
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

    public override void ModifyHitNPC(Projectile projectile, NPC target, ref NPC.HitModifiers modifiers)
    {
        if (!IsFromCurrentMeteorShower)
            return;

        if (!BossHexManager.IsPartOfBossFight(target, _sourceBossType))
            return;

        modifiers.FinalDamage *= 0.1f;
    }
}
