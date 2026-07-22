using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AjazzKeyboard.Models;

public class KeyInfo : INotifyPropertyChanged
{
    public string Id    { get; init; } = "";
    public int    Index { get; init; }
    public double X     { get; init; }
    public double Y     { get; init; }
    public double W     { get; init; } = 90;
    public double H     { get; init; } = 90;

    private string _label = "";
    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(); }
    }

    private string? _imagePath;
    public string? ImagePath
    {
        get => _imagePath;
        set
        {
            _imagePath = value;
            OnPropertyChanged();
            LoadPreview();
        }
    }

    private BitmapImage? _previewImage;
    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set { _previewImage = value; OnPropertyChanged(); }
    }

    private void LoadPreview()
    {
        if (_imagePath == null || !File.Exists(_imagePath))
        {
            PreviewImage = null;
            return;
        }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource    = new Uri(_imagePath);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        PreviewImage = bmp;
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    private KeyAction _action = new();
    public KeyAction Action
    {
        get => _action;
        set
        {
            _action = value ?? new KeyAction();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionDescription));
        }
    }

    public string ActionDescription => Action.Describe();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
