using System;
using System.Globalization;
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
        /// Updates the display of the now and next programmes. The
        /// programmes should have their times specified in UTC; this
        /// method converts them to the local time zone using
        /// DateTimeOffset.ToLocalTime().
        /// </summary>
        /// <param name="now">The programme currently airing.</param>
        /// <param name="next">The programme airing after the current one.</param>
        public void UpdateProgrammes(Programme? now, Programme? next)
        {
            if (now != null)
            {
                var localStart = now.StartUtc.ToLocalTime().DateTime;
                var localEnd = now.EndUtc.ToLocalTime().DateTime;
                NowText.Text = $"{now.Title} ({FormatRange(localStart, localEnd)})";
            }
            else
            {
                NowText.Text = "No programme";
            }

            if (next != null)
            {
                var localStart = next.StartUtc.ToLocalTime().DateTime;
                var localEnd = next.EndUtc.ToLocalTime().DateTime;
                NextText.Text = $"{next.Title} ({FormatRange(localStart, localEnd)})";
            }
            else
            {
                NextText.Text = "No programme";
            }
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
    }
}