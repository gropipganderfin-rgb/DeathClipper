using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DeathClipper;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/deathclip";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly ICondition condition;
    private readonly IObjectTable objectTable;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly DeathDetector deathDetector = new();

    private Configuration configuration;
    private bool configWindowOpen;
    private string hotkeyValidationMessage = string.Empty;
    private bool hotkeyIsValid = true;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        ICondition condition,
        IObjectTable objectTable,
        IChatGui chatGui,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.condition = condition;
        this.objectTable = objectTable;
        this.chatGui = chatGui;
        this.log = log;

        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Version = 1;
        configuration.CooldownSeconds = Math.Clamp(configuration.CooldownSeconds, 0, 300);
        ValidateHotkey();

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Death Clipper settings. Use '/deathclip test' to test F13.",
            ShowInHelp = true,
        });

        framework.Update += OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw += DrawConfiguration;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfiguration;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        pluginInterface.UiBuilder.Draw -= DrawConfiguration;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfiguration;
        commandManager.RemoveHandler(CommandName);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var localPlayer = objectTable.LocalPlayer;
        var nowUtc = DateTime.UtcNow;

        if (!deathDetector.Observe(
                localPlayer is not null,
                localPlayer?.IsDead ?? false,
                condition[ConditionFlag.InCombat],
                nowUtc,
                configuration))
            return;

        if (TrySaveReplay("death"))
            deathDetector.MarkClipSaved(nowUtc);
    }

    private bool TrySaveReplay(string reason)
    {
        if (!Hotkey.TryParse(configuration.SaveReplayHotkey, out var parsedHotkey, out var parseError))
        {
            log.Error("Could not save replay: invalid hotkey: {Error}", parseError);
            chatGui.PrintError($"[Death Clipper] Invalid hotkey: {parseError}");
            return false;
        }

        if (!Hotkey.TrySend(parsedHotkey, out var sendError))
        {
            log.Error("Could not send replay hotkey: {Error}", sendError);
            chatGui.PrintError($"[Death Clipper] Could not send {parsedHotkey.DisplayName}: {sendError}");
            return false;
        }

        log.Information("Sent replay hotkey {Hotkey}; reason: {Reason}", parsedHotkey.DisplayName, reason);
        if (configuration.ShowChatMessage)
            chatGui.Print($"[Death Clipper] Sent {parsedHotkey.DisplayName} to save the replay.");

        return true;
    }

    private void OnCommand(string command, string arguments)
    {
        var argument = arguments.Trim();
        if (argument.Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            TrySaveReplay("manual test");
            return;
        }

        if (argument.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            configuration.Enabled = true;
            SaveConfiguration();
            chatGui.Print("[Death Clipper] Enabled.");
            return;
        }

        if (argument.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            configuration.Enabled = false;
            SaveConfiguration();
            chatGui.Print("[Death Clipper] Disabled.");
            return;
        }

        configWindowOpen = true;
    }

    private void OpenConfiguration() => configWindowOpen = true;

    private void DrawConfiguration()
    {
        if (!configWindowOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(480, 0), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Death Clipper Settings###DeathClipperSettings", ref configWindowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        var changed = false;

        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            configuration.Enabled = enabled;
            changed = true;
        }

        var onlyInCombat = configuration.OnlyInCombat;
        if (ImGui.Checkbox("Only trigger while in combat", ref onlyInCombat))
        {
            configuration.OnlyInCombat = onlyInCombat;
            changed = true;
        }

        var oncePerPull = configuration.OncePerPull;
        if (ImGui.Checkbox("Save only once per pull", ref oncePerPull))
        {
            configuration.OncePerPull = oncePerPull;
            changed = true;
        }

        var showChatMessage = configuration.ShowChatMessage;
        if (ImGui.Checkbox("Show confirmation in chat", ref showChatMessage))
        {
            configuration.ShowChatMessage = showChatMessage;
            changed = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Recorder save-replay hotkey");

        if (ImGui.Button("Use F13"))
        {
            configuration.SaveReplayHotkey = "F13";
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("NVIDIA default: ALT+F10"))
        {
            configuration.SaveReplayHotkey = "ALT+F10";
            changed = true;
        }

        var hotkeyText = configuration.SaveReplayHotkey;
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("Custom hotkey", ref hotkeyText, 64))
        {
            configuration.SaveReplayHotkey = hotkeyText.ToUpperInvariant();
            changed = true;
        }

        var cooldown = configuration.CooldownSeconds;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Minimum seconds between clips", ref cooldown))
        {
            configuration.CooldownSeconds = Math.Clamp(cooldown, 0, 300);
            changed = true;
        }

        if (changed)
        {
            ValidateHotkey();
            SaveConfiguration();
        }

        if (hotkeyIsValid)
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.55f, 1f), hotkeyValidationMessage);
        else
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), hotkeyValidationMessage);

        if (ImGui.Button("Test: press save-replay hotkey"))
            TrySaveReplay("settings test");

        ImGui.Separator();
        ImGui.TextWrapped("NVIDIA Instant Replay, OBS Replay Buffer, Xbox Game Bar, or another recorder must already be running. Death Clipper only presses the configured save-replay hotkey.");

        ImGui.End();
    }

    private void ValidateHotkey()
    {
        hotkeyIsValid = Hotkey.TryParse(configuration.SaveReplayHotkey, out var hotkey, out var error);
        hotkeyValidationMessage = hotkeyIsValid ? $"Valid hotkey: {hotkey.DisplayName}" : error;
    }

    private void SaveConfiguration() => pluginInterface.SavePluginConfig(configuration);
}
