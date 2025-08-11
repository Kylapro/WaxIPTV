using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace WaxIPTV.Theming
{
    /// <summary>
    /// Provides functionality for applying a theme described as a JSON document
    /// to a WPF <see cref="ResourceDictionary"/>.  The expected JSON
    /// structure defines colours, typography settings, shape definitions
    /// and optional density values.  Tokens not specified will fall back
    /// to their existing values in the resource dictionary.
    /// </summary>
    public static class ThemeLoader
    {
        /// <summary>
        /// Applies a theme specified in a JSON string to the given resource
        /// dictionary.  This method will update brushes, fonts, sizes,
        /// corner radius and spacing values based on the provided
        /// properties.  Missing values are left unchanged.  Supported
        /// JSON schema:
        /// <code>
        /// {
        ///   "colors": {
        ///     "bg":     "#FFFFFF",
        ///     "surface":"#13131A",
        ///     "text":   "#000000",
        ///     "muted":  "#B8B9C2",
        ///     "accent": "#5B8CFF"
        ///   },
        ///   "typography": {
        ///     "fontFamily": "Segoe UI",
        ///     "sizeBase":  14
        ///   },
        ///   "shape": { "radius": 8 },
        ///   "density": { "scale": 1.0 }
        /// }
        /// </code>
        /// Colours may omit surface/muted entries; ThemeLoader will derive
        /// them from the background colour if absent.  Density controls
        /// the scale factor applied to spacing.
        /// </summary>
        /// <param name="json">The raw JSON string containing theme tokens.</param>
        /// <param name="rd">The resource dictionary to update.</param>
        public static void ApplyThemeJson(string json, ResourceDictionary rd)
        {
            var root = JsonDocument.Parse(json).RootElement;

            // Local helpers to read values safely
            static string GetNestedString(JsonElement parent, string key, string def)
            {
                if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                {
                    var s = val.GetString();
                    return !string.IsNullOrWhiteSpace(s) ? s : def;
                }
                return def;
            }
            static double GetNestedDouble(JsonElement parent, string key, double def)
            {
                if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var val))
                {
                    if (val.ValueKind == JsonValueKind.Number)
                        return val.GetDouble();
                    if (val.ValueKind == JsonValueKind.String && double.TryParse(val.GetString(), out var d))
                        return d;
                }
                return def;
            }

            // Extract nested objects
            var colors = root.TryGetProperty("colors", out var c) ? c : default;
            var typo   = root.TryGetProperty("typography", out var t) ? t : default;
            var shape  = root.TryGetProperty("shape", out var sh) ? sh : default;
            var density = root.TryGetProperty("density", out var d) ? d : default;

            // Colours
            // Extract default colour strings from existing brushes.  If the
            // resource dictionary contains brushes that are not SolidColorBrush
            // instances, fall back to hard‑coded defaults.
            string BgFallback() => rd["BgBrush"] is SolidColorBrush sb ? sb.Color.ToString() : "#0B0B0F";
            string TextFallback() => rd["TextBrush"] is SolidColorBrush sb2 ? sb2.Color.ToString() : "#FFFFFF";
            string MutedFallback() => rd["MutedTextBrush"] is SolidColorBrush sb3 ? sb3.Color.ToString() : "#B8B9C2";
            string AccentFallback() => rd["AccentBrush"] is SolidColorBrush sb4 ? sb4.Color.ToString() : "#5B8CFF";
            var bgHex     = GetNestedString(colors, "bg",     BgFallback());
            var surfHex   = GetNestedString(colors, "surface", "");
            var textHex   = GetNestedString(colors, "text",   TextFallback());
            var mutedHex  = GetNestedString(colors, "muted",  MutedFallback());
            var accentHex = GetNestedString(colors, "accent", AccentFallback());
            var dropdownHex = GetNestedString(colors, "dropdown", "");

            // Typography
            var fontFamily = GetNestedString(typo, "fontFamily", ((FontFamily)rd["FontFamilyBase"]).Source);
            var sizeBase   = GetNestedDouble(typo, "sizeBase", (double)rd["FontSizeBase"]);

            // Shape & density
            var radiusVal  = GetNestedDouble(shape, "radius", ((CornerRadius)rd["ThemeCornerRadius"]).TopLeft);
            var densityVal = GetNestedDouble(density, "scale", 1.0);

            // Convert hex strings to Color
            static Color FromHex(string hex)
            {
                return (Color)ColorConverter.ConvertFromString(hex)!;
            }
            static Color WithAlpha(Color c, byte alpha)
            {
                return Color.FromArgb(alpha, c.R, c.G, c.B);
            }
            static Color Blend(Color bg, Color fg, double t)
            {
                byte Lerp(byte b, byte f) => (byte)(b + (f - b) * t);
                return Color.FromArgb(0xFF, Lerp(bg.R, fg.R), Lerp(bg.G, fg.G), Lerp(bg.B, fg.B));
            }

            // Parse base colours
            var bgCol    = FromHex(bgHex);
            var textCol  = FromHex(textHex);
            var mutedCol = FromHex(mutedHex);
            var accentCol= FromHex(accentHex);
            // Derive surface colour if not specified.  Use a small blend toward
            // white on dark backgrounds and toward black on light backgrounds.
            Color surfCol;
            if (!string.IsNullOrWhiteSpace(surfHex))
            {
                surfCol = FromHex(surfHex);
            }
            else
            {
                double lumBg = (bgCol.R + bgCol.G + bgCol.B) / 3.0;
                if (lumBg > 128)
                {
                    // Light background: surface is slightly darker (towards black)
                    surfCol = Blend(bgCol, Colors.Black, 0.06);
                }
                else
                {
                    // Dark background: surface is slightly lighter (towards white)
                    surfCol = Blend(bgCol, Colors.White, 0.06);
                }
            }

            // Determine drop‑down background colour.  If specified in the theme
            // JSON, use it; otherwise default to the surface colour.
            Color dropdownCol;
            if (!string.IsNullOrWhiteSpace(dropdownHex))
            {
                dropdownCol = FromHex(dropdownHex);
            }
            else
            {
                dropdownCol = surfCol;
            }

            // Update brushes
            rd["BgBrush"]               = new SolidColorBrush(bgCol);
            rd["SurfaceBrush"]          = new SolidColorBrush(surfCol);
            rd["TextBrush"]             = new SolidColorBrush(textCol);
            rd["MutedTextBrush"]        = new SolidColorBrush(mutedCol);
            rd["AccentBrush"]           = new SolidColorBrush(accentCol);
            rd["AccentHoverBrush"]      = new SolidColorBrush(WithAlpha(accentCol, 0x33));
            rd["AccentSelectionBrush"]  = new SolidColorBrush(WithAlpha(accentCol, 0x55));
            // Choose divider colour based on the brightness of the background.  Use a
            // semi‑opaque white on dark backgrounds and a semi‑opaque black on
            // light backgrounds.  Luminance is approximated by averaging RGB
            // components.
            Color dividerCol;
            double lum = (bgCol.R + bgCol.G + bgCol.B) / 3.0;
            if (lum > 128)
            {
                // Light background: use translucent black as divider
                dividerCol = WithAlpha(Colors.Black, 0x22);
            }
            else
            {
                // Dark background: use translucent white as divider
                dividerCol = WithAlpha(Colors.White, 0x22);
            }
            rd["DividerBrush"] = new SolidColorBrush(dividerCol);

            // Drop‑down backgrounds use their own brush so that users can
            // customise them independently of the surface.
            rd["DropdownBrush"] = new SolidColorBrush(dropdownCol);

            // Update typography and shape
            rd["FontFamilyBase"]    = new FontFamily(fontFamily);
            rd["FontSizeBase"]      = sizeBase;
            rd["ThemeCornerRadius"] = new CornerRadius(radiusVal);

            // Calculate spacing based on sizeBase and density
            var baseSpacing = Math.Max(6, Math.Round(sizeBase * 0.6));
            double Scale(double k) => Math.Round(baseSpacing * k * densityVal);
            rd["SpacingXS"] = new Thickness(Scale(0.5));
            rd["SpacingS"]  = new Thickness(Scale(1.0));
            rd["SpacingM"]  = new Thickness(Scale(1.5));
            rd["SpacingL"]  = new Thickness(Scale(2.0));

            // Freeze brush resources for performance (optional)
            static void TryFreeze(object obj)
            {
                if (obj is Freezable freezable && freezable.CanFreeze)
                    freezable.Freeze();
            }
            TryFreeze(rd["BgBrush"]);
            TryFreeze(rd["SurfaceBrush"]);
            TryFreeze(rd["TextBrush"]);
            TryFreeze(rd["MutedTextBrush"]);
            TryFreeze(rd["AccentBrush"]);
            TryFreeze(rd["AccentHoverBrush"]);
            TryFreeze(rd["AccentSelectionBrush"]);
            TryFreeze(rd["DividerBrush"]);

            TryFreeze(rd["DropdownBrush"]);
        }
    }
}