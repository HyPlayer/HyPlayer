using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System.Threading.Tasks;

namespace HyPlayer.Services.LastFM
{
    public static class LastFMManager
    {
        public static async Task TryLoginLastfmAccountFromBrowser(string token)
        {
            await Ioc.Default.GetRequiredService<ILastFmService>().CompleteBrowserLoginAsync(token);
        }
        public static async Task UpdateNowPlaying(SingleSongBase item)
        {
            await Ioc.Default.GetRequiredService<ILastFmService>().UpdateNowPlayingAsync(item);
        }
        public static async Task Scrobble(SingleSongBase item)
        {
            await Ioc.Default.GetRequiredService<ILastFmService>().ScrobbleAsync(item);
        }
    }
}
