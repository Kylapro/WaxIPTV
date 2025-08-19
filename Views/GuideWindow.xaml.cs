using System.Collections.Generic;
using System.Linq;
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
        public GuideWindow(List<Channel> channels, Dictionary<string, List<Programme>> programmes)
        {
            InitializeComponent();

            var snapshot = new EpgSnapshot
            {
                Channels = channels.Select((c, i) => new ChannelMeta
                {
                    TvgId = c.Id,
                    Number = (i + 1).ToString(),
                    Name = c.Name,
                    Group = c.Group,
                    LogoPath = c.Logo
                }).ToArray(),
                Programs = programmes.SelectMany(kv => kv.Value.Select(p => new ProgramBlock
                {
                    ChannelTvgId = kv.Key,
                    Title = p.Title,
                    Synopsis = p.Desc,
                    StartUtc = p.StartUtc.UtcDateTime,
                    EndUtc = p.EndUtc.UtcDateTime,
                    IsLive = false,
                    IsNew = false
                })).ToArray()
            };

            ((GuideViewModel)Guide.DataContext).LoadFrom(snapshot);
        }
    }
}
