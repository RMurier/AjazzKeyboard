using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AjazzKeyboard.Models;
using AjazzKeyboard.Services;
using Microsoft.Win32;

namespace AjazzKeyboard.ViewModels;

public partial class KeyboardPageViewModel : ObservableObject
{
    private readonly HidService _hid;

    [ObservableProperty] private ObservableCollection<KeyInfo> _keys = [];
    [ObservableProperty] private KeyInfo? _selectedKey;
    [ObservableProperty] private string _editLabel = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isConnected;

    // ── Action editing ───────────────────────────────────────────────────────

    [ObservableProperty] private KeyActionType _editActionType = KeyActionType.None;
    [ObservableProperty] private bool   _editCtrl;
    [ObservableProperty] private bool   _editAlt;
    [ObservableProperty] private bool   _editShift;
    [ObservableProperty] private bool   _editWin;
    [ObservableProperty] private string _editHotkeyKey  = "";
    [ObservableProperty] private string _editAppPath    = "";
    [ObservableProperty] private string _editMacroText  = "";

    public static readonly string[] ActionTypeNames =
        ["No action", "Keyboard shortcut", "Launch application", "Type text"];

    public string EditActionTypeName
    {
        get => ActionTypeNames[(int)EditActionType];
        set
        {
            int idx = Array.IndexOf(ActionTypeNames, value);
            if (idx >= 0) EditActionType = (KeyActionType)idx;
        }
    }

    public bool IsHotkeyAction => EditActionType == KeyActionType.Hotkey;
    public bool IsAppAction    => EditActionType == KeyActionType.LaunchApp;
    public bool IsMacroAction  => EditActionType == KeyActionType.TextMacro;

    partial void OnEditActionTypeChanged(KeyActionType value)
    {
        OnPropertyChanged(nameof(EditActionTypeName));
        OnPropertyChanged(nameof(IsHotkeyAction));
        OnPropertyChanged(nameof(IsAppAction));
        OnPropertyChanged(nameof(IsMacroAction));
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    public KeyboardPageViewModel(HidService hid)
    {
        _hid = hid;
        _hid.ConnectionChanged += (_, connected) =>
        {
            IsConnected   = connected;
            StatusMessage = connected ? "AKP153 connected." : "AKP153 disconnected.";
        };
        _hid.ButtonPressed += (_, index) =>
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (index < 0 || index >= Keys.Count) return;
                var key = Keys[index];
                SelectKey(key);
                if (key.Action.Type != KeyActionType.None)
                    Task.Run(() => ActionService.Execute(key.Action));
            });

        foreach (var key in KeyboardLayout.Build())
            Keys.Add(key);

