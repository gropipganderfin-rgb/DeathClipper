using Dalamud.Configuration;

namespace DeathClipper;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    private string instantReplayToggleHotkey = "ALT+T";

    public int Version { get; set; } = 2;

    public bool Enabled { get; set; } = true;

    public bool OnlyInCombat { get; set; } = true;

    public bool IncludePartyMemberDeaths { get; set; } = true;

    public bool OncePerPull { get; set; } = true;

    public bool ShowChatMessage { get; set; } = true;

    public int CooldownSeconds { get; set; } = 10;

    public string SaveReplayHotkey { get; set; } = "F13";

    public bool ManageInstantReplay { get; set; } = false;

    public bool DisableInstantReplayOnDutyExit { get; set; } = true;

    public string InstantReplayToggleHotkey
    {
        get => instantReplayToggleHotkey;
        set => instantReplayToggleHotkey =
            string.Equals(value, "ALT+SHIFT+F10", StringComparison.OrdinalIgnoreCase)
                ? "ALT+T"
                : value;
    }

    public int DutyEntryDelaySeconds { get; set; } = 5;
}
