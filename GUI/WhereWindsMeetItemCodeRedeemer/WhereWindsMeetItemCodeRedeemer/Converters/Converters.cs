using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Converters;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isTrue = value is bool b && b;
        return isTrue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PendingBrush = new(Color.FromRgb(59, 130, 246));   // Blue
    private static readonly SolidColorBrush RedeemedBrush = new(Color.FromRgb(107, 114, 128)); // Gray
    private static readonly SolidColorBrush ProcessingBrush = new(Color.FromRgb(245, 158, 11)); // Amber
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(16, 185, 129));   // Green
    private static readonly SolidColorBrush FailedBrush = new(Color.FromRgb(239, 68, 68));     // Red
    private static readonly SolidColorBrush SkippedBrush = new(Color.FromRgb(156, 163, 175));  // Light gray

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CodeStatus status)
        {
            return status switch
            {
                CodeStatus.Pending => PendingBrush,
                CodeStatus.Redeemed => RedeemedBrush,
                CodeStatus.Processing => ProcessingBrush,
                CodeStatus.Success => SuccessBrush,
                CodeStatus.Failed => FailedBrush,
                CodeStatus.Skipped => SkippedBrush,
                _ => RedeemedBrush
            };
        }

        return RedeemedBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class ZeroToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value is int i ? i : 0;
        bool visible = Invert ? count > 0 : count == 0;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString()!.Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            return Enum.Parse(targetType, parameter.ToString()!, true);
        }
        return Binding.DoNothing;
    }
}
