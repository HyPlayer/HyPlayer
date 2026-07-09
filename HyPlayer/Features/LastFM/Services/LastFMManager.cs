using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System.Threading.Tasks;

namespace HyPlayer.Features.LastFM.Services
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
