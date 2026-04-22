using System.IO;

namespace AjazzKeyboard.Models;

public enum KeyActionType { None, Hotkey, LaunchApp, TextMacro }

public class KeyAction
{
    public KeyActionType Type    { get; set; } = KeyActionType.None;

    // Hotkey
    public bool   Ctrl  { get; set; }
    public bool   Alt   { get; set; }
    public bool   Shift { get; set; }
    public bool   Win   { get; set; }
    public string Key   { get; set; } = "";

    // LaunchApp
    public string AppPath { get; set; } = "";
    public string AppArgs { get; set; } = "";

    // TextMacro
    public string Text { get; set; } = "";

    public string Describe() => Type switch
    {
        KeyActionType.Hotkey =>
            $"{(Ctrl ? "Ctrl+" : "")}{(Alt ? "Alt+" : "")}{(Shift ? "Shift+" : "")}{(Win ? "Win+" : "")}{Key}"
            .TrimEnd('+'),
        KeyActionType.LaunchApp =>
            string.IsNullOrEmpty(AppPath) ? "App: (none)" : $"App: {Path.GetFileName(AppPath)}",
        KeyActionType.TextMacro =>
            string.IsNullOrEmpty(Text) ? "Type: (empty)"
                                       : $"Type: {(Text.Length > 20 ? Text[..20] + "…" : Text)}",
        _ => "No action",
    };
}
