using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WaxIPTV.Models;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Interaction logic for EpgNowNext.xaml
    /// </summary>
    public partial class EpgNowNext : UserControl
    {
        public EpgNowNext()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Updates the list of programmes for the selected channel.  The
        /// programmes should be provided in UTC and are converted to the
        /// local time zone before display.
        /// </summary>
        /// <param name="programmes">All programmes for the channel in start time order.</param>
        public void UpdateProgrammes(List<Programme>? programmes)
        {
            if (programmes == null || programmes.Count == 0)
            {
                ProgrammeList.ItemsSource = new List<ProgrammeDisplay>
                {
                    new("No programme", string.Empty, string.Empty)
                };
                return;
            }

            var items = programmes.Select(p =>
            {
                var localStart = p.StartUtc.ToLocalTime().DateTime;
                var localEnd = p.EndUtc.ToLocalTime().DateTime;
                return new ProgrammeDisplay(
                    p.Title,
                    FormatRange(localStart, localEnd),
                    p.Desc ?? string.Empty
                );
            }).ToList();

            ProgrammeList.ItemsSource = items;
        }

        /// <summary>
        /// Formats a start and end DateTime into a short time range string
        /// such as "18:00 - 19:30" using the current culture.
        /// </summary>
        private static string FormatRange(DateTime start, DateTime end)
        {
            string startStr = start.ToString("t", CultureInfo.CurrentCulture);
            string endStr = end.ToString("t", CultureInfo.CurrentCulture);
            return $"{startStr} - {endStr}";
        }

        private record ProgrammeDisplay(string Title, string TimeRange, string Description);
    }
}