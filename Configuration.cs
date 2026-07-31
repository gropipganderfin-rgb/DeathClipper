using Dalamud.Configuration;

namespace DeathClipper;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public bool OnlyInCombat { get; set; } = true;

    public bool IncludePartyMemberDeaths { get; set; } = true;

    public bool OncePerPull { get; set; } = true;

    public bool ShowChatMessage { get; set; } = true;

    public int CooldownSeconds { get; set; } = 10;

    public string SaveReplayHotkey { get; set; } = "F13";
}
