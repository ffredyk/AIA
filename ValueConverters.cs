using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace AIA
{
    /// <summary>
    /// Converts an enum value to an array of all values of that enum type.
    /// </summary>
    public class EnumValuesConverter : MarkupExtension, IValueConverter
    {
        public static EnumValuesConverter Instance { get; } = new EnumValuesConverter();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            return Enum.GetValues(value.GetType());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }

    /// <summary>
    /// Converts an integer to Visibility. 0 = Collapsed, > 0 = Visible.
    /// Use ConverterParameter="Inverse" to invert the behavior.
    /// </summary>
    public class IntToVisibilityConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool inverse = parameter?.ToString() == "Inverse";
            bool hasValue = value is int intVal && intVal > 0;
            
            if (inverse)
                return hasValue ? Visibility.Collapsed : Visibility.Visible;
            
            return hasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }

    /// <summary>
    /// Converts a boolean to Visibility with inverse logic.
    /// True = Collapsed, False = Visible.
    /// Use ConverterParameter="Bool" to return inverse boolean instead of Visibility.
    /// </summary>
    public class InverseBoolToVisibilityConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // If parameter is "Bool", return inverse boolean
                if (parameter?.ToString() == "Bool")
                {
                    return !boolValue;
                }
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            
            if (parameter?.ToString() == "Bool")
            {
                return true;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }

    /// <summary>
    /// Converts null to Visibility. Null = Collapsed, Not null = Visible.
    /// Use ConverterParameter="Inverse" to invert: Null = Visible, Not null = Collapsed.
    /// </summary>
    public class NullToVisibilityConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null || (value is string str && string.IsNullOrEmpty(str));
            bool inverse = parameter?.ToString() == "Inverse";
            
            if (inverse)
            {
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            }
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }
}
