using System.Windows.Controls;
using AjazzKeyboard.ViewModels;

namespace AjazzKeyboard.Views;

public partial class ProfilesPage : Page
{
    public ProfilesPage(ProfilesPageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
