using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AjazzKeyboard.Models;
using AjazzKeyboard.Services;
using Microsoft.Win32;

namespace AjazzKeyboard.ViewModels;

public partial class ProfilesPageViewModel : ObservableObject
{
    private readonly ProfileService _profileService;
    private readonly KeyboardPageViewModel _keyboardVm;
    private readonly RgbPageViewModel _rgbVm;
    private readonly HidService _hid;

    [ObservableProperty] private ObservableCollection<string> _profileNames = [];
    [ObservableProperty] private string? _selectedProfileName;
    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private string _statusMessage = "";

    public ProfilesPageViewModel(ProfileService profileService,
                                 KeyboardPageViewModel keyboardVm,
                                 RgbPageViewModel rgbVm,
                                 HidService hid)
    {
        _profileService = profileService;
        _keyboardVm     = keyboardVm;
        _rgbVm          = rgbVm;
        _hid            = hid;
        Refresh();
    }

    private void Refresh()
    {
        ProfileNames.Clear();
        foreach (var name in _profileService.ListProfiles().OrderBy(x => x))
            ProfileNames.Add(name);
    }

    [RelayCommand]
    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrEmpty(name)) { StatusMessage = "Enter a profile name."; return; }

        var profile = new KeyboardProfile { Name = name };
        _keyboardVm.SaveToProfile(profile);
        profile.Rgb = _rgbVm.BuildSettings();

        _profileService.Save(profile);
        Refresh();
        SelectedProfileName = name;
        StatusMessage = $"Profile '{name}' saved.";
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (SelectedProfileName == null) return;

        var profile = _profileService.Load(SelectedProfileName);
        if (profile == null) { StatusMessage = "Profile not found."; return; }

        _keyboardVm.LoadProfile(profile);
        _rgbVm.LoadFromSettings(profile.Rgb);
        _hid.SetRgbMode(profile.Rgb);

        StatusMessage = $"Profile '{profile.Name}' loaded.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfileName == null) return;
        _profileService.Delete(SelectedProfileName);
        StatusMessage = $"Profile '{SelectedProfileName}' deleted.";
        SelectedProfileName = null;
        Refresh();
    }

    [RelayCommand]
    private void ExportProfile()
    {
        if (SelectedProfileName == null) return;

        var dlg = new SaveFileDialog
        {
            FileName = SelectedProfileName,
            DefaultExt = ".json",
            Filter = "JSON files (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true) return;

        var profile = _profileService.Load(SelectedProfileName);
        if (profile != null) _profileService.Export(profile, dlg.FileName);
        StatusMessage = "Profile exported.";
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var dlg = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        var profile = _profileService.Import(dlg.FileName);
        if (profile == null) { StatusMessage = "Invalid profile file."; return; }

        _profileService.Save(profile);
        Refresh();
        SelectedProfileName = profile.Name;
        StatusMessage = $"Profile '{profile.Name}' imported.";
    }
}
