using System.Diagnostics;
using System.Runtime.InteropServices;
using AjazzKeyboard.Models;

namespace AjazzKeyboard.Services;

public static class ActionService
{
    [DllImport("user32.dll")] private static extern void  keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern short VkKeyScan(char ch);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static void Execute(KeyAction action)
    {
        switch (action.Type)
        {
            case KeyActionType.Hotkey:    ExecuteHotkey(action);   break;
            case KeyActionType.LaunchApp: ExecuteApp(action);      break;
            case KeyActionType.TextMacro: ExecuteText(action);     break;
        }
    }

    private static void ExecuteHotkey(KeyAction a)
    {
        if (!TryGetVk(a.Key, out byte vk)) return;

        var mods = new List<byte>();
        if (a.Ctrl)  mods.Add(0x11); // VK_CONTROL
        if (a.Alt)   mods.Add(0x12); // VK_MENU
        if (a.Shift) mods.Add(0x10); // VK_SHIFT
        if (a.Win)   mods.Add(0x5B); // VK_LWIN

        foreach (var m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        for (int i = mods.Count - 1; i >= 0; i--)
            keybd_event(mods[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void ExecuteApp(KeyAction a)
    {
        if (string.IsNullOrWhiteSpace(a.AppPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(a.AppPath)
            {
                Arguments       = a.AppArgs,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static void ExecuteText(KeyAction a)
    {
        if (string.IsNullOrEmpty(a.Text)) return;
        foreach (char c in a.Text)
        {
            short scan = VkKeyScan(c);
            if (scan == -1) continue;
            byte vk    = (byte)(scan & 0xFF);
            bool shift = ((scan >> 8) & 1) != 0;
            if (shift) keybd_event(0x10, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (shift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(10);
        }
    }

    private static bool TryGetVk(string keyName, out byte vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(keyName)) return false;
        keyName = keyName.Trim();
        if (_vkMap.TryGetValue(keyName, out vk)) return true;
        if (keyName.Length == 1)
        {
            char c = char.ToUpper(keyName[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = (byte)c; return true; }
        }
        return false;
    }

    private static readonly Dictionary<string, byte> _vkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        {"F1",0x70},{"F2",0x71},{"F3",0x72},{"F4",0x73},
        {"F5",0x74},{"F6",0x75},{"F7",0x76},{"F8",0x77},
        {"F9",0x78},{"F10",0x79},{"F11",0x7A},{"F12",0x7B},
        {"Space",0x20},{"Enter",0x0D},{"Escape",0x1B},{"Esc",0x1B},
        {"Tab",0x09},{"Backspace",0x08},{"Delete",0x2E},{"Insert",0x2D},
        {"Home",0x24},{"End",0x23},{"PageUp",0x21},{"PageDown",0x22},
        {"Up",0x26},{"Down",0x28},{"Left",0x25},{"Right",0x27},
        {"PrintScreen",0x2C},{"ScrollLock",0x91},{"Pause",0x13},
        {"NumLock",0x90},{"CapsLock",0x14},
        {"Num0",0x60},{"Num1",0x61},{"Num2",0x62},{"Num3",0x63},{"Num4",0x64},
        {"Num5",0x65},{"Num6",0x66},{"Num7",0x67},{"Num8",0x68},{"Num9",0x69},
        {"MediaPlay",0xB3},{"MediaStop",0xB2},{"MediaNext",0xB0},{"MediaPrev",0xB1},
        {"VolumeUp",0xAF},{"VolumeDown",0xAE},{"VolumeMute",0xAD},
    };
}
