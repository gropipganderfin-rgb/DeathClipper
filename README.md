# Death Clipper

Death Clipper is a local Dalamud plugin for FFXIV. When your character changes from alive to dead during combat, it presses the configured **Save Replay** hotkey.

Default behavior:

- Save Replay hotkey: `F13`
- Deaths outside combat are ignored
- Only the first death in each pull saves a replay
- Ten-second safety cooldown

The plugin does not record or encode video. NVIDIA Instant Replay, OBS Replay Buffer, Xbox Game Bar, AMD Software, or another recorder must already be running with its save-replay shortcut set to `F13`.

## Publish as a GitHub Dalamud repository

The included `.github/workflows/publish.yml` workflow builds the plugin, creates a GitHub Release, generates `repo.json`, and deploys the repository through GitHub Pages.

1. Create a **public** GitHub repository.
2. Upload the **contents** of this folder to the repository root. `DeathClipper.csproj` must be at the root.
3. Confirm `.github/workflows/publish.yml` was uploaded.
4. Open **Settings → Pages** and select **GitHub Actions** as the source.
5. Open **Actions → Publish Death Clipper → Run workflow**.
6. Wait for both the `build` and `deploy` jobs to finish.

Your Dalamud custom repository URL will be:

```text
https://YOUR-GITHUB-NAME.github.io/YOUR-REPOSITORY-NAME/repo.json
```

In FFXIV:

1. Remove any Death Clipper DLL from **Dev Plugin Locations**.
2. Enter `/xlsettings` and open **Experimental**.
3. Add the URL under **Custom Plugin Repositories**.
4. Save, open `/xlplugins`, search for **Death Clipper**, and install it.

Every push to `main` creates a new plugin version so Dalamud can detect and install updates.

## Recorder setup

### NVIDIA App

1. Enable **Instant Replay**.
2. Set its replay duration long enough to cover your pulls.
3. Change **Save Instant Replay** to `F13`.
4. In FFXIV, enter `/deathclip test` and confirm NVIDIA saves a clip.

### OBS

Enable Replay Buffer, assign its save shortcut to `F13`, and start Replay Buffer before playing.

## Commands

- `/deathclip` — open settings
- `/deathclip test` — press F13 immediately
- `/deathclip on` — enable automatic death clips
- `/deathclip off` — disable automatic death clips

## Local build

Requirements:

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- XIVLauncher with Dalamud

Build without using a PowerShell script:

```powershell
dotnet restore .\DeathClipper.csproj
dotnet build .\DeathClipper.csproj -c Release -p:Platform=x64 -o .\PluginBuild
```

The development DLL will be `PluginBuild\DeathClipper.dll`.

## Notes

- Loading the plugin while already dead does not create a clip.
- With **Save only once per pull** enabled, being raised and dying again in the same combat will not create a duplicate clip.
- FFXIV and third-party plugins may violate Square Enix's terms of service. Use at your own discretion.
- This project was created with AI assistance. Dalamud's official repository requires AI use to be disclosed and the code to be personally reviewed and tested before submission.
