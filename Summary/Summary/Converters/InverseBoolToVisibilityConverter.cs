using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Summary.Converters
{
    internal partial class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue && boolValue)
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return (value is Visibility visibility && visibility != Visibility.Visible);
        }
    }
}