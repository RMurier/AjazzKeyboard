using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AjazzKeyboard.Models;
using AjazzKeyboard.Services;

namespace AjazzKeyboard.ViewModels;

public partial class RgbPageViewModel : ObservableObject
{
    private readonly HidService _hid;

    [ObservableProperty] private RgbMode _selectedMode = RgbMode.Static;
    [ObservableProperty] private int _red = 255;
    [ObservableProperty] private int _green = 100;
    [ObservableProperty] private int _blue = 220;
    [ObservableProperty] private int _speed = 3;
    [ObservableProperty] private int _brightness = 5;
    [ObservableProperty] private bool _directionReversed;
    [ObservableProperty] private Brush _previewBrush = Brushes.White;
    [ObservableProperty] private string _statusMessage = "";

    public IEnumerable<RgbMode> AllModes => Enum.GetValues<RgbMode>();
    public bool ColorPickerEnabled => SelectedMode is RgbMode.Static or RgbMode.Breathing or RgbMode.Reactive;
    public bool SpeedEnabled => SelectedMode is not RgbMode.Static and not RgbMode.Off;

    public RgbPageViewModel(HidService hid)
    {
        _hid = hid;
        UpdatePreview();
    }

    partial void OnRedChanged(int value)   => UpdatePreview();
    partial void OnGreenChanged(int value) => UpdatePreview();
    partial void OnBlueChanged(int value)  => UpdatePreview();
    partial void OnSelectedModeChanged(RgbMode value)
    {
        OnPropertyChanged(nameof(ColorPickerEnabled));
        OnPropertyChanged(nameof(SpeedEnabled));
    }

    private void UpdatePreview() =>
        PreviewBrush = new SolidColorBrush(Color.FromRgb((byte)Red, (byte)Green, (byte)Blue));

    [RelayCommand]
    private void Apply()
    {
        var settings = BuildSettings();
        bool ok = _hid.SetRgbMode(settings);
        StatusMessage = ok ? "RGB applied to keyboard." : "Settings saved (keyboard not connected).";
    }

    [RelayCommand]
    private void SetPreset(string preset)
    {
        switch (preset)
        {
            case "Red":    (Red, Green, Blue) = (255, 0,   0);   break;
            case "Green":  (Red, Green, Blue) = (0,   255, 0);   break;
            case "Blue":   (Red, Green, Blue) = (0,   0,   255); break;
            case "White":  (Red, Green, Blue) = (255, 255, 255); break;
            case "Cyan":   (Red, Green, Blue) = (0,   255, 255); break;
            case "Magenta":(Red, Green, Blue) = (255, 0,   255); break;
            case "Yellow": (Red, Green, Blue) = (255, 255, 0);   break;
            case "Orange": (Red, Green, Blue) = (255, 140, 0);   break;
        }
    }

    public RgbSettings BuildSettings() => new()
    {
        Mode              = SelectedMode,
        Red               = (byte)Red,
        Green             = (byte)Green,
        Blue              = (byte)Blue,
        Speed             = (byte)Speed,
        Brightness        = (byte)Brightness,
        DirectionReversed = DirectionReversed,
    };

    public void LoadFromSettings(RgbSettings s)
    {
        SelectedMode      = s.Mode;
        Red               = s.Red;
        Green             = s.Green;
        Blue              = s.Blue;
        Speed             = s.Speed;
        Brightness        = s.Brightness;
        DirectionReversed = s.DirectionReversed;
    }
}
