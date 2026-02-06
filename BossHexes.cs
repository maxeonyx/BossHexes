using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using BossHexes.Common.Systems;

namespace BossHexes;

public sealed class BossHexes : Mod
{
    internal enum MessageType : byte
    {
        SyncHexes,
        KillPlayer,    // Server -> Client: tells client to kill themselves (TimeLimit hex)
        FlipGravity    // Server -> Client: tells client to flip their gravity
    }

    public override void HandlePacket(BinaryReader reader, int whoAmI)
    {
        var msgType = (MessageType)reader.ReadByte();

        switch (msgType)
        {
            case MessageType.SyncHexes:
                int bossType = reader.ReadInt32();
                byte flashy = reader.ReadByte();
                byte modifier = reader.ReadByte();
                byte constraint = reader.ReadByte();
                int timeLimitTicks = reader.ReadInt32();
                int timeLimitMaxTicks = reader.ReadInt32();
                int pacifistHealerIndex = reader.ReadInt32();

                // Apply hex state locally
                BossHexManager.ReceiveSync(
                    bossType,
                    (FlashyHex)flashy,
                    (ModifierHex)modifier,
                    (ConstraintHex)constraint,
                    timeLimitTicks,
                    timeLimitMaxTicks,
                    pacifistHealerIndex
                );

                // Server forwards to other clients
                if (Main.netMode == NetmodeID.Server)
                {
                    BossHexManager.SendSync(this, -1, whoAmI);
                }
                break;

            case MessageType.KillPlayer:
                // Server tells client to kill themselves (TimeLimit hex)
                byte killTarget = reader.ReadByte();
                string deathReason = reader.ReadString();

                if (Main.netMode == NetmodeID.MultiplayerClient && killTarget == Main.myPlayer)
                {
                    Player player = Main.LocalPlayer;
                    if (!player.dead)
                    {
                        player.KillMe(
                            Terraria.DataStructures.PlayerDeathReason.ByCustomReason(
                                Terraria.Localization.NetworkText.FromLiteral(deathReason)),
                            9999.0,
                            0);
                    }
                }
                break;

            case MessageType.FlipGravity:
                // Server tells client to flip their gravity
                byte flipTarget = reader.ReadByte();

                if (Main.netMode == NetmodeID.MultiplayerClient && flipTarget == Main.myPlayer)
                {
                    Main.LocalPlayer.gravDir *= -1;
                }
                break;
        }
    }

    /// <summary>
    /// Sends a packet to kill a player (for server -> client).
    /// </summary>
    internal static void SendKillPlayer(Mod mod, int playerIndex, string deathReason)
    {
        if (Main.netMode != NetmodeID.Server)
            return;

        ModPacket packet = mod.GetPacket();
        packet.Write((byte)MessageType.KillPlayer);
        packet.Write((byte)playerIndex);
        packet.Write(deathReason);
        packet.Send(playerIndex); // Send only to target client
    }

    /// <summary>
    /// Sends a packet to flip a player's gravity (for server -> client).
    /// </summary>
    internal static void SendFlipGravity(Mod mod, int playerIndex)
    {
        if (Main.netMode != NetmodeID.Server)
            return;

        ModPacket packet = mod.GetPacket();
        packet.Write((byte)MessageType.FlipGravity);
        packet.Write((byte)playerIndex);
        packet.Send(playerIndex); // Send only to target client
    }
}
