using System.Windows.Controls;
using AjazzKeyboard.ViewModels;

namespace AjazzKeyboard.Views;

public partial class KeyboardPage : Page
{
    public KeyboardPage(KeyboardPageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
