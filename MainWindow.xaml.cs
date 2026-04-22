using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AjazzKeyboard.Services;
using AjazzKeyboard.ViewModels;
using AjazzKeyboard.Views;

namespace AjazzKeyboard;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly HidService _hid;
    private readonly KeyboardPage _keyboardPage;
    private readonly RgbPage _rgbPage;
    private readonly ProfilesPage _profilesPage;

    private Button? _activeNavButton;

    public MainWindow()
    {
        InitializeComponent();

        var profileService = new ProfileService();
        _hid = new HidService();

        var keyboardVm  = new KeyboardPageViewModel(_hid);
        var rgbVm       = new RgbPageViewModel(_hid);
        var profilesVm  = new ProfilesPageViewModel(profileService, keyboardVm, rgbVm, _hid);

        _keyboardPage = new KeyboardPage(keyboardVm);
        _rgbPage      = new RgbPage(rgbVm);
        _profilesPage = new ProfilesPage(profilesVm);

        _hid.ConnectionChanged += (_, connected) =>
            Dispatcher.Invoke(() =>
            {
                ConnDot.Fill   = new SolidColorBrush(connected
                    ? Color.FromRgb(0x4C, 0xAF, 0x50)
                    : Color.FromRgb(0xF4, 0x43, 0x36));
                ConnLabel.Text = connected ? "Connected" : "Disconnected";
            });

        Loaded += (_, _) =>
        {
            Navigate(_keyboardPage, BtnKeyboard);
            _hid.TryConnect();
            TryLoadIcon();
        };
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var page = (btn.Tag as string) switch
        {
            "Keyboard" => (Page)_keyboardPage,
            "RGB"      => _rgbPage,
            "Profiles" => _profilesPage,
            _          => _keyboardPage,
        };

        Navigate(page, btn);
    }

    private void Navigate(Page page, Button navBtn)
    {
        ContentFrame.Navigate(page);

        // Swap active style
        if (_activeNavButton != null)
            _activeNavButton.Style = (Style)Resources["NavButtonStyle"];

        navBtn.Style = (Style)Resources["NavButtonActiveStyle"];
        _activeNavButton = navBtn;
    }

    private void TryLoadIcon()
    {
        // Looks for Assets/icon.ico (or .png) next to the .exe
        var exeDir = AppContext.BaseDirectory;
        foreach (var candidate in new[] { "icon.ico", "icon.png", "logo.ico", "logo.png" })
        {
            var path = Path.Combine(exeDir, "Assets", candidate);
            if (!File.Exists(path)) continue;
            try
            {
                Icon = BitmapFrame.Create(new Uri(path, UriKind.Absolute));
            }
            catch { /* ignore bad icon files */ }
            break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hid.Dispose();
        base.OnClosed(e);
    }
}
