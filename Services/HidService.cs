using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AjazzKeyboard.Models;
using HidLibrary;
using System.Diagnostics;

namespace AjazzKeyboard.Services;

public sealed class HidService : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
    private const uint GENERIC_READ     = 0x80000000;
    private const uint GENERIC_WRITE    = 0x40000000;
    private const uint FILE_SHARE_READ  = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING    = 3;

    private const int VID = 0x0300;
    private static readonly int[] PIDs = [0x1010, 0x3010, 0x3011];
    
    // The device reports 512 bytes. 1 byte is Report ID, 511 are payload.
    private int _reportSize = 512; 

    public const int ImageSize = 85;
    public const int KeyCount  = 15;

    private static readonly string KeyMapPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AjazzKeyboard", "keymap.json");

    private HidDevice? _device;
    private IntPtr     _writeHandle = INVALID_HANDLE_VALUE;
    private CancellationTokenSource? _readCts;
    private byte       _lastRawButtonByte = 0;
    private readonly object _lock = new();
    private readonly object _connectionLock = new();
    private volatile bool _suspendReading;

    // swToDev[softwareIndex] = raw device index the physical key actually responds
    // to; devToSw is the inverse. Verified empirically across several probe/photo
    // sessions on this AKP153E unit: the panel is scanned 1-indexed (1-15, not
    // 0-14) following devIdx = 13 + row - 3*col for the 3x5 grid. Persisted
    // calibration (keymap.json) overrides this if present.
    private static readonly int[] DefaultSwToDev = [13, 10, 7, 4, 1, 14, 11, 8, 5, 2, 15, 12, 9, 6, 3];
    private int[] _swToDev = (int[])DefaultSwToDev.Clone();
    private Dictionary<int, int> _devToSw = BuildInverse(DefaultSwToDev);

    private static Dictionary<int, int> BuildInverse(int[] swToDev)
    {
        var inverse = new Dictionary<int, int>();
        for (int sw = 0; sw < swToDev.Length; sw++)
            inverse[swToDev[sw]] = sw;
        return inverse;
    }

    public bool   IsConnected { get; private set; }
    public string DeviceInfo  { get; private set; } = "Not connected";

    public event EventHandler<bool>?   ConnectionChanged;
    public event EventHandler<int>?    ButtonPressed;

    public HidService()
    {
        // Every timed read/write spins up a Task.Run to enforce a hard timeout (see
        // SendInternal/StartReading). Under sustained load (e.g. probing 100+ indices
        // while the read loop polls concurrently) the default thread pool can be slow
        // to inject new threads, making calls appear to time out when they're really
        // just queued. Force enough threads to be available immediately.
        ThreadPool.SetMinThreads(32, 32);
        LoadKeyMap();
    }

    /// <summary>Overrides the raw-device-index ↔ software-slot mapping and persists it.</summary>
    public void SetKeyMap(int[] swToDev)
    {
        if (swToDev.Length != KeyCount)
            throw new ArgumentException($"Key map must have exactly {KeyCount} entries.", nameof(swToDev));

        // devIdx is just a raw byte address on the wire — this panel turned out to be
        // 1-indexed (1-15), not 0-14, so don't assume an upper bound tied to KeyCount.
        var devToSw = new Dictionary<int, int>();
        for (int sw = 0; sw < KeyCount; sw++)
        {
            int dev = swToDev[sw];
            if (dev < 0 || dev > 255)
                throw new ArgumentOutOfRangeException(nameof(swToDev), $"Device index {dev} is out of range.");
            if (devToSw.ContainsKey(dev))
                throw new ArgumentException($"Device index {dev} is assigned to more than one key.", nameof(swToDev));
            devToSw[dev] = sw;
        }

        _swToDev = (int[])swToDev.Clone();
        _devToSw = devToSw;
        Log.Write($"SetKeyMap: applied [{string.Join(",", _swToDev)}]");
        SaveKeyMap();
    }

    private void LoadKeyMap()
    {
        try
        {
            if (!File.Exists(KeyMapPath))
            {
                Log.Write($"LoadKeyMap: no file at '{KeyMapPath}', using identity mapping.");
                return;
            }
            var loaded = JsonSerializer.Deserialize<int[]>(File.ReadAllText(KeyMapPath));
            if (loaded != null && loaded.Length == KeyCount)
            {
                Log.Write($"LoadKeyMap: loaded [{string.Join(",", loaded)}] from '{KeyMapPath}'.");
                SetKeyMap(loaded);
            }
            else
            {
                Log.Write($"LoadKeyMap: file at '{KeyMapPath}' has wrong length ({loaded?.Length.ToString() ?? "null"}), expected {KeyCount}. Using identity mapping.");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"LoadKeyMap: failed to read/parse '{KeyMapPath}': {ex.Message}. Using identity mapping.");
        }
    }

    private void SaveKeyMap()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyMapPath)!);
            File.WriteAllText(KeyMapPath, JsonSerializer.Serialize(_swToDev));
            Log.Write($"SaveKeyMap: saved to '{KeyMapPath}'.");
        }
        catch (Exception ex)
        {
            Log.Write($"SaveKeyMap: failed to write '{KeyMapPath}': {ex.Message}. Mapping won't survive a restart.");
        }
    }

    public static List<string> GetAllDevices()
    {
        var results = new List<string>();
        foreach (var d in HidDevices.Enumerate())
            results.Add($"VID:0x{d.Attributes.VendorId:X4} PID:0x{d.Attributes.ProductId:X4} {d.Description}");
        Log.Write($"GetAllDevices: found {results.Count} HID device(s).");
        return results.Count > 0 ? results : ["No HID devices found."];
    }

    public bool TryConnect()
    {
        // Reconnects can now be triggered from several places at once (manual Connect
        // click, ForceReconnect, and the auto-reconnect that fires after a detected
        // disconnect). Without this lock, two overlapping calls can interleave their
        // field writes and leave IsConnected=true with an INVALID writeHandle.
        lock (_connectionLock)
        {
            try
            {
                Log.Write("TryConnect: starting scan...");
                foreach (int pid in PIDs)
                {
                    var devices = HidDevices.Enumerate(VID, pid).ToList();
                    Log.Write($"TryConnect: pid=0x{pid:X4} -> {devices.Count} device(s) enumerated.");
                    foreach (var device in devices)
                    {
                        device.OpenDevice();
                        if (!device.IsConnected)
                        {
                            Log.Write($"TryConnect: OpenDevice failed/not connected for '{device.DevicePath}' (already open elsewhere?).");
                            continue;
                        }

                        if (device.Capabilities.OutputReportByteLength < 64)
                        {
                            Log.Write($"TryConnect: '{device.DevicePath}' rejected, OutputReportByteLength={device.Capabilities.OutputReportByteLength} < 64.");
                            device.CloseDevice();
                            continue;
                        }

                        _readCts?.Cancel();
                        if (_writeHandle != INVALID_HANDLE_VALUE) CloseHandle(_writeHandle);

                        _writeHandle = CreateFile(device.DevicePath, GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                        if (_writeHandle == INVALID_HANDLE_VALUE)
                        {
                            Log.Write($"TryConnect: CreateFile failed for '{device.DevicePath}', Win32 error={Marshal.GetLastWin32Error()}");
                            device.CloseDevice();
                            continue;
                        }

                        _device = device;
                        _reportSize = _device.Capabilities.OutputReportByteLength; // Should be 512
                        IsConnected = true;
                        DeviceInfo = $"KUIYN AKP153 — VID:{VID:X4} PID:{pid:X4}";
                        Log.Write($"TryConnect: connected. path='{device.DevicePath}' writeHandle=0x{_writeHandle:X} reportSize={_reportSize} pid=0x{pid:X4}");

                        ConnectionChanged?.Invoke(this, true);
                        // Was lowered to 50 over a suspected USB brownout at full brightness,
                        // but sustained writes have since proven stable with read polling
                        // suspended during sends — and 50% may simply be too dim to see on
                        // this panel. Back to a clearly-visible level; revisit if disconnects
                        // reappear.
                        SetBrightness(90);
                        StartReading();
                        return true;
                    }
                }
                Log.Write("TryConnect: no usable device found.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Write($"TryConnect: EXCEPTION: {ex}");
                IsConnected = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Pauses the button-read polling loop. Reads and writes appear to contend for
    /// the same USB bandwidth on this device — bulk image sends are dramatically
    /// faster (and more reliable) with polling paused, since button presses can't
    /// meaningfully happen during an automated send anyway.
    /// </summary>
    public void SuspendReading()
    {
        _suspendReading = true;
        Log.Write("SuspendReading: paused.");
    }

    public void ResumeReading()
    {
        _suspendReading = false;
        Log.Write("ResumeReading: resumed.");
    }

    public void Disconnect()
    {
        lock (_connectionLock)
        {
            try
            {
                Log.Write("Disconnect: called explicitly.");
                _readCts?.Cancel();
                if (_writeHandle != INVALID_HANDLE_VALUE) { CloseHandle(_writeHandle); _writeHandle = INVALID_HANDLE_VALUE; }
                CloseDeviceBounded();
            }
            catch (Exception ex)
            {
                Log.Write($"Disconnect: EXCEPTION: {ex}");
                // Force the sentinel value even if something above threw partway
                // through, so we never end up with IsConnected=true and a stale handle.
                _writeHandle = INVALID_HANDLE_VALUE;
            }
            finally
            {
                IsConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
        }
    }

    /// <summary>
    /// HidDevice.CloseDevice() can block waiting on an in-flight Read() from the
    /// polling loop — and that Read() may itself be one of the "abandoned" ones our
    /// external timeout gave up waiting on (the native call can keep running
    /// regardless). Bound the close so a stuck read can't turn Disconnect() into a
    /// permanent hang.
    /// </summary>
    private void CloseDeviceBounded()
    {
        var device = _device;
        _device = null;
        if (device == null) return;

        try
        {
            var closeTask = Task.Run(() => device.CloseDevice());
            if (!closeTask.Wait(1000))
                Log.Write("CloseDeviceBounded: CloseDevice() did not return within 1000ms, abandoning it (may leak a handle until the process exits).");
        }
        catch (Exception ex)
        {
            Log.Write($"CloseDeviceBounded: EXCEPTION: {ex}");
        }
    }

    /// <summary>
    /// Explicit disconnect+reconnect cycle (no cable unplug needed). Test hypothesis:
    /// keys that wrote OK but never actually rendered might get painted to the LCD
    /// when the connection re-establishes, even though the write itself already
    /// succeeded earlier.
    /// </summary>
    public bool ForceReconnect()
    {
        lock (_connectionLock)
        {
            try
            {
                Log.Write("ForceReconnect: disconnecting then reconnecting...");
                Disconnect();
                Thread.Sleep(300);
                return TryConnect();
            }
            catch (Exception ex)
            {
                Log.Write($"ForceReconnect: EXCEPTION: {ex}");
                return false;
            }
        }
    }

    public bool SetKeyImage(int keyIndex, byte[] jpegData)
    {
        return SetKeyImageRaw(ToDeviceKeyIndex(keyIndex), jpegData);
    }

    public bool SetKeyImageRaw(int devIdx, byte[] jpegData, int maxAttempts = 3)
    {
        var sw = Stopwatch.StartNew();
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (TrySendKeyImageOnce(devIdx, jpegData))
            {
                Log.Write($"SetKeyImageRaw: OK for device index {devIdx} in {sw.ElapsedMilliseconds}ms (attempt {attempt}/{maxAttempts}).");
                return true;
            }
            Log.Write($"SetKeyImageRaw: attempt {attempt}/{maxAttempts} failed for device index {devIdx}");
            if (attempt < maxAttempts) Thread.Sleep(200);
        }
        Log.Write($"SetKeyImageRaw: GAVE UP for device index {devIdx} after {sw.ElapsedMilliseconds}ms.");
        return false;
    }

    private bool TrySendKeyImageOnce(int devIdx, byte[] jpegData)
    {
        if (!IsConnected || _writeHandle == INVALID_HANDLE_VALUE)
        {
            Log.Write($"TrySendKeyImageOnce({devIdx}): aborted before send — IsConnected={IsConnected}, writeHandle={(_writeHandle == INVALID_HANDLE_VALUE ? "INVALID" : $"0x{_writeHandle:X}")}");
            return false;
        }

        lock (_lock)
        {
            // 1. BAT Initiation - 511 bytes max payload
            byte[] init = Crt("BAT", 0x00, 0x00, 0x55, 0x55, (byte)devIdx);
            if (!SendInternal(init))
            {
                Log.Write($"TrySendKeyImageOnce({devIdx}): BAT init failed.");
                return false;
            }
            Thread.Sleep(100);

            // 2. DATA Transfer - Chunks must be exactly 511 bytes
            int payloadSize = _reportSize - 1;
            int offset = 0;
            int chunkNum = 0;
            while (offset < jpegData.Length)
            {
                byte[] chunk = new byte[payloadSize];
                int length = Math.Min(payloadSize, jpegData.Length - offset);
                Array.Copy(jpegData, offset, chunk, 0, length);

                if (!SendInternal(chunk))
                {
                    Log.Write($"TrySendKeyImageOnce({devIdx}): chunk {chunkNum} (offset {offset}/{jpegData.Length}) failed.");
                    return false;
                }

                offset += length;
                chunkNum++;
                Thread.Sleep(10);
            }

            // 3. STP Termination
            Thread.Sleep(50);
            bool ok = SendInternal(Crt("STP"));
            if (!ok) Log.Write($"TrySendKeyImageOnce({devIdx}): STP termination failed.");
            // The USB write can succeed (ACKed) before the panel has actually committed
            // the frame to the LCD. Measured from real logs: keys that do stick are
            // spaced ~17s apart, suggesting a BAT for a different key within that
            // window interrupts the previous key's in-progress commit. Give it a full
            // ~17s of quiet before letting the next key's BAT go out.
            Thread.Sleep(17000);
            return ok;
        }
    }

    public bool ClearKey(int keyIndex)
    {
        if (!IsConnected)
        {
            Log.Write($"ClearKey({keyIndex}): aborted, not connected.");
            return false;
        }
        int devIdx = ToDeviceKeyIndex(keyIndex);
        Log.Write($"ClearKey({keyIndex}): sending CLE for device index {devIdx}.");
        bool ok;
        lock (_lock) { ok = SendInternal(Crt("CLE", 0x00, 0x00, (byte)devIdx)); }
        Log.Write($"ClearKey({keyIndex}): {(ok ? "OK" : "FAILED")}.");
        return ok;
    }

    public bool SetBrightness(byte percent)
    {
        if (!IsConnected)
        {
            Log.Write($"SetBrightness({percent}): aborted, not connected.");
            return false;
        }
        bool ok;
        lock (_lock) { ok = SendInternal(Crt("LIG", 0x00, 0x00, percent)); }
        Log.Write($"SetBrightness({percent}): {(ok ? "OK" : "FAILED")}.");
        return ok;
    }

    private void StartReading()
    {
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        Log.Write("StartReading: read loop starting.");
        Task.Run(() =>
        {
          try
          {
            while (!token.IsCancellationRequested && (_device?.IsConnected ?? false))
            {
                if (_suspendReading)
                {
                    Thread.Sleep(50);
                    continue;
                }

                // NOTE: deliberately NOT holding _lock here. HidDevice.Read(50) does not
                // reliably honor its own timeout — if it hangs, holding _lock while
                // blocked on it would starve every write forever (a real regression we
                // hit). Bound it externally instead so a stuck read can never freeze
                // the app; a slow/stuck read task is simply abandoned.
                var device = _device;
                if (device == null) continue;

                HidDeviceData? report = null;
                try
                {
                    var readTask = Task.Run(() => device.Read(50));
                    if (readTask.Wait(800))
                    {
                        report = readTask.Result;
                    }
                    else
                    {
                        Log.Write("StartReading: Read(50) exceeded 800ms, abandoning this poll.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // e.g. ObjectDisposedException if the device got closed while this
                    // read was in flight. Log and just skip this poll instead of taking
                    // the whole read loop down.
                    Log.Write($"StartReading: Read(50) threw: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (report == null || report.Data == null || report.Data.Length < 11) continue;
                if (report.Data[1] == 0x41 && report.Data[2] == 0x43) continue;

                byte keyVal = 0;
                if (report.Data[1] != 0) keyVal = report.Data[1];
                else if (report.Data[10] != 0) keyVal = report.Data[10];

                if (keyVal != _lastRawButtonByte)
                {
                    Log.Write($"StartReading: raw button byte changed {_lastRawButtonByte} -> {keyVal}.");
                    if (keyVal > 0)
                    {
                        // The panel's image-write addressing turned out to be 1-indexed
                        // (1-15). Assuming button-press reports use the same raw key
                        // numbering as the writes (same firmware, same physical matrix) —
                        // unverified by direct testing yet, watch the log when pressing
                        // a physical key to confirm.
                        int swIdx = FromDeviceKeyIndex(keyVal);
                        if (swIdx >= 0)
                        {
                            Log.Write($"StartReading: raw devIdx={keyVal} -> software slot {swIdx}. Firing ButtonPressed.");
                            ButtonPressed?.Invoke(this, swIdx);
                        }
                        else
                        {
                            Log.Write($"StartReading: raw devIdx={keyVal} has no mapped software slot — ignored.");
                        }
                    }
                    _lastRawButtonByte = keyVal;
                }
            }

            Log.Write($"StartReading: read loop exiting. cancelled={token.IsCancellationRequested}, deviceConnected={_device?.IsConnected ?? false}.");
            if (!token.IsCancellationRequested)
            {
                // Loop ended because the device dropped off (unplugged/errored/USB
                // brownout), not because we cancelled it for a deliberate reconnect.
                Log.Write("StartReading: device appears to have disconnected. Marking IsConnected=false.");
                IsConnected = false;
                ConnectionChanged?.Invoke(this, false);

                // The dropout can be a transient USB brownout (device re-enumerates a
                // moment later). Try once to reconnect automatically instead of
                // leaving the user stuck until they notice and click Connect again.
                Task.Run(() =>
                {
                    Thread.Sleep(800);
                    Log.Write("StartReading: attempting auto-reconnect after unexpected disconnect...");
                    TryConnect();
                });
            }
          }
          catch (Exception ex)
          {
              Log.Write($"StartReading: read loop CRASHED: {ex}");
              IsConnected = false;
              ConnectionChanged?.Invoke(this, false);
          }
        }, token);
    }

    private int ToDeviceKeyIndex(int swIndex) =>
        swIndex >= 0 && swIndex < KeyCount ? _swToDev[swIndex] : swIndex;

    private int FromDeviceKeyIndex(int devIndex) =>
        _devToSw.TryGetValue(devIndex, out var sw) ? sw : -1;

    private byte[] Crt(string cmd, params byte[] parameters)
    {
        int payloadSize = _reportSize - 1;
        byte[] payload = new byte[payloadSize];
        payload[0] = 0x43; payload[1] = 0x52; payload[2] = 0x54;
        payload[5] = (byte)cmd[0]; payload[6] = (byte)cmd[1]; payload[7] = (byte)cmd[2];
        for (int i = 0; i < parameters.Length && 8 + i < payloadSize; i++)
            payload[8 + i] = parameters[i];
        return payload;
    }

    private bool SendInternal(byte[] data)
    {
        if (_writeHandle == INVALID_HANDLE_VALUE)
        {
            Log.Write("SendInternal: aborted — writeHandle is INVALID.");
            return false;
        }

        // CRITICAL: The total buffer MUST match the device report size (512)
        byte[] buffer = new byte[_reportSize];
        buffer[0] = 0x00; // Report ID
        // Copy only up to _reportSize - 1 bytes
        Array.Copy(data, 0, buffer, 1, Math.Min(data.Length, _reportSize - 1));

        // WriteFile is a blocking synchronous call with no built-in timeout: if the
        // device stops responding mid-transfer it can hang forever. Bound it so a
        // single unresponsive write can't freeze the whole app.
        IntPtr handle = _writeHandle;
        int win32Error = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            var writeTask = Task.Run(() =>
            {
                bool result = WriteFile(handle, buffer, (uint)buffer.Length, out _, IntPtr.Zero);
                if (!result) win32Error = Marshal.GetLastWin32Error();
                return result;
            });

            if (!writeTask.Wait(4000))
            {
                Log.Write($"SendInternal: WriteFile TIMED OUT after {sw.ElapsedMilliseconds}ms (handle=0x{handle:X}).");
                return false;
            }

            bool ok = writeTask.Result;
            if (!ok)
                Log.Write($"SendInternal: WriteFile FAILED after {sw.ElapsedMilliseconds}ms — Win32 error={win32Error} (0x{win32Error:X}), handle=0x{handle:X}.");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Write($"SendInternal: EXCEPTION after {sw.ElapsedMilliseconds}ms (handle=0x{handle:X}): {ex}");
            return false;
        }
    }

    public static byte[] EncodeJpeg(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.PushTransform(new RotateTransform(-90, ImageSize / 2.0, ImageSize / 2.0));
            ctx.DrawImage(source, new Rect(0, 0, ImageSize, ImageSize));
        }
        var rtb = new RenderTargetBitmap(ImageSize, ImageSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        using var ms = new MemoryStream();
        var enc = new JpegBitmapEncoder { QualityLevel = 65 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        enc.Save(ms);
        return ms.ToArray();
    }

    public bool SetRgbMode(RgbSettings rgb) => false;

    public void Dispose()
    {
        Log.Write("Dispose: shutting down HidService.");
        _readCts?.Cancel();
        if (_writeHandle != INVALID_HANDLE_VALUE) CloseHandle(_writeHandle);
        CloseDeviceBounded();
    }
}
