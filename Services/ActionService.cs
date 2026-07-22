using System.Diagnostics;
using System.Runtime.InteropServices;
using AjazzKeyboard.Models;

namespace AjazzKeyboard.Services;

public static class ActionService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ShellExecute(IntPtr hwnd, string lpOperation, string lpFile, string lpParameters, string lpDirectory, int nShowCmd);

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public static IntPtr CaptureForeground() => GetForegroundWindow();

    public static void Execute(KeyAction action, IntPtr targetWindow = default)
    {
        Log.Write($"Executing action: {action.Describe()} (target: {targetWindow})");

        // Restore focus if needed
        if (targetWindow != IntPtr.Zero)
        {
            SetForegroundWindow(targetWindow);
            Thread.Sleep(150); // Give Windows time to switch focus
        }

        switch (action.Type)
        {
            case KeyActionType.Hotkey:    ExecuteHotkey(action); break;
            case KeyActionType.LaunchApp: ExecuteApp(action);    break;
            case KeyActionType.TextMacro: ExecuteText(action);   break;
        }
    }

    private static void ExecuteHotkey(KeyAction a)
    {
        if (!TryGetVk(a.Key, out byte vk))
        {
            Log.Write($"ExecuteHotkey: unrecognized key name '{a.Key}', nothing sent.");
            return;
        }

        var mods = new List<byte>();
        if (a.Ctrl)  mods.Add(0x11);
        if (a.Alt)   mods.Add(0x12);
        if (a.Shift) mods.Add(0x10);
        if (a.Win)   mods.Add(0x5B);

        var inputs = new List<INPUT>();
        foreach (var m in mods) inputs.Add(CreateKeyInput(m, 0));
        inputs.Add(CreateKeyInput(vk, 0));
        inputs.Add(CreateKeyInput(vk, KEYEVENTF_KEYUP));
        for (int i = mods.Count - 1; i >= 0; i--)
            inputs.Add(CreateKeyInput(mods[i], KEYEVENTF_KEYUP));

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        Log.Write($"Hotkey SendInput sent {sent}/{inputs.Count} inputs");
    }

    private static void ExecuteApp(KeyAction a)
    {
        if (string.IsNullOrWhiteSpace(a.AppPath))
        {
            Log.Write("ExecuteApp: AppPath is empty, nothing launched.");
            return;
        }
        try
        {
            Log.Write($"Launching: {a.AppPath} with args: {a.AppArgs}");
            var psi = new ProcessStartInfo
            {
                FileName = a.AppPath,
                Arguments = a.AppArgs,
                UseShellExecute = true,
                Verb = "open"
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Write($"Launch failed: {ex.Message}. Falling back to ShellExecute.");
            ShellExecute(IntPtr.Zero, "open", a.AppPath, a.AppArgs ?? "", null, 1);
        }
    }

    private static void ExecuteText(KeyAction a)
    {
        if (string.IsNullOrEmpty(a.Text))
        {
            Log.Write("ExecuteText: Text is empty, nothing sent.");
            return;
        }

        var inputs = new List<INPUT>();
        foreach (char c in a.Text)
        {
            // For standard newlines, send Enter key
            if (c == '\n' || c == '\r')
            {
                if (c == '\r' && a.Text.Contains("\r\n")) continue; // Skip \r in \r\n to avoid double enter
                inputs.Add(CreateKeyInput(0x0D, 0));
                inputs.Add(CreateKeyInput(0x0D, KEYEVENTF_KEYUP));
            }
            else
            {
                inputs.Add(CreateUnicodeInput(c, 0));
                inputs.Add(CreateUnicodeInput(c, KEYEVENTF_KEYUP));
            }
        }

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        Log.Write($"Text SendInput sent {sent}/{inputs.Count} inputs");
    }

    private static INPUT CreateKeyInput(byte vk, uint flags) => new()
    {
        type = (uint)INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };

    private static INPUT CreateUnicodeInput(char c, uint flags) => new()
    {
        type = (uint)INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = (ushort)c, dwFlags = flags | KEYEVENTF_UNICODE } }
    };

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
