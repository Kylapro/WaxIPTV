using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using WaxIPTV.EpgGuide;
using WaxIPTV.Models;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Minimal window hosting the new EPG guide grid.
    /// </summary>
    public partial class GuideWindow : Window
    {
        public GuideWindow(EpgSnapshot? snapshot, IList<Channel> channels, Dictionary<string, List<Programme>> programmes)
        {
            InitializeComponent();
            Loaded += async (_, __) => { await LoadData(snapshot, channels, programmes); };
        }

        public async Task LoadData(EpgSnapshot? snapshot, IList<Channel> channels, Dictionary<string, List<Programme>> programmes)
        {
            var vm = (GuideViewModel)Guide.DataContext;
            if (snapshot != null)
            {
                await vm.LoadFrom(snapshot);
            }
            else
            {
                await vm.LoadIncrementalAsync(channels, programmes);
            }
        }
    }
}