        IsConnected = _hid.IsConnected;
    }

    // ── Key selection ────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectKey(KeyInfo? key)
    {
        if (SelectedKey != null) SelectedKey.IsSelected = false;
        SelectedKey = key;
        if (SelectedKey == null) return;

        SelectedKey.IsSelected = true;
        EditLabel = key!.Label;

        var a = key.Action;
        EditActionType = a.Type;
        EditCtrl       = a.Ctrl;
        EditAlt        = a.Alt;
        EditShift      = a.Shift;
        EditWin        = a.Win;
        EditHotkeyKey  = a.Key;
        EditAppPath    = a.AppPath;
        EditMacroText  = a.Text;
    }

    // ── Label ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ApplyLabel()
    {
        if (SelectedKey == null) return;
        SelectedKey.Label = EditLabel;
        StatusMessage = "Label updated.";
    }

    // ── Action ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ApplyAction()
    {
        if (SelectedKey == null) return;
        SelectedKey.Action = new KeyAction
        {
            Type    = EditActionType,
            Ctrl    = EditCtrl,
            Alt     = EditAlt,
            Shift   = EditShift,
            Win     = EditWin,
            Key     = EditHotkeyKey.Trim(),
            AppPath = EditAppPath.Trim(),
            AppArgs = "",
            Text    = EditMacroText,
        };
        StatusMessage = $"Action: {SelectedKey.Action.Describe()}";
    }

    [RelayCommand]
    private void BrowseApp()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select application",
            Filter = "Executables|*.exe|All files|*.*",
        };
        if (dlg.ShowDialog() == true)
            EditAppPath = dlg.FileName;
    }

    // ── Image ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void PickImage()
    {
        if (SelectedKey == null) return;

        var dlg = new OpenFileDialog
        {
            Title  = $"Image for key {SelectedKey.Index + 1}",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        SelectedKey.ImagePath = dlg.FileName;
        StatusMessage = $"Image: {Path.GetFileName(dlg.FileName)}";
    }

    [RelayCommand]
    private void ClearImage()
    {
        if (SelectedKey == null) return;
        SelectedKey.ImagePath = null;
        SelectedKey.Label = $"Key {SelectedKey.Index + 1}";
        EditLabel = SelectedKey.Label;
        _hid.ClearKey(SelectedKey.Index);
        StatusMessage = "Key cleared.";
    }

    // ── Device ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ScanDevices()
    {
        var lines = HidService.GetAllDevices();
        MessageBox.Show(
            string.Join(Environment.NewLine, lines),
            "Connected HID Devices",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void SendToDevice()
    {
        if (SelectedKey == null) return;
        if (!_hid.IsConnected) { StatusMessage = "Not connected."; return; }

        byte[] jpeg;
        if (SelectedKey.PreviewImage != null)
            jpeg = HidService.EncodeJpeg(SelectedKey.PreviewImage);
        else if (!string.IsNullOrWhiteSpace(SelectedKey.Label))
            jpeg = RenderLabelToJpeg(SelectedKey.Label);
        else
        {
            StatusMessage = "Nothing to send (add a label or image).";
            return;
        }

        bool ok = _hid.SetKeyImage(SelectedKey.Index, jpeg);
        StatusMessage = ok ? $"Key {SelectedKey.Index + 1} sent to device." : "Send failed.";
    }

    [RelayCommand]
    private void TryConnect()
    {
        bool ok = _hid.TryConnect();
        StatusMessage = ok ? $"Connected — {_hid.DeviceInfo}" : "AKP153 not found. Check USB.";
        IsConnected   = ok;
    }

    [RelayCommand]
    private void TestBrightness()
    {
        if (!_hid.IsConnected) { StatusMessage = "Not connected."; return; }
        bool ok = _hid.SetBrightness(80);
        StatusMessage = ok ? "LIG command sent (brightness 80%) — did screens react?"
                           : "Write() returned false — HID write failed.";
    }

    // ── Profile support ──────────────────────────────────────────────────────

    public void LoadProfile(KeyboardProfile profile)
    {
        foreach (var key in Keys)
        {
            if (profile.KeyBindings.TryGetValue(key.Id, out var value))
            {
                if (File.Exists(value)) key.ImagePath = value;
                else                    key.Label     = value;
            }
            if (profile.Actions.TryGetValue(key.Id, out var action))
                key.Action = action;
        }
    }

    public void SaveToProfile(KeyboardProfile profile)
    {
        profile.KeyBindings.Clear();
        profile.Actions.Clear();
        foreach (var key in Keys)
        {
            if (!string.IsNullOrEmpty(key.ImagePath))
                profile.KeyBindings[key.Id] = key.ImagePath;
            else if (!string.IsNullOrEmpty(key.Label))
                profile.KeyBindings[key.Id] = key.Label;

            if (key.Action.Type != KeyActionType.None)
                profile.Actions[key.Id] = key.Action;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] RenderLabelToJpeg(string label)
    {
        const int size = HidService.ImageSize;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
                null,
                new Rect(0, 0, size, size));

            var ft = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                Brushes.White,
                96.0)
            {
                MaxTextWidth  = size - 8,
                MaxTextHeight = size - 8,
                TextAlignment = TextAlignment.Center,
            };
            dc.DrawText(ft, new Point(4, (size - ft.Height) / 2));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return HidService.EncodeJpeg(rtb);
    }
}
