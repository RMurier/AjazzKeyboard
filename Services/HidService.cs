using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AjazzKeyboard.Models;
using HidLibrary;

namespace AjazzKeyboard.Services;

/// <summary>
/// Communicates with the AJAZZ AKP153 macro pad over HID.
///
/// Protocol (community reverse-engineered — Uriziel01/ajazz-sdk/mirajazz):
///   VID 0x0300, PID 0x1010 / 0x3010 / 0x3011
///   512-byte HID output reports (+1 byte report ID)
///
/// Image upload sequence for key K:
///   1. BAT init:  CRT 0x00 0x00 "BAT" 0x00 0x00 0x19 0x3F K  [zeros to 512]
///   2. Raw JPEG:  512 bytes of JPEG data per packet, last one zero-padded
///   3. STP stop:  CRT 0x00 0x00 "STP"  [zeros to 512]
///
/// Other commands (same 5-byte CRT prefix, then 3-char command):
///   Brightness:  CRT 0x00 0x00 "LIG" 0x00 0x00 percent
///   Clear key:   CRT 0x00 0x00 "CLE" 0x00 0x00 keyIdx
/// </summary>
public sealed class HidService : IDisposable
{
    private const int VID         = 0x0300;
    private static readonly int[] PIDs = [0x1010, 0x3010, 0x3011];
    private const int REPORT_SIZE = 512;

    public const int ImageSize = 85;

    private HidDevice? _device;
    private CancellationTokenSource? _readCts;

    public bool   IsConnected { get; private set; }
    public string DeviceInfo  { get; private set; } = "Not connected";

    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<int>?  ButtonPressed;

    // ── Connection ──────────────────────────────────────────────────────────

    public bool TryConnect()
    {
        foreach (int pid in PIDs)
        {
            foreach (var device in HidDevices.Enumerate(VID, pid))
            {
                if (!device.IsConnected) continue;

                _readCts?.Cancel();
                _device?.CloseDevice();
                _device?.Dispose();

                _device = device;
                _device.OpenDevice();
                                IsConnected = true;

                DeviceInfo = $"AJAZZ AKP153 — VID:{VID:X4} PID:{pid:X4} " +
                             $"(report={_device.Capabilities.OutputReportByteLength}B)";
                ConnectionChanged?.Invoke(this, true);

                // Light up the pad at full brightness on connect
                SetBrightness(100);
                StartReading();
                return true;
            }
        }

        IsConnected = false;
        DeviceInfo  = "Not connected";
        ConnectionChanged?.Invoke(this, false);
        return false;
    }

