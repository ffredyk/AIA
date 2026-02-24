using System.Globalization;
using AIA.Domain.Models;
using TaskStatus = AIA.Domain.Models.TaskStatus;

namespace AIA.Mobile.Converters
{
    public class PriorityToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is TaskPriority priority)
            {
                return priority switch
                {
                    TaskPriority.Low => Color.FromArgb("#808080"),
                    TaskPriority.Medium => Color.FromArgb("#0078D4"),
                    TaskPriority.High => Color.FromArgb("#FFA500"),
                    TaskPriority.Critical => Color.FromArgb("#FF4444"),
                    _ => Color.FromArgb("#808080")
                };
            }
            return Color.FromArgb("#808080");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is TaskStatus status)
            {
                return status switch
                {
                    TaskStatus.NotStarted => Color.FromArgb("#808080"),
                    TaskStatus.InProgress => Color.FromArgb("#0078D4"),
                    TaskStatus.OnHold => Color.FromArgb("#FFA500"),
                    TaskStatus.Completed => Color.FromArgb("#1EB75F"),
                    TaskStatus.Cancelled => Color.FromArgb("#666666"),
                    _ => Color.FromArgb("#808080")
                };
            }
            return Color.FromArgb("#808080");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsNotNullOrEmptyConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is string str && !string.IsNullOrWhiteSpace(str);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsNotNullConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
