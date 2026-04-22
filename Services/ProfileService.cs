using System.IO;
using System.Text.Json;
using AjazzKeyboard.Models;

namespace AjazzKeyboard.Services;

public class ProfileService
{
    private static readonly string ProfileDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "AjazzKeyboard", "Profiles");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ProfileService() => Directory.CreateDirectory(ProfileDir);

    public IEnumerable<string> ListProfiles() =>
        Directory.EnumerateFiles(ProfileDir, "*.json")
                 .Select(Path.GetFileNameWithoutExtension)
                 .OfType<string>();

    public KeyboardProfile? Load(string name)
    {
        var path = ProfilePath(name);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<KeyboardProfile>(File.ReadAllText(path));
    }

    public void Save(KeyboardProfile profile)
    {
        profile.ModifiedAt = DateTime.Now;
        File.WriteAllText(ProfilePath(profile.Name),
                          JsonSerializer.Serialize(profile, JsonOpts));
    }

    public void Delete(string name)
    {
        var path = ProfilePath(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public void Export(KeyboardProfile profile, string filePath)
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(profile, JsonOpts));
    }

    public KeyboardProfile? Import(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        return JsonSerializer.Deserialize<KeyboardProfile>(File.ReadAllText(filePath));
    }

    private static string ProfilePath(string name) =>
        Path.Combine(ProfileDir, $"{SanitizeName(name)}.json");

    private static string SanitizeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
