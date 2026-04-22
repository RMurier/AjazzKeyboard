namespace AjazzKeyboard.Models;

public enum RgbMode
{
    Static = 0,
    Breathing = 1,
    Wave = 2,
    Ripple = 3,
    Rainbow = 4,
    Reactive = 5,
    Off = 6,
}

public class RgbSettings
{
    public RgbMode Mode { get; set; } = RgbMode.Static;
    public byte Red { get; set; } = 255;
    public byte Green { get; set; } = 255;
    public byte Blue { get; set; } = 255;
    public byte Speed { get; set; } = 3;
    public byte Brightness { get; set; } = 5;
    public bool DirectionReversed { get; set; } = false;
}
