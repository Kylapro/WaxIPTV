using Microsoft.Win32;

namespace WaxIPTV.Theming
{
    /// <summary>
    /// Provides access to the Windows system theme settings.  The current
    /// theme is determined by reading the <c>AppsUseLightTheme</c> value
    /// from the user's registry hive.  A value of 0 indicates dark mode
    /// while 1 indicates light mode.  If the value cannot be read, light
    /// mode is assumed.
    /// </summary>
    public static class SystemThemeService
    {
        private const string PersonalizeKey = @"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
        private const string ValueName = "AppsUseLightTheme";

        /// <summary>
        /// Returns <c>true</c> if Windows is configured for light mode,
        /// <c>false</c> for dark mode.  The method falls back to light mode
        /// if the registry cannot be read.
        /// </summary>
        public static bool IsLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key?.GetValue(ValueName) is int value)
                {
                    return value > 0;
                }
            }
            catch
            {
                // Ignore registry errors and assume light theme
            }
            return true;
        }
    }
}
