using System.Windows;
using WaxIPTV.Models;
using WaxIPTV.Services;
using WaxIPTV.ViewModels;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml.  This window hosts the
    /// startup settings UI and binds to a <see cref="SettingsViewModel"/>.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>
        /// Constructs the settings window, creating a view model and
        /// assigning it as the DataContext.  Accepts the settings
        /// service and existing settings instance to allow saving
        /// changes.
        /// </summary>
        public SettingsWindow(SettingsService service, AppSettings settings)
        {
            InitializeComponent();
            DataContext = new SettingsViewModel(service, settings);
        }
    }
}