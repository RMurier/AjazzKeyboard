using System.Windows.Controls;
using AjazzKeyboard.ViewModels;

namespace AjazzKeyboard.Views;

public partial class RgbPage : Page
{
    public RgbPage(RgbPageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