    public void Disconnect()
    {
        _readCts?.Cancel();
        _device?.CloseDevice();
        IsConnected = false;
        DeviceInfo  = "Not connected";
        ConnectionChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Returns all connected HID devices as formatted strings for diagnostics.
    /// </summary>
    public static List<string> GetAllDevices()
    {
        var results = new List<string>();
        foreach (var g in HidDevices.Enumerate()
            .GroupBy(d => new { d.Attributes.VendorId, d.Attributes.ProductId }))
        {
            try
            {
                var d = g.First();
                d.OpenDevice();
                string line = $"VID:0x{g.Key.VendorId:X4}  PID:0x{g.Key.ProductId:X4}" +
                              $"  interfaces:{g.Count()}" +
                              $"  outReport:{d.Capabilities.OutputReportByteLength}B" +
                              $"  usagePage:0x{d.Capabilities.UsagePage:X4}";
                d.CloseDevice();
                results.Add(line);
            }
            catch { /* skip inaccessible devices */ }
        }
        return results.Count > 0 ? results : ["No HID devices found."];
    }

    // ── LCD images ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a JPEG image to the LCD of key index 0–14.
    /// </summary>
    public bool SetKeyImage(int keyIndex, byte[] jpegData)
    {
        if (!IsConnected) return false;

        // 1. BAT — announce image upload for this key
        //    Layout: CRT 0x00 0x00  BAT  0x00 0x00  0x19 0x3F  keyIdx
        byte[] init = Crt("BAT", 0x00, 0x00, 0x19, 0x3F, (byte)keyIndex);
        if (!Send(init)) return false;

        // 2. Raw JPEG data — 512 bytes per packet, zero-padded on the last one
        int offset = 0;
        while (offset < jpegData.Length)
        {
            byte[] chunk  = new byte[REPORT_SIZE];
            int    length = Math.Min(REPORT_SIZE, jpegData.Length - offset);
            Array.Copy(jpegData, offset, chunk, 0, length);
            if (!Send(chunk)) return false;
            offset += length;
        }

        // 3. STP — signal end of image data
        byte[] stop = Crt("STP");
        return Send(stop);
    }

    /// <summary>Clears a key's LCD to black. Pass -1 to clear all keys (keyIdx 0xFF).</summary>
    public bool ClearKey(int keyIndex)
    {
        if (!IsConnected) return false;
        byte idx = keyIndex < 0 ? (byte)0xFF : (byte)keyIndex;
        return Send(Crt("CLE", 0x00, 0x00, idx));
    }

    /// <summary>Sets display brightness (0–100).</summary>
    public bool SetBrightness(byte percent)
    {
        if (_device == null || !_device.IsConnected) return false;
        return Send(Crt("LIG", 0x00, 0x00, percent));
    }

    /// <summary>Scales a WPF BitmapSource to 85×85 and encodes it as JPEG bytes.</summary>
    public static byte[] EncodeJpeg(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
            ctx.DrawImage(source, new System.Windows.Rect(0, 0, ImageSize, ImageSize));

        var rtb = new RenderTargetBitmap(ImageSize, ImageSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        using var ms = new MemoryStream();
        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        enc.Save(ms);
        return ms.ToArray();
    }

    // ── RGB stubs (API compat — AKP153 uses LCD, not RGB LEDs) ──────────────

    public bool SetRgbMode(RgbSettings rgb) => false;
    public bool SetKeyColor(byte hidCode, byte r, byte g, byte b) => false;

    // ── Button reading ───────────────────────────────────────────────────────

    private void StartReading()
    {
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && (_device?.IsConnected ?? false))
            {
                try
                {
                    var report = await _device.ReadAsync();
                    if (report?.Data == null) continue;

                    // Key press events: button index at byte 9 (1-indexed, 0 = released)
                    if (report.Data.Length > 9 && report.Data[9] != 0)
                        ButtonPressed?.Invoke(this, report.Data[9] - 1);
                }
                catch (OperationCanceledException) { break; }
                catch { /* device disconnected */ }
            }
        }, token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a 512-byte CRT command packet.
    /// Format: 0x43 0x52 0x54  0x00 0x00  cmd[0..2]  params[0..]  zeros
    /// </summary>
    private static byte[] Crt(string cmd, params byte[] parameters)
    {
        byte[] payload = new byte[REPORT_SIZE];
        payload[0] = 0x43; // C
        payload[1] = 0x52; // R
        payload[2] = 0x54; // T
        payload[3] = 0x00;
        payload[4] = 0x00;
        if (cmd.Length > 0) payload[5] = (byte)cmd[0];
        if (cmd.Length > 1) payload[6] = (byte)cmd[1];
        if (cmd.Length > 2) payload[7] = (byte)cmd[2];
        for (int i = 0; i < parameters.Length && 8 + i < REPORT_SIZE; i++)
            payload[8 + i] = parameters[i];
        return payload;
    }

    private bool Send(byte[] data)
    {
        if (_device == null || !_device.IsConnected) return false;

        byte[] report = new byte[REPORT_SIZE + 1];
        report[0] = 0x00; // Report ID = 0
        data.CopyTo(report, 1);

        return _device.Write(report);
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        _device?.CloseDevice();
        _device?.Dispose();
    }
}
