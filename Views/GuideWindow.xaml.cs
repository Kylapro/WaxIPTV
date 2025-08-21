using System;
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
        private readonly MainWindow _parent;

        public GuideWindow(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;

            Loaded += async (_, __) =>
            {
                if (parent.CurrentEpgSnapshot != null)
                    await ((GuideViewModel)Guide.DataContext).LoadFromAsync(parent.CurrentEpgSnapshot);
            };

            parent.EpgSnapshotUpdated += Parent_EpgSnapshotUpdated;
            Closed += (_, __) => parent.EpgSnapshotUpdated -= Parent_EpgSnapshotUpdated;
        }

        private void Parent_EpgSnapshotUpdated(EpgSnapshot snapshot)
        {
            Dispatcher.InvokeAsync(async () =>
                await ((GuideViewModel)Guide.DataContext).LoadFromAsync(snapshot));
        }
    }
}
