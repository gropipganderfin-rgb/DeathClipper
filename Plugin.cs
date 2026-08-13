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
    private static readonly TimeSpan AutomaticClipDelay = TimeSpan.FromSeconds(5);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly ICondition condition;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly DeathDetector deathDetector = new();

    private Configuration configuration;

    private DateTime? pendingAutomaticClipUtc;
    private DateTime? pendingInstantReplayEnableUtc;

    private bool dutyStateInitialized;
    private bool wasInDuty;

    // null  = UNKNOWN: automatic toggle is BLOCKED
    // true  = Death Clipper believes Instant Replay is ON
    // false = Death Clipper believes Instant Replay is OFF
    private bool? instantReplayTrackedOn;

    private bool configWindowOpen;
    private string hotkeyValidationMessage = string.Empty;
    private bool hotkeyIsValid = true;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        ICondition condition,
        IObjectTable objectTable,
        IPartyList partyList,
        IChatGui chatGui,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.framework = framework;
        this.condition = condition;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.chatGui = chatGui;
        this.log = log;

        configuration =
            pluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        configuration.Version = 1;
        configuration.CooldownSeconds =
            Math.Clamp(configuration.CooldownSeconds, 0, 300);

        configuration.DutyEntryDelaySeconds =
            Math.Clamp(configuration.DutyEntryDelaySeconds, 0, 30);

        ValidateHotkey();

        commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage =
                    "Open Death Clipper settings. Use '/deathclip test' to test the save-replay hotkey.",
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

        var isInDuty = IsInDuty();

        HandleInstantReplayDutyManagement(
            isInDuty,
            nowUtc);

        var anyMonitoredPlayerDead =
            localPlayer?.IsDead ?? false;

        if (configuration.IncludePartyMemberDeaths)
        {
            foreach (var partyMember in partyList)
            {
                if (partyMember.MaxHP > 0
                    && partyMember.CurrentHP == 0)
                {
                    anyMonitoredPlayerDead = true;
                    break;
                }
            }
        }

        if (pendingAutomaticClipUtc is { } triggerAtUtc
            && nowUtc >= triggerAtUtc)
        {
            pendingAutomaticClipUtc = null;

            if (TrySaveReplay(
                    "automatic death (5-second delay)"))
            {
                deathDetector.MarkClipSaved(nowUtc);
            }
        }

        if (!deathDetector.Observe(
                localPlayer is not null,
                anyMonitoredPlayerDead,
                condition[ConditionFlag.InCombat],
                nowUtc,
                configuration))
        {
            return;
        }

        pendingAutomaticClipUtc ??=
            nowUtc + AutomaticClipDelay;
    }

    private bool IsInDuty()
    {
        return condition[ConditionFlag.BoundByDuty]
               || condition[ConditionFlag.BoundByDuty56]
               || condition[ConditionFlag.BoundByDuty95];
    }

    private void HandleInstantReplayDutyManagement(
        bool isInDuty,
        DateTime nowUtc)
    {
        if (!dutyStateInitialized)
        {
            dutyStateInitialized = true;
            wasInDuty = isInDuty;
            return;
        }

        if (!configuration.ManageInstantReplay)
        {
            pendingInstantReplayEnableUtc = null;
            wasInDuty = isInDuty;
            return;
        }

        // ENTERING DUTY
        if (isInDuty && !wasInDuty)
        {
            if (instantReplayTrackedOn == false)
            {
                // We KNOW it is OFF, so it is safe to toggle it ON.
                pendingInstantReplayEnableUtc =
                    nowUtc
                    + TimeSpan.FromSeconds(
                        configuration.DutyEntryDelaySeconds);

                log.Information(
                    "Entered duty. Instant Replay enable scheduled in {Delay} seconds.",
                    configuration.DutyEntryDelaySeconds);
            }
            else if (instantReplayTrackedOn is null)
            {
                // UNKNOWN MUST NEVER TOGGLE.
                pendingInstantReplayEnableUtc = null;

                log.Information(
                    "Entered duty with Instant Replay state UNKNOWN. Automatic toggle blocked.");

                if (configuration.ShowChatMessage)
                {
                    chatGui.Print(
                        "[Death Clipper] Instant Replay state is UNKNOWN. Automatic toggle blocked. Open /deathclip and mark whether Instant Replay is currently ON or OFF.");
                }
            }
            // If state is TRUE, it is already tracked as ON,
            // so we intentionally do nothing.
        }

        // LEAVING DUTY
        else if (!isInDuty && wasInDuty)
        {
            pendingInstantReplayEnableUtc = null;

            if (configuration.DisableInstantReplayOnDutyExit
                && instantReplayTrackedOn == true)
            {
                TryToggleInstantReplay(
                    assumedStateAfterToggle: false,
                    reason: "leaving duty");
            }
        }

        wasInDuty = isInDuty;

        if (pendingInstantReplayEnableUtc
                is not { } enableAtUtc
            || nowUtc < enableAtUtc)
        {
            return;
        }

        pendingInstantReplayEnableUtc = null;

        if (!isInDuty)
            return;

        // CRITICAL SAFETY CHECK:
        // We ONLY toggle ON when state is explicitly OFF.
        if (instantReplayTrackedOn != false)
            return;

        TryToggleInstantReplay(
            assumedStateAfterToggle: true,
            reason: "entering duty");
    }

    private bool TrySaveReplay(string reason)
    {
        if (!Hotkey.TryParse(
                configuration.SaveReplayHotkey,
                out var parsedHotkey,
                out var parseError))
        {
            log.Error(
                "Could not save replay: invalid hotkey: {Error}",
                parseError);

            chatGui.PrintError(
                $"[Death Clipper] Invalid hotkey: {parseError}");

            return false;
        }

        if (!Hotkey.TrySend(
                parsedHotkey,
                out var sendError))
        {
            log.Error(
                "Could not send replay hotkey: {Error}",
                sendError);

            chatGui.PrintError(
                $"[Death Clipper] Could not send {parsedHotkey.DisplayName}: {sendError}");

            return false;
        }

        log.Information(
            "Sent replay hotkey {Hotkey}; reason: {Reason}",
            parsedHotkey.DisplayName,
            reason);

        if (configuration.ShowChatMessage)
        {
            chatGui.Print(
                $"[Death Clipper] Sent {parsedHotkey.DisplayName} to save the replay.");
        }

        return true;
    }

    private bool TryToggleInstantReplay(
        bool assumedStateAfterToggle,
        string reason)
    {
        if (!Hotkey.TryParse(
                configuration.InstantReplayToggleHotkey,
                out var parsedHotkey,
                out var parseError))
        {
            log.Error(
                "Could not toggle Instant Replay: invalid toggle hotkey: {Error}",
                parseError);

            chatGui.PrintError(
                $"[Death Clipper] Invalid Instant Replay toggle hotkey: {parseError}");

            instantReplayTrackedOn = null;
            pendingInstantReplayEnableUtc = null;

            return false;
        }

        if (!Hotkey.TrySend(
                parsedHotkey,
                out var sendError))
        {
            log.Error(
                "Could not send Instant Replay toggle hotkey: {Error}",
                sendError);

            chatGui.PrintError(
                $"[Death Clipper] Could not send {parsedHotkey.DisplayName}: {sendError}");

            instantReplayTrackedOn = null;
            pendingInstantReplayEnableUtc = null;

            return false;
        }

        instantReplayTrackedOn =
            assumedStateAfterToggle;

        var stateText =
            assumedStateAfterToggle
                ? "ON"
                : "OFF";

        log.Information(
            "Sent Instant Replay toggle hotkey {Hotkey}; tracked state: {State}; reason: {Reason}",
            parsedHotkey.DisplayName,
            stateText,
            reason);

        if (configuration.ShowChatMessage)
        {
            chatGui.Print(
                $"[Death Clipper] Toggled NVIDIA Instant Replay. Tracked state: {stateText}.");
        }

        return true;
    }

    private void OnCommand(
        string command,
        string arguments)
    {
        var argument = arguments.Trim();

        if (argument.Equals(
                "test",
                StringComparison.OrdinalIgnoreCase))
        {
            TrySaveReplay("manual test");
            return;
        }

        if (argument.Equals(
                "on",
                StringComparison.OrdinalIgnoreCase))
        {
            configuration.Enabled = true;
            SaveConfiguration();

            chatGui.Print(
                "[Death Clipper] Enabled.");

            return;
        }

        if (argument.Equals(
                "off",
                StringComparison.OrdinalIgnoreCase))
        {
            configuration.Enabled = false;
            pendingAutomaticClipUtc = null;

            SaveConfiguration();

            chatGui.Print(
                "[Death Clipper] Disabled.");

            return;
        }

        if (argument.Equals(
                "replaystatus",
                StringComparison.OrdinalIgnoreCase))
        {
            chatGui.Print(
                $"[Death Clipper] Instant Replay tracked state: {GetInstantReplayStateText()}.");

            return;
        }

        if (argument.Equals(
                "forgetreplaystate",
                StringComparison.OrdinalIgnoreCase))
        {
            instantReplayTrackedOn = null;
            pendingInstantReplayEnableUtc = null;

            chatGui.Print(
                "[Death Clipper] Instant Replay tracked state reset to UNKNOWN. Automatic toggles are blocked.");

            return;
        }

        configWindowOpen = true;
    }

    private void OpenConfiguration()
    {
        configWindowOpen = true;
    }

    private void DrawConfiguration()
    {
        if (!configWindowOpen)
            return;

        ImGui.SetNextWindowSize(
            new Vector2(500, 0),
            ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(
                "Death Clipper Settings###DeathClipperSettings",
                ref configWindowOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        var changed = false;

        var enabled = configuration.Enabled;

        if (ImGui.Checkbox(
                "Enabled",
                ref enabled))
        {
            configuration.Enabled = enabled;
            changed = true;
        }

        var onlyInCombat =
            configuration.OnlyInCombat;

        if (ImGui.Checkbox(
                "Only trigger while in combat",
                ref onlyInCombat))
        {
            configuration.OnlyInCombat =
                onlyInCombat;

            changed = true;
        }

        var includePartyMemberDeaths =
            configuration.IncludePartyMemberDeaths;

        if (ImGui.Checkbox(
                "Trigger when any party member dies",
                ref includePartyMemberDeaths))
        {
            configuration.IncludePartyMemberDeaths =
                includePartyMemberDeaths;

            pendingAutomaticClipUtc = null;
            deathDetector.Reset();
            changed = true;
        }

        var oncePerPull =
            configuration.OncePerPull;

        if (ImGui.Checkbox(
                "Save only once per pull",
                ref oncePerPull))
        {
            configuration.OncePerPull =
                oncePerPull;

            changed = true;
        }

        var showChatMessage =
            configuration.ShowChatMessage;

        if (ImGui.Checkbox(
                "Show confirmation in chat",
                ref showChatMessage))
        {
            configuration.ShowChatMessage =
                showChatMessage;

            changed = true;
        }

        ImGui.Separator();

        ImGui.TextUnformatted(
            "Recorder save-replay hotkey");

        if (ImGui.Button("Use F13"))
        {
            configuration.SaveReplayHotkey =
                "F13";

            changed = true;
        }

        ImGui.SameLine();

        if (ImGui.Button(
                "NVIDIA: ALT+F10"))
        {
            configuration.SaveReplayHotkey =
                "ALT+F10";

            changed = true;
        }

        var hotkeyText =
            configuration.SaveReplayHotkey;

        ImGui.SetNextItemWidth(260);

        if (ImGui.InputText(
                "Custom hotkey",
                ref hotkeyText,
                64))
        {
            configuration.SaveReplayHotkey =
                hotkeyText.ToUpperInvariant();

            changed = true;
        }

        var cooldown =
            configuration.CooldownSeconds;

        ImGui.SetNextItemWidth(100);

        if (ImGui.InputInt(
                "Minimum seconds between clips",
                ref cooldown))
        {
            configuration.CooldownSeconds =
                Math.Clamp(cooldown, 0, 300);

            changed = true;
        }

        if (changed)
        {
            ValidateHotkey();
            SaveConfiguration();
        }

        if (hotkeyIsValid)
        {
            ImGui.TextColored(
                new Vector4(
                    0.35f,
                    0.85f,
                    0.55f,
                    1f),
                hotkeyValidationMessage);
        }
        else
        {
            ImGui.TextColored(
                new Vector4(
                    1f,
                    0.35f,
                    0.35f,
                    1f),
                hotkeyValidationMessage);
        }

        if (ImGui.Button(
                "Test: press save-replay hotkey"))
        {
            TrySaveReplay(
                "settings test");
        }

        ImGui.Separator();

        ImGui.TextUnformatted(
            "NVIDIA Instant Replay duty management");

        var manageInstantReplay =
            configuration.ManageInstantReplay;

        if (ImGui.Checkbox(
                "Automatically manage Instant Replay in duties",
                ref manageInstantReplay))
        {
            configuration.ManageInstantReplay =
                manageInstantReplay;

            if (!manageInstantReplay)
            {
                pendingInstantReplayEnableUtc =
                    null;
            }
            else if (
                IsInDuty()
                && instantReplayTrackedOn == false)
            {
                pendingInstantReplayEnableUtc =
                    DateTime.UtcNow
                    + TimeSpan.FromSeconds(
                        configuration.DutyEntryDelaySeconds);
            }
            else
            {
                // TRUE = already on.
                // NULL = unknown and therefore blocked.
                pendingInstantReplayEnableUtc =
                    null;
            }

            changed = true;
        }

        var disableOnExit =
            configuration.DisableInstantReplayOnDutyExit;

        if (ImGui.Checkbox(
                "Turn Instant Replay off when leaving the duty",
                ref disableOnExit))
        {
            configuration.DisableInstantReplayOnDutyExit =
                disableOnExit;

            changed = true;
        }

        var toggleHotkey =
            configuration.InstantReplayToggleHotkey;

        ImGui.SetNextItemWidth(260);

        if (ImGui.InputText(
                "Instant Replay toggle hotkey",
                ref toggleHotkey,
                64))
        {
            configuration.InstantReplayToggleHotkey =
                toggleHotkey.ToUpperInvariant();

            changed = true;
        }

        var dutyDelay =
            configuration.DutyEntryDelaySeconds;

        ImGui.SetNextItemWidth(100);

        if (ImGui.InputInt(
                "Seconds after entering duty",
                ref dutyDelay))
        {
            configuration.DutyEntryDelaySeconds =
                Math.Clamp(
                    dutyDelay,
                    0,
                    30);

            changed = true;
        }

        if (changed)
        {
            ValidateHotkey();
            SaveConfiguration();
        }

        if (Hotkey.TryParse(
                configuration.InstantReplayToggleHotkey,
                out var toggleParsed,
                out var toggleError))
        {
            ImGui.TextColored(
                new Vector4(
                    0.35f,
                    0.85f,
                    0.55f,
                    1f),
                $"Valid toggle hotkey: {toggleParsed.DisplayName}");
        }
        else
        {
            ImGui.TextColored(
                new Vector4(
                    1f,
                    0.35f,
                    0.35f,
                    1f),
                toggleError);
        }

        ImGui.TextUnformatted(
            $"Instant Replay tracked state: {GetInstantReplayStateText()}");

        if (ImGui.Button(
                "Instant Replay is currently ON"))
        {
            instantReplayTrackedOn = true;
            pendingInstantReplayEnableUtc = null;
        }

        ImGui.SameLine();

        if (ImGui.Button(
                "Instant Replay is currently OFF"))
        {
            instantReplayTrackedOn = false;

            if (configuration.ManageInstantReplay
                && IsInDuty())
            {
                pendingInstantReplayEnableUtc =
                    DateTime.UtcNow
                    + TimeSpan.FromSeconds(
                        configuration.DutyEntryDelaySeconds);
            }
            else
            {
                pendingInstantReplayEnableUtc =
                    null;
            }
        }

        if (ImGui.Button(
                "Reset tracked state to UNKNOWN"))
        {
            instantReplayTrackedOn = null;
            pendingInstantReplayEnableUtc = null;
        }

        ImGui.TextWrapped(
            "UNKNOWN is a safety state. Death Clipper will NEVER send the Instant Replay toggle while the state is UNKNOWN. " +
            "Tell Death Clipper whether Instant Replay is currently ON or OFF before using automatic duty management.");

        ImGui.Separator();

        ImGui.TextWrapped(
            "Death Clipper monitors deaths and presses the configured save-replay hotkey. " +
            "Optional NVIDIA duty management can toggle Instant Replay when entering and leaving duties.");

        ImGui.End();
    }

    private string GetInstantReplayStateText()
    {
        return instantReplayTrackedOn switch
        {
            true => "ON (tracked)",
            false => "OFF (tracked)",
            null => "UNKNOWN (automatic toggles blocked)",
        };
    }

    private void ValidateHotkey()
    {
        hotkeyIsValid =
            Hotkey.TryParse(
                configuration.SaveReplayHotkey,
                out var hotkey,
                out var error);

        hotkeyValidationMessage =
            hotkeyIsValid
                ? $"Valid hotkey: {hotkey.DisplayName}"
                : error;
    }

    private void SaveConfiguration()
    {
        pluginInterface.SavePluginConfig(
            configuration);
    }
}
