using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace WaxIPTV.Theming
{
    /// <summary>
    /// Loads theme definitions from a JSON document and applies them to a WPF
    /// <see cref="ResourceDictionary"/> at runtime.  Supports color tokens, typography
    /// settings and shape definitions.  Additional keys can be extended as needed.
    /// </summary>
    public static class ThemeLoader
    {
        /// <summary>
        /// Applies a theme specified in a JSON string to the given resource dictionary.
        /// Expected JSON structure:
        /// {
        ///   "colors": { "bg": "#FFFFFF", "text": "#000000", "accent": "#FF0000" },
        ///   "typography": { "fontFamily": "Segoe UI", "sizeBase": 14 },
        ///   "shape": { "radius": 8 }
        /// }
        /// </summary>
        /// <param name="json">The raw JSON string containing theme tokens.</param>
        /// <param name="resources">The resource dictionary to update.</param>
        public static void ApplyThemeJson(string json, ResourceDictionary resources)
        {
            var doc = JsonDocument.Parse(json).RootElement;
            if (doc.TryGetProperty("colors", out var colors))
            {
                if (colors.TryGetProperty("bg", out var bg))
                    resources["BgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg.GetString()!));
                if (colors.TryGetProperty("text", out var text))
                    resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text.GetString()!));
                if (colors.TryGetProperty("accent", out var accent))
                    resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent.GetString()!));
            }
            if (doc.TryGetProperty("typography", out var typography))
            {
                if (typography.TryGetProperty("fontFamily", out var ff))
                    resources["FontFamily"] = new FontFamily(ff.GetString()!);
                if (typography.TryGetProperty("sizeBase", out var sz))
                    resources["FontSize"] = sz.GetInt32();
            }
            if (doc.TryGetProperty("shape", out var shape))
            {
                if (shape.TryGetProperty("radius", out var radius))
                    resources["CornerRadius"] = new CornerRadius(radius.GetInt32());
            }
        }
    }
}