using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WaxIPTV.Theming;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml.  This view provides a simple
    /// panel for editing the theme JSON at runtime.  Users can paste
    /// arbitrary JSON into the text box and click Apply to update the
    /// application-wide resource dictionary.  In a full implementation
    /// additional settings (e.g. playlist URL, player paths) could be
    /// edited here as well.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            // Load the current theme JSON into the text box on startup
            try
            {
                var themePath = Theming.ThemeManager.CurrentThemePath;
                if (File.Exists(themePath))
                {
                    ThemeJsonEditor.Text = File.ReadAllText(themePath);
                }
            }
            catch
            {
                // ignore read errors
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            var json = ThemeJsonEditor.Text;
            try
            {
                // Validate JSON by parsing; will throw if invalid
                JsonDocument.Parse(json);
                // Apply to application resources
                ThemeLoader.ApplyThemeJson(json, Application.Current.Resources);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid theme JSON: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}