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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.Features.History.Services
{
    public sealed record CurPlayingListHistoryState(List<string> SongIds, int CurrentIndex);

    public class HistoryManagement
    {
        public static void InitializeHistoryTrack()
        {
            Ioc.Default.GetRequiredService<IHistoryService>().InitializeHistoryTrack();
        }

        public static void AddNCSongHistory(string songid)
        {
            Ioc.Default.GetRequiredService<IHistoryService>().AddNCSongHistory(songid);
        }

        public static void AddSearchHistory(string Text)
        {
            Ioc.Default.GetRequiredService<IHistoryService>().AddSearchHistory(Text);
        }

        public static void AddSonglistHistory(string playListid)
        {
            Ioc.Default.GetRequiredService<IHistoryService>().AddSonglistHistory(playListid);
        }

        public static async Task SetcurPlayingListHistory(List<string> songids, int currentIndex)
        {
            await Ioc.Default.GetRequiredService<IHistoryService>().SetCurrentPlayingListHistoryAsync(songids, currentIndex);
        }

        public static async Task ClearHistory()
        {
            await Ioc.Default.GetRequiredService<IHistoryService>().ClearHistoryAsync();
        }

        public static async Task<List<SingleSongBase>> GetSongHistory()
        {
            return await Ioc.Default.GetRequiredService<IHistoryService>().GetSongHistoryAsync();
        }

        public static List<string> GetSearchHistory()
        {
            return Ioc.Default.GetRequiredService<IHistoryService>().GetSearchHistory();
        }

        public static async Task<List<SingleSongBase>> GetcurPlayingListHistory()
        {
            return await Ioc.Default.GetRequiredService<IHistoryService>().GetCurrentPlayingListHistoryAsync();
        }

        public static async Task<CurPlayingListHistoryResult> GetCurPlayingListHistoryStateAsync()
        {
            return await Ioc.Default.GetRequiredService<IHistoryService>().GetCurrentPlayingListHistoryStateAsync();
        }
    }

    public sealed record CurPlayingListHistoryResult(List<SingleSongBase> Songs, int CurrentIndex);
}
