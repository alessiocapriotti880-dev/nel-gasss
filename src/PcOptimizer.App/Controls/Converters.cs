using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PcOptimizer.App.Controls;

/// <summary>true → Visible; con ConverterParameter "invert" il contrario.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter as string == "invert")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Visibility.Visible;
}

/// <summary>null o stringa vuota → Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Livello (rischio, gravità, esito, stato della checklist) → colore del tema: verde per basso/positivo,
/// giallo per medio/attenzione, rosso per alto/critico, grigio per informativo.
/// </summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString() switch
        {
            "Low" or "Improved" or "Done" or "Tenuto" or "Stable" => "Good",
            "Medium" or "Warning" or "Suggestion" or "Check" or "Action" or "Dopo il riavvio" => "Warn",
            "High" or "Critical" or "Worse" or "Blocked" or "Annullato" or "Errore" or "Unstable" => "Bad",
            _ => "Info",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Rapporto 0-1 → larghezza in pixel (ConverterParameter = larghezza massima, predefinita 220).</summary>
public sealed class RatioToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var max = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 220;
        return value is double ratio && double.IsFinite(ratio) ? Math.Max(2, ratio * max) : 2.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
