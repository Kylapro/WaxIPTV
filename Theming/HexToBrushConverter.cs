using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WaxIPTV.Theming
{
    /// <summary>
    /// Converter that transforms a hex string into a SolidColorBrush and
    /// back again.  Used by the settings UI to display colour swatches
    /// for the theme editor.  Non‑hex inputs return a transparent brush.
    /// </summary>
    public sealed class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(s)!;
                    return new SolidColorBrush(color);
                }
                catch
                {
                    // ignore invalid input
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush b)
            {
                var c = b.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#000000";
        }
    }
}