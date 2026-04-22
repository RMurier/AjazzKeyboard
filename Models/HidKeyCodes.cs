namespace AjazzKeyboard.Models;

public static class HidKeyCodes
{
    public static readonly Dictionary<byte, string> Names = new()
    {
        { 0x04, "A" }, { 0x05, "B" }, { 0x06, "C" }, { 0x07, "D" },
        { 0x08, "E" }, { 0x09, "F" }, { 0x0A, "G" }, { 0x0B, "H" },
        { 0x0C, "I" }, { 0x0D, "J" }, { 0x0E, "K" }, { 0x0F, "L" },
        { 0x10, "M" }, { 0x11, "N" }, { 0x12, "O" }, { 0x13, "P" },
        { 0x14, "Q" }, { 0x15, "R" }, { 0x16, "S" }, { 0x17, "T" },
        { 0x18, "U" }, { 0x19, "V" }, { 0x1A, "W" }, { 0x1B, "X" },
        { 0x1C, "Y" }, { 0x1D, "Z" },
        { 0x1E, "1" }, { 0x1F, "2" }, { 0x20, "3" }, { 0x21, "4" },
        { 0x22, "5" }, { 0x23, "6" }, { 0x24, "7" }, { 0x25, "8" },
        { 0x26, "9" }, { 0x27, "0" },
        { 0x28, "Enter" }, { 0x29, "ESC" }, { 0x2A, "Backspace" },
        { 0x2B, "Tab" }, { 0x2C, "Space" },
        { 0x2D, "-" }, { 0x2E, "=" }, { 0x2F, "[" }, { 0x30, "]" },
        { 0x31, "\\" }, { 0x33, ";" }, { 0x34, "'" }, { 0x35, "`" },
        { 0x36, "," }, { 0x37, "." }, { 0x38, "/" },
        { 0x39, "CapsLock" },
        { 0x3A, "F1" }, { 0x3B, "F2" }, { 0x3C, "F3" }, { 0x3D, "F4" },
        { 0x3E, "F5" }, { 0x3F, "F6" }, { 0x40, "F7" }, { 0x41, "F8" },
        { 0x42, "F9" }, { 0x43, "F10" }, { 0x44, "F11" }, { 0x45, "F12" },
        { 0x4C, "Delete" }, { 0x49, "Insert" }, { 0x4A, "Home" },
        { 0x4B, "PageUp" }, { 0x4D, "End" }, { 0x4E, "PageDown" },
        { 0x4F, "Right" }, { 0x50, "Left" }, { 0x51, "Down" }, { 0x52, "Up" },
        { 0xE0, "L-Ctrl" }, { 0xE1, "L-Shift" }, { 0xE2, "L-Alt" }, { 0xE3, "L-Win" },
        { 0xE4, "R-Ctrl" }, { 0xE5, "R-Shift" }, { 0xE6, "R-Alt" }, { 0xE7, "R-Win" },
    };

    public static readonly Dictionary<string, byte> Codes =
        Names.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static string GetName(byte code) =>
        Names.TryGetValue(code, out var name) ? name : $"0x{code:X2}";
}
