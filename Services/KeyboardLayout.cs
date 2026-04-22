using AjazzKeyboard.Models;

namespace AjazzKeyboard.Services;

/// <summary>
/// AJAZZ AKP153 layout: 15 LCD keys in a 3×5 grid.
/// </summary>
public static class KeyboardLayout
{
    private const double KEY_SIZE = 90.0;
    private const double GAP      = 10.0;

    public const int Rows     = 3;
    public const int Cols     = 5;
    public const int KeyCount = Rows * Cols; // 15

    public static List<KeyInfo> Build()
    {
        var keys = new List<KeyInfo>(KeyCount);
        for (int i = 0; i < KeyCount; i++)
        {
            keys.Add(new KeyInfo
            {
                Id    = $"key_{i}",
                Index = i,
                Label = $"Key {i + 1}",
                X     = (i % Cols) * (KEY_SIZE + GAP),
                Y     = (i / Cols) * (KEY_SIZE + GAP),
                W     = KEY_SIZE,
                H     = KEY_SIZE,
            });
        }
        return keys;
    }
}
