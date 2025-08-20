using System.Threading.Tasks;
using System.Windows;
using WaxIPTV.EpgGuide;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Minimal window hosting the new EPG guide grid.
    /// </summary>
    public partial class GuideWindow : Window
    {
        public GuideWindow(EpgSnapshot snapshot)
        {
            InitializeComponent();

            Loaded += async (_, __) =>
            {
                await ((GuideViewModel)Guide.DataContext).LoadFrom(snapshot);
            };
        }
    }
}
