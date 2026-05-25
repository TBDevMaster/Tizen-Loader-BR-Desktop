using System.Globalization;
using System.Windows.Data;

namespace TizenLoaderBRDesktop.Helpers;

public sealed class BoolToSimNaoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool flag && flag ? "Sim" : "Não";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string text && text.Equals("Sim", StringComparison.OrdinalIgnoreCase);
    }
}
