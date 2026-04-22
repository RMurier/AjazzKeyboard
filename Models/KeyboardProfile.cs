namespace AjazzKeyboard.Models;

public class KeyboardProfile
{
    public string Name { get; set; } = "Default";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    public Dictionary<string, string>    KeyBindings { get; set; } = new();
    public Dictionary<string, KeyAction> Actions     { get; set; } = new();
    public RgbSettings Rgb { get; set; } = new();
}
