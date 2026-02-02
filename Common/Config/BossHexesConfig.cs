using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace BossHexes.Common.Config;

public sealed class BossHexesConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ServerSide;

    [Header("$Mods.BossHexes.Config.Headers.General")]
    [DefaultValue(true)]
    public bool EnableBossHexes;

    [Header("$Mods.BossHexes.Config.Headers.HexCategories")]
    [DefaultValue(true)]
    public bool EnableFlashyHexes;

    [DefaultValue(true)]
    public bool EnableModifierHexes;

    [DefaultValue(true)]
    public bool EnableConstraintHexes;
}
