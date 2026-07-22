using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly ProfileService _profileService;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusMessage = "Disconnected";
    [ObservableProperty] private ObservableCollection<KeyInfo> _keys = new();
    [ObservableProperty] private KeyInfo? _selectedKey;

    // Edit fields
    [ObservableProperty] private string _editLabel = "";
    [ObservableProperty] private KeyActionType _editActionType = KeyActionType.None;
    [ObservableProperty] private bool   _editCtrl;
    [ObservableProperty] private bool   _editAlt;
    [ObservableProperty] private bool   _editShift;
    [ObservableProperty] private bool   _editWin;
    [ObservableProperty] private string _editHotkeyKey  = "";
    [ObservableProperty] private string _editAppPath    = "";
    [ObservableProperty] private string _editAppArgs    = "";
    [ObservableProperty] private string _editMacroText  = "";

    public static readonly string[] ActionTypeNames =
        ["No action", "Keyboard shortcut", "Launch application", "Type text"];

    public string EditActionTypeName
    {
        get => ActionTypeNames[(int)EditActionType];
        set => EditActionType = (KeyActionType)Array.IndexOf(ActionTypeNames, value);
    }

    public bool IsHotkeyAction => EditActionType == KeyActionType.Hotkey;
    public bool IsAppAction    => EditActionType == KeyActionType.LaunchApp;
    public bool IsMacroAction  => EditActionType == KeyActionType.TextMacro;

    public KeyboardPageViewModel(HidService hid, ProfileService profileService)
    {
        _hid = hid;
        _profileService = profileService;

        // Create default keys
        for (int i = 0; i < HidService.KeyCount; i++)
            Keys.Add(new KeyInfo { Index = i, Label = $"Key {i + 1}" });

        // Keep the "Connected" badge and status text in sync with the actual HID
        // connection state, whether it changes from the startup auto-connect, a
        // manual Connect click, or a physical disconnect detected in HidService.
        _hid.ConnectionChanged += (_, connected) =>
        {
            Log.Write($"KeyboardPageViewModel: ConnectionChanged -> {connected}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = connected;
                StatusMessage = connected ? $"Connected — {_hid.DeviceInfo}" : "Disconnected.";
            });
        };

        _hid.ButtonPressed += (_, index) =>
        {
            Log.Write($"KeyboardPageViewModel: ButtonPressed software index={index}");
            if (index < 0 || index >= Keys.Count)
            {
                Log.Write($"KeyboardPageViewModel: ButtonPressed index {index} out of range (Keys.Count={Keys.Count}), ignored.");
                return;
            }
            var key = Keys[index];
            Log.Write($"KeyboardPageViewModel: key {index} action={key.Action.Describe()}");

            var target = ActionService.CaptureForeground();

            Application.Current.Dispatcher.Invoke(() =>
            {
                SelectKey(key);
                StatusMessage = $"Key {index + 1} pressed: {key.Action.Describe()}";
            });

            if (key.Action.Type != KeyActionType.None)
                ActionService.Execute(key.Action, target);
        };
    }

    [RelayCommand]
    private void SelectKey(KeyInfo? key)
    {
        try
        {
            Log.Write($"SelectKey: index={key?.Index.ToString() ?? "null"} calibrating={_isCalibrating}");
            if (_isCalibrating)
            {
                if (key != null) _ = HandleCalibrationClick(key);
                return;
            }

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
            EditAppArgs    = a.AppArgs;
            EditMacroText  = a.Text;
        }
        catch (Exception ex)
        {
            Log.Write($"SelectKey: EXCEPTION: {ex}");
        }
    }

    [RelayCommand]
    private void ApplyLabel()
    {
        if (SelectedKey == null) return;
        try
        {
            Log.Write($"ApplyLabel: key {SelectedKey.Index} -> '{EditLabel}'");
            SelectedKey.Label = EditLabel;
            StatusMessage = "Label updated.";
        }
        catch (Exception ex)
        {
            Log.Write($"ApplyLabel: EXCEPTION: {ex}");
        }
    }

    [RelayCommand]
    private void ApplyAction()
    {
        if (SelectedKey == null) return;
        try
        {
            SelectedKey.Action = new KeyAction
            {
                Type    = EditActionType,
                Ctrl    = EditCtrl,
                Alt     = EditAlt,
                Shift   = EditShift,
                Win     = EditWin,
                Key     = EditHotkeyKey.Trim(),
                AppPath = EditAppPath.Trim(),
                AppArgs = EditAppArgs.Trim(),
                Text    = EditMacroText,
            };
            Log.Write($"ApplyAction: key {SelectedKey.Index} -> {SelectedKey.Action.Describe()}");
            StatusMessage = $"Action: {SelectedKey.Action.Describe()}";
        }
        catch (Exception ex)
        {
            Log.Write($"ApplyAction: EXCEPTION: {ex}");
        }
    }

    [RelayCommand]
    private void PickImage()
    {
        if (SelectedKey == null) return;
        try
        {
            var ofd = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
            if (ofd.ShowDialog() == true)
            {
                Log.Write($"PickImage: key {SelectedKey.Index} -> '{ofd.FileName}'");
                SelectedKey.ImagePath = ofd.FileName;
                StatusMessage = "Image selected.";
            }
        }
        catch (Exception ex)
        {
            Log.Write($"PickImage: EXCEPTION: {ex}");
            StatusMessage = $"Image pick crashed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearImage()
    {
        if (SelectedKey == null) return;
        try
        {
            Log.Write($"ClearImage: key {SelectedKey.Index}");
            SelectedKey.ImagePath = null;
            StatusMessage = "Image cleared.";
        }
        catch (Exception ex)
        {
            Log.Write($"ClearImage: EXCEPTION: {ex}");
        }
    }

    [RelayCommand]
    private void SendToDevice()
    {
        if (SelectedKey == null) return;
        if (!_hid.IsConnected) { StatusMessage = "Not connected."; return; }

        ApplyAction();

        byte[] jpeg;
        if (SelectedKey.PreviewImage != null)
            jpeg = HidService.EncodeJpeg(SelectedKey.PreviewImage);
        else if (!string.IsNullOrWhiteSpace(SelectedKey.Label))
            jpeg = RenderLabelToJpeg(SelectedKey.Label);
        else
        {
            StatusMessage = "Nothing to send.";
            return;
        }

        int keyIndex = SelectedKey.Index;
        Log.Write($"SendToDevice: key {keyIndex}, jpeg {jpeg.Length} bytes.");
        StatusMessage = $"Sending key {keyIndex + 1}...";
        _ = Task.Run(() =>
        {
            try
            {
                _hid.SuspendReading();
                bool ok;
                try { ok = _hid.SetKeyImage(keyIndex, jpeg); }
                finally { _hid.ResumeReading(); }
                Log.Write($"SendToDevice: key {keyIndex} -> {(ok ? "OK" : "FAILED")}.");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = ok ? $"Key {keyIndex + 1} sent to box." : $"FAILED to send key {keyIndex + 1}.";
                });
            }
            catch (Exception ex)
            {
                Log.Write($"SendToDevice: EXCEPTION: {ex}");
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Send crashed: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void SendAllToDevice()
    {
        if (!_hid.IsConnected) { StatusMessage = "Not connected."; return; }
        ApplyAction();

        var sends = new List<(int index, byte[] jpeg)>();
        foreach (var key in Keys)
        {
            byte[] jpeg;
            if (key.PreviewImage != null)
                jpeg = HidService.EncodeJpeg(key.PreviewImage);
            else if (!string.IsNullOrWhiteSpace(key.Label))
                jpeg = RenderLabelToJpeg(key.Label);
            else continue;
            sends.Add((key.Index, jpeg));
        }

        Log.Write($"SendAllToDevice: {sends.Count} key(s) queued.");
        StatusMessage = $"Sending {sends.Count} keys...";
        _ = Task.Run(() =>
        {
            int successCount = 0;
            try
            {
                _hid.SuspendReading();
                try
                {
                    foreach (var (idx, jpeg) in sends)
                    {
                        bool ok = _hid.SetKeyImage(idx, jpeg);
                        Log.Write($"SendAllToDevice: key {idx} -> {(ok ? "OK" : "FAILED")}.");
                        if (ok) successCount++;
                    }
                }
                finally
                {
                    _hid.ResumeReading();
                }
                Log.Write($"SendAllToDevice: done, {successCount}/{sends.Count} succeeded.");
                Application.Current.Dispatcher.Invoke(() =>
                    StatusMessage = $"Sent {successCount}/{sends.Count} keys.");
            }
            catch (Exception ex)
            {
                Log.Write($"SendAllToDevice: EXCEPTION after {successCount}/{sends.Count}: {ex}");
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Send crashed: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private async Task SimulatePress()
    {
        if (SelectedKey == null) return;
        try
        {
            Log.Write($"SimulatePress: key {SelectedKey.Index}, action={SelectedKey.Action.Describe()}");
            StatusMessage = "Simulating in 2s...";
            await Task.Delay(2000);
            var target = ActionService.CaptureForeground();
            ActionService.Execute(SelectedKey.Action, target);
            StatusMessage = "Simulated.";
        }
        catch (Exception ex)
        {
            Log.Write($"SimulatePress: EXCEPTION: {ex}");
            StatusMessage = $"Simulate crashed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseApp()
    {
        try
        {
            var ofd = new OpenFileDialog { Filter = "Applications (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == true)
            {
                Log.Write($"BrowseApp: selected '{ofd.FileName}'");
                EditAppPath = ofd.FileName;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"BrowseApp: EXCEPTION: {ex}");
        }
    }

    // Side screen segments (no physical button behind them) confirmed addressable
    // separately from the 15-key grid — see ProbeKeyRange findings.
    private static readonly int[] SideScreenRawIndices = [16, 17, 18];

    [RelayCommand]
    private async Task ShowGridNumbers()
    {
        if (!_hid.IsConnected) { Log.Write("ShowGridNumbers: aborted, not connected."); StatusMessage = "Connect first!"; return; }

        Log.Write("ShowGridNumbers: starting.");
        int total = HidService.KeyCount + SideScreenRawIndices.Length;
        int okCount = 0;
        // Each key now gets a ~17s uninterrupted window after its write (see
        // HidService.TrySendKeyImageOnce) so the panel can actually commit the frame
        // instead of having it interrupted by the next key's BAT. Sending the same
        // key repeatedly no longer helps — it just interrupts its own commit — so a
        // single send per key is both simpler and more likely to actually stick.
        _hid.SuspendReading();
        try
        {
            // Main 3x5 grid: go through the software slot -> verified real device
            // index mapping (do NOT send raw indices directly here — the panel is
            // 1-indexed 1-15, not 0-14, and bypassing the mapping hits an invalid
            // index and skips a valid one).
            for (int swIndex = 0; swIndex < HidService.KeyCount; swIndex++)
            {
                byte[] jpeg = RenderLabelToJpeg((swIndex + 1).ToString());
                StatusMessage = $"Sending key position {swIndex + 1}/{HidService.KeyCount} (~17s each, be patient)...";
                bool ok = await Task.Run(() => _hid.SetKeyImage(swIndex, jpeg));
                if (ok) okCount++;
                else Log.Write($"ShowGridNumbers: failed to send test number for software slot {swIndex}");
            }

            // Side screen: no software slot exists for these, address them raw.
            foreach (int rawIdx in SideScreenRawIndices)
            {
                byte[] jpeg = RenderLabelToJpeg(rawIdx.ToString());
                StatusMessage = $"Sending side screen index {rawIdx}...";
                bool ok = await Task.Run(() => _hid.SetKeyImageRaw(rawIdx, jpeg));
                if (ok) okCount++;
                else Log.Write($"ShowGridNumbers: failed to send test number for side-screen raw index {rawIdx}");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ShowGridNumbers: EXCEPTION after {okCount}/{total}: {ex}");
            StatusMessage = $"Crashed: {ex.Message}";
            return;
        }
        finally
        {
            _hid.ResumeReading();
        }
        Log.Write($"ShowGridNumbers: done, {okCount}/{total} succeeded.");
        StatusMessage = "Done. Check the 15 keys (1-15, top-left to bottom-right) and the side screen (16-18).";
    }

    [RelayCommand]
    private void ForceReconnect()
    {
        try
        {
            Log.Write("ForceReconnect (VM): click.");
            StatusMessage = "Reconnecting...";
            bool ok = _hid.ForceReconnect();
            StatusMessage = ok ? $"Reconnected — {_hid.DeviceInfo}" : "Reconnect failed.";
        }
        catch (Exception ex)
        {
            Log.Write($"ForceReconnect (VM): EXCEPTION: {ex}");
            StatusMessage = $"Reconnect crashed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ProbeKeyRange()
    {
        if (!_hid.IsConnected) { Log.Write("ProbeKeyRange: aborted, not connected."); StatusMessage = "Connect first!"; return; }

        const int min = -50, max = 50;
        Log.Write($"ProbeKeyRange: starting, range [{min},{max}].");
        int okCount = 0;
        _hid.SuspendReading();
        try
        {
            for (int devIdx = min; devIdx <= max; devIdx++)
            {
                byte[] jpeg = RenderLabelToJpeg(devIdx.ToString());
                StatusMessage = $"Probing raw index {devIdx} ({devIdx - min + 1}/{max - min + 1})...";
                bool ok = await Task.Run(() => _hid.SetKeyImageRaw(devIdx, jpeg, maxAttempts: 3));
                if (ok) okCount++;
                else Log.Write($"ProbeKeyRange: index {devIdx} failed/timed out after 3 attempts.");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ProbeKeyRange: EXCEPTION after {okCount} succeeded: {ex}");
            StatusMessage = $"Crashed: {ex.Message}";
            return;
        }
        finally
        {
            _hid.ResumeReading();
        }
        Log.Write($"ProbeKeyRange: done, {okCount}/{max - min + 1} succeeded.");
        StatusMessage = $"Probe done ({min} to {max}). Photograph everything lit up and report which number is where.";
    }

    private bool _isCalibrating;
    private int  _calibrationStep;
    private int[]? _calibrationMap;

    [RelayCommand]
    private async Task CalibrateGrid()
    {
        if (!_hid.IsConnected) { Log.Write("CalibrateGrid: aborted, not connected."); StatusMessage = "Connect first!"; return; }

        try
        {
            Log.Write("CalibrateGrid: starting interactive calibration.");
            _isCalibrating   = true;
            _calibrationStep = 0;
            _calibrationMap  = new int[HidService.KeyCount];
            // Not reading physical button presses during calibration anyway (you click
            // the on-screen tiles instead) — pausing polling avoids USB contention with
            // the per-key writes below.
            _hid.SuspendReading();
            await SendNextCalibrationNumber();
        }
        catch (Exception ex)
        {
            Log.Write($"CalibrateGrid: EXCEPTION: {ex}");
            StatusMessage = $"Calibration crashed: {ex.Message}";
            _isCalibrating = false;
            _hid.ResumeReading();
        }
    }

    private async Task SendNextCalibrationNumber()
    {
        try
        {
            // Panel is 1-indexed (1-15), not 0-14 — offset _calibrationStep (0-14) by
            // one so this sweeps the real valid range instead of hitting an invalid
            // index 0 and never testing the real index 15.
            int devIndex = _calibrationStep + 1;
            byte[] jpeg = RenderLabelToJpeg(devIndex.ToString());
            Log.Write($"Calibrating device index {devIndex}...");

            bool ok = await Task.Run(() => _hid.SetKeyImageRaw(devIndex, jpeg));
            if (!ok) Log.Write($"Failed to send calibration image for device index {devIndex}");

            StatusMessage = $"Calibration: click the pad key showing '{devIndex}' ({_calibrationStep + 1}/{HidService.KeyCount}).";
        }
        catch (Exception ex)
        {
            Log.Write($"SendNextCalibrationNumber: EXCEPTION at step {_calibrationStep}: {ex}");
            StatusMessage = $"Calibration crashed: {ex.Message}";
            _isCalibrating = false;
            _hid.ResumeReading();
        }
    }

    private async Task HandleCalibrationClick(KeyInfo key)
    {
        if (_calibrationMap == null) return;

        try
        {
            int devIndex = _calibrationStep + 1;
            Log.Write($"HandleCalibrationClick: software slot {key.Index} <- raw device index {devIndex}.");
            _calibrationMap[key.Index] = devIndex;
            _calibrationStep++;

            if (_calibrationStep >= HidService.KeyCount)
            {
                _isCalibrating = false;
                _hid.ResumeReading();
                Log.Write($"HandleCalibrationClick: all {HidService.KeyCount} keys clicked, map=[{string.Join(",", _calibrationMap)}]. Saving.");
                try
                {
                    _hid.SetKeyMap(_calibrationMap);
                    StatusMessage = "Calibration complete and saved.";
                }
                catch (Exception ex)
                {
                    Log.Write($"HandleCalibrationClick: SetKeyMap failed: {ex.Message}");
                    StatusMessage = $"Calibration failed (a key was clicked twice?): {ex.Message}. Please retry.";
                }
                finally
                {
                    _calibrationMap = null;
                }
                return;
            }

            await SendNextCalibrationNumber();
        }
        catch (Exception ex)
        {
            Log.Write($"HandleCalibrationClick: EXCEPTION: {ex}");
            StatusMessage = $"Calibration crashed: {ex.Message}";
            _isCalibrating = false;
            _calibrationMap = null;
            _hid.ResumeReading();
        }
    }

    public void SaveToProfile(KeyboardProfile profile)
    {
        Log.Write($"SaveToProfile: '{profile.Name}', {Keys.Count} key(s).");
        profile.KeyBindings.Clear();
        profile.Actions.Clear();
        foreach (var key in Keys)
        {
            string idx = key.Index.ToString();
            profile.KeyBindings[idx] = key.Label + "|" + (key.ImagePath ?? "");
            profile.Actions[idx] = key.Action;
        }
    }

    public void LoadProfile(KeyboardProfile profile)
    {
        Log.Write($"LoadProfile: '{profile.Name}'.");
        foreach (var key in Keys)
        {
            string idx = key.Index.ToString();
            if (profile.KeyBindings.TryGetValue(idx, out var binding))
            {
                var parts = binding.Split('|');
                key.Label = parts[0];
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    if (File.Exists(parts[1])) key.ImagePath = parts[1];
                    else Log.Write($"LoadProfile: key {idx} image '{parts[1]}' not found on disk, skipped.");
                }
            }
            if (profile.Actions.TryGetValue(idx, out var action))
            {
                key.Action = action;
            }
        }
    }

    [RelayCommand]
    private void TryConnect()
    {
        try
        {
            Log.Write($"TryConnect (VM): click, hid.IsConnected={_hid.IsConnected}");
            if (_hid.IsConnected)
            {
                StatusMessage = $"Already connected — {_hid.DeviceInfo}";
                IsConnected = true;
                return;
            }

            bool ok = _hid.TryConnect();
            StatusMessage = ok ? $"Connected — {_hid.DeviceInfo}" : "Not found.";
            IsConnected = ok;
        }
        catch (Exception ex)
        {
            Log.Write($"TryConnect (VM): EXCEPTION: {ex}");
            StatusMessage = $"Connect crashed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ScanDevices()
    {
        try
        {
            var devices = HidService.GetAllDevices();
            Log.Write($"ScanDevices: {devices.Count} entrie(s).");
            MessageBox.Show(string.Join("\n", devices), "HID Devices");
        }
        catch (Exception ex)
        {
            Log.Write($"ScanDevices: EXCEPTION: {ex}");
        }
    }

    [RelayCommand]
    private void TestBrightness()
    {
        try
        {
            Log.Write("TestBrightness: click.");
            _hid.SetBrightness(50);
        }
        catch (Exception ex)
        {
            Log.Write($"TestBrightness: EXCEPTION: {ex}");
        }
    }

    private byte[] RenderLabelToJpeg(string text)
    {
        int size = HidService.ImageSize;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, size, size));
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 32, Brushes.White, 96);
            dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return HidService.EncodeJpeg(rtb);
    }
}
