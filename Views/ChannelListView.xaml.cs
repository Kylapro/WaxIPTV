using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using WaxIPTV.Models;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Code‑behind for the ChannelListView user control.  This class
    /// exposes helpers for setting the channel list, retrieving the
    /// currently selected channel and raising an event when a channel
    /// is double‑clicked.  The event is used by the main window to
    /// trigger immediate playback without requiring the user to press
    /// the Play button.
    /// </summary>
    public partial class ChannelListView : UserControl
    {
        /// <summary>
        /// Raised when a channel is activated via double click.  The event
        /// carries a <see cref="ChannelEventArgs"/> instance which
        /// encapsulates the activated channel.  A custom event args type is
        /// used because <see cref="EventHandler{TEventArgs}"/> requires
        /// that T derives from <see cref="EventArgs"/>.
        /// </summary>
        public event EventHandler<ChannelEventArgs>? ChannelActivated;

        public ChannelListView()
        {
            InitializeComponent();
            // Forward the selection changed event so consumers can react
            ChannelList.SelectionChanged += ChannelList_SelectionChanged;
        }

        /// <summary>
        /// Populates the list of channels shown in the ListBox.  Any
        /// existing items are replaced.  This method accepts any
        /// IEnumerable of Channel objects.
        /// </summary>
        /// <param name="channels">The channel collection to display.</param>
        public void SetItems(IEnumerable<Channel> channels)
        {
            ChannelList.ItemsSource = channels;
        }

        /// <summary>
        /// Returns the currently selected Channel, or null if nothing
        /// is selected.
        /// </summary>
        public Channel? GetSelected() => ChannelList.SelectedItem as Channel;

        /// <summary>
        /// Selects the first item in the list if available.  This is
        /// used by MainWindow to ensure a channel is selected on
        /// startup when channels are loaded.  If no items are present
        /// the selection remains unchanged.
        /// </summary>
        public void SelectFirst()
        {
            if (ChannelList.Items.Count > 0)
            {
                ChannelList.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Raised whenever the selection in the internal ListBox changes.
        /// Consumers can subscribe to this event to be notified of
        /// selection changes without needing to access the ListBox
        /// directly.  The event arguments are forwarded from the
        /// ListBox's SelectionChanged event.
        /// </summary>
        public event SelectionChangedEventHandler? SelectionChanged;

        /// <summary>
        /// Handles the ListBox double click event by raising the
        /// ChannelActivated event.  The event argument carries the
        /// selected Channel; if no selection exists then no event
        /// is raised.
        /// </summary>
        private void ChannelList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ChannelList.SelectedItem is Channel ch)
            {
                ChannelActivated?.Invoke(this, new ChannelEventArgs(ch));
            }
        }

        // Forward the selection changed event from the ListBox to our
        // public SelectionChanged event.  This allows consumers such
        // as the main window to listen for selection changes without
        // relying on the internal ListBox implementation.
        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectionChanged?.Invoke(this, e);
        }

        // (No additional initialization logic is required beyond
        // subscribing in the constructor.)
    }
}