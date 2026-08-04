using Microsoft.UI.Xaml.Data;
using System;

namespace Summary.Converters
{
    internal partial class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is bool b ? !b : value;
    }
}