using System.Diagnostics;
using System.IO;

namespace AjazzKeyboard.Services;

/// <summary>
/// Writes debug lines to both the VS Output window (when a debugger is attached)
/// and a plain text file, so logs are readable even when running via Ctrl+F5
/// (no debugger, no console window) — which also avoids debugger-induced timing
/// overhead that can throw off the HID timeouts.
/// </summary>
public static class Log
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AjazzKeyboard", "debug.log");

    private static readonly object _fileLock = new();
    private static bool _initialized;

    public static void Write(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Debug.WriteLine(line);

        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (!_initialized)
                {
                    // Start each run with a clean file so it's easy to find what
                    // just happened instead of scrolling through old sessions.
                    File.WriteAllText(LogPath, $"=== AjazzKeyboard log started {DateTime.Now} ==={Environment.NewLine}");
                    _initialized = true;
                }
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never be the reason the app breaks.
        }
    }
}
