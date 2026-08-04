using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Summary.Converters
{
    internal partial class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue && boolValue)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return (value is Visibility v && v == Visibility.Visible);
        }
    }
}