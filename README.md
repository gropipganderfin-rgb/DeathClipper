# Death Clipper

Death Clipper is a local Dalamud plugin for FFXIV. When you or a monitored
party member changes from alive to dead during combat, it presses the
configured Save Replay hotkey.

## Default death-clip behavior

- Save Replay hotkey: `F13`
- Automatic death clips save five seconds after a detected death
- Party-member death detection is enabled by default
- Deaths outside combat are ignored
- Only the first death in each pull saves a replay
- Ten-second safety cooldown

## NVIDIA Instant Replay duty management

Version 2.1 adds optional NVIDIA Instant Replay management.

When enabled:

1. Enter a duty.
2. Death Clipper detects a Dalamud BoundByDuty condition.
3. It waits the configured number of seconds.
4. It presses the configured NVIDIA Instant Replay toggle hotkey.
5. Death clipping continues normally.
6. When you leave the duty, Death Clipper can toggle Instant Replay back off.

The duty-management feature is OFF by default so existing users are not
unexpectedly affected after updating.

The default Instant Replay toggle shortcut is:

`ALT+T`

Existing configurations that still contain the previous default
`ALT+SHIFT+F10` are automatically migrated to `ALT+T` when loaded.

This can be changed in Death Clipper settings.

### Important state limitation

NVIDIA does not expose a reliable Instant Replay ON/OFF state to Death Clipper.

Death Clipper therefore tracks the state based on the toggle hotkeys that the
plugin itself sends.

The settings window shows one of:

- `ON (tracked)`
- `OFF (tracked)`
- `UNKNOWN`

If Instant Replay is manually toggled outside Death Clipper, the tracked state
may no longer match NVIDIA. Use **Reset tracked state to UNKNOWN** when needed.

## NVIDIA setup

1. Open the NVIDIA overlay.
2. Confirm Instant Replay works normally.
3. Confirm the Instant Replay toggle shortcut matches the shortcut configured
   in Death Clipper.
4. Configure the replay length you want.
5. Configure Death Clipper's Save Replay hotkey.
6. Use `/deathclip test` to test saving a replay.
7. Enable **Automatically manage Instant Replay in duties** if desired.

## Commands

- `/deathclip` - open settings
- `/deathclip test` - immediately press the Save Replay hotkey
- `/deathclip on` - enable automatic death clips
- `/deathclip off` - disable automatic death clips
- `/deathclip replaystatus` - show Death Clipper's tracked Instant Replay state
- `/deathclip forgetreplaystate` - reset tracked Instant Replay state to UNKNOWN

## Notes

- Loading the plugin while a monitored player is already dead does not create a clip.
- Loading Death Clipper while already inside a duty does not blindly toggle
  Instant Replay.
- With **Save only once per pull** enabled, additional deaths in the same combat
  will not create duplicate clips.
- FFXIV and third-party plugins may violate Square Enix's terms of service.
  Use at your own discretion.
- This project was created with AI assistance. Dalamud's official repository
  requires AI use to be disclosed and the code to be personally reviewed and
  tested before submission.
