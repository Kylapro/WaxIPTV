using System.Collections.Generic;
using System.Windows;
using System.Threading.Tasks;
using WaxIPTV.EpgGuide;
using WaxIPTV.Models;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Minimal window hosting the new EPG guide grid.
    /// </summary>
    public partial class GuideWindow : Window
    {
        public GuideWindow(List<Channel> channels, Dictionary<string, List<Programme>> programmes)
        {
            InitializeComponent();

            Loaded += async (_, __) =>
            {
                await ((GuideViewModel)Guide.DataContext).LoadIncrementalAsync(channels, programmes);
            };
        }
    }
}
