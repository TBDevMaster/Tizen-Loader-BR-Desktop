using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TizenLoaderBRDesktop.Helpers;

public sealed class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int selectedIndex)
        {
            return Visibility.Collapsed;
        }

        if (!int.TryParse(parameter?.ToString(), out var targetIndex))
        {
            return Visibility.Collapsed;
        }

        return selectedIndex == targetIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
