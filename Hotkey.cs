using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeathClipper;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8,
}

internal readonly record struct ParsedHotkey(HotkeyModifiers Modifiers, ushort VirtualKey, string DisplayName);

internal static class Hotkey
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkAlt = 0x12;
    private const ushort VkLeftWindows = 0x5B;

    public static bool TryParse(string text, out ParsedHotkey hotkey, out string error)
    {
        hotkey = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a hotkey such as ALT+F10.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        ushort primaryKey = 0;
        string? primaryName = null;

        foreach (var rawPart in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.ToUpperInvariant();
            switch (part)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyModifiers.Windows;
                    continue;
            }

            if (primaryKey != 0)
            {
                error = "A hotkey can contain only one non-modifier key.";
                return false;
            }

            if (!TryParsePrimaryKey(part, out primaryKey))
            {
                error = $"Unsupported key '{rawPart}'. Use A-Z, 0-9, or F1-F24.";
                return false;
            }

            primaryName = part;
        }

        if (primaryKey == 0 || primaryName is null)
        {
            error = "The hotkey needs a non-modifier key, such as F10.";
            return false;
        }

        var displayParts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) displayParts.Add("CTRL");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) displayParts.Add("SHIFT");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) displayParts.Add("ALT");
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) displayParts.Add("WIN");
        displayParts.Add(primaryName);

        hotkey = new ParsedHotkey(modifiers, primaryKey, string.Join('+', displayParts));
        return true;
    }

    public static bool TrySend(ParsedHotkey hotkey, out string error)
    {
        error = string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            error = "Replay hotkeys can only be sent on Windows.";
            return false;
        }

        var pressedModifiers = GetModifierKeys(hotkey.Modifiers);
        var inputs = new List<Input>(pressedModifiers.Count * 2 + 2);

        foreach (var key in pressedModifiers)
            inputs.Add(CreateKeyboardInput(key, keyUp: false));

        inputs.Add(CreateKeyboardInput(hotkey.VirtualKey, keyUp: false));
        inputs.Add(CreateKeyboardInput(hotkey.VirtualKey, keyUp: true));

        for (var index = pressedModifiers.Count - 1; index >= 0; index--)
            inputs.Add(CreateKeyboardInput(pressedModifiers[index], keyUp: true));

        var inputArray = inputs.ToArray();
        var sent = SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf<Input>());
        if (sent == (uint)inputArray.Length)
            return true;

        error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return false;
    }

    private static bool TryParsePrimaryKey(string part, out ushort key)
    {
        key = 0;

        if (part.Length == 1)
        {
            var character = part[0];
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = character;
                return true;
            }
        }

        if (part.StartsWith('F')
            && int.TryParse(part.AsSpan(1), out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            key = (ushort)(0x70 + functionNumber - 1);
            return true;
        }

        return false;
    }

    private static List<ushort> GetModifierKeys(HotkeyModifiers modifiers)
    {
        var keys = new List<ushort>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control)) keys.Add(VkControl);
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) keys.Add(VkShift);
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) keys.Add(VkAlt);
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) keys.Add(VkLeftWindows);
        return keys;
    }

    private static Input CreateKeyboardInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyEventKeyUp : 0,
            },
        },
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        // SendInput validates cbSize against the complete native INPUT union.
        // Including MOUSEINPUT keeps this union at the required 32 bytes on x64,
        // even though Death Clipper only sends KEYBDINPUT values.
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
