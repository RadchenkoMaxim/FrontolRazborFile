using System.Globalization;
using System.Windows.Data;

namespace FrontolFileAnalyzer;

public sealed class GroupNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        var separator = text.IndexOf('|');
        return separator >= 0 ? text[(separator + 1)..] : text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
