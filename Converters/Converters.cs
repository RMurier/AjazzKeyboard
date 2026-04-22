using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AjazzKeyboard.Models;

namespace AjazzKeyboard.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is bool b ? !b : value;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value == null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class ConnectedBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class IntToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is int i ? (double)i : value;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is double d ? (int)d : value;
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value != null;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class KeyActionTypeConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        KeyActionType.None      => "No action",
        KeyActionType.Hotkey    => "Keyboard shortcut",
        KeyActionType.LaunchApp => "Launch application",
        KeyActionType.TextMacro => "Type text",
        _ => value?.ToString() ?? ""
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
