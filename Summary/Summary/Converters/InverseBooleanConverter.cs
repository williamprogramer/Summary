using Microsoft.UI.Xaml.Data;
using System;

namespace Summary.Converters
{
    public partial class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
                return !b;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
                return !b;
            return value;
        }
    }
}