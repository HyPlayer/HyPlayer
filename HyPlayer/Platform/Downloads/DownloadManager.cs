using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Settings;
using HyPlayer.Platform.Runtime;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Lyric;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using TagLib;

namespace HyPlayer.Features.Downloads.Services;

internal static class DownloadManager
{
    private const int MaxAlbumPicturesCacheSize = 64;
    private static bool _timerStarted;
    public static ObservableCollection<DownloadObject> DownloadLists { get; } = [];
    public static BackgroundDownloader Downloader { get; } = new();
    public static List<Task> WritingTasks { get; } = [];
    public static Dictionary<string, Picture> AlbumPicturesCache { get; } = [];
    private static IGlobalTimerService GlobalTimer => Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private static INotificationService Notification => Ioc.Default.GetRequiredService<INotificationService>();
    private static IUIThreadDispatcher UIThreadDispatcher => Ioc.Default.GetRequiredService<IUIThreadDispatcher>();
    private static DownloadSettings DownloadSettings => Ioc.Default.GetRequiredService<DownloadSettings>();
    private static LyricSettings LyricSettings => Ioc.Default.GetRequiredService<LyricSettings>();
    private static HttpClient HttpClient => Ioc.Default.GetRequiredService<HttpClient>();
    private static ILyricProvidable LyricProvider => Ioc.Default.GetRequiredService<ILyricProvidable>();

    private static IReadOnlyList<IMusicResourceProvidable> MusicResourceProviders =>
        AppDepository.ResolveMultiple<IMusicResourceProvidable>();

    private static IResourceQualityTagProvidable QualityTagProvider =>
        Ioc.Default.GetRequiredService<IResourceQualityTagProvidable>();

    private static IDiagnosticsStateService Diagnostics => Ioc.Default.GetRequiredService<IDiagnosticsStateService>();

    public static bool CheckDownloadAbilityAndToast()
    {
        Notification.ShowMessage("开始下载");
        return true;
    }

    private static void EnsureTimerStarted()
    {
        if (!_timerStarted)
        {
            GlobalTimer.SecondTick += Timer_Elapsed;
            _timerStarted = true;
        }
    }

    public static void StopTimer()
    {
        if (_timerStarted)
        {
            GlobalTimer.SecondTick -= Timer_Elapsed;
            _timerStarted = false;
        }

        WritingTasks.RemoveAll(t => t.IsCompleted);
        if (DownloadLists.Count == 0)
            AlbumPicturesCache.Clear();
        TrimAlbumPicturesCache();
    }

    public static void AddDownload(SingleSongBase song)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        CleanupCompletedWritingTasks();
        EnsureTimerStarted();

        DownloadLists.Add(CreateDownloadObject(song));
    }

    public static void AddDownload(List<SingleSongBase> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        CleanupCompletedWritingTasks();
        EnsureTimerStarted();

        songs.ForEach(t => { DownloadLists.Add(CreateDownloadObject(t)); });
    }

    private static void Timer_Elapsed(object? sender, EventArgs e)
    {
        Timer_Elapsed();
    }

    private static void Timer_Elapsed()
    {
        if (DownloadLists.Count == 0)
        {
            StopTimer();
            return;
        }

        var maxDownloadCount = DownloadSettings.MaxDownloadCount;
        for (var i = 0; i < DownloadLists.Count; i++)
            switch (DownloadLists[i].Status)
            {
                case DownloadObject.DownloadStatus.Downloading:
                    if (--maxDownloadCount <= 0) return;
                    continue;
                case DownloadObject.DownloadStatus.Queueing:
                    _ = DownloadLists[i].StartDownload();
                    --maxDownloadCount;
                    return;
                case DownloadObject.DownloadStatus.Finished:
                    var i1 = i;
                    _ = UIThreadDispatcher.TryRunAsync(() =>
                    {
                        DownloadLists.RemoveAt(i1);
                        if (DownloadLists.Count == 0)
                            StopTimer();
                    });
                    break;
                case DownloadObject.DownloadStatus.Paused:
                case DownloadObject.DownloadStatus.Error:
                    break;
            }
    }

    private static DownloadObject CreateDownloadObject(SingleSongBase song)
    {
        return new DownloadObject(
            song,
            Notification,
            UIThreadDispatcher,
            DownloadSettings,
            LyricSettings,
            HttpClient,
            LyricProvider,
            MusicResourceProviders,
            QualityTagProvider,
            Diagnostics);
    }

    public static void CacheAlbumPicture(string albumId, Picture picture)
    {
        if (string.IsNullOrWhiteSpace(albumId))
            return;

        AlbumPicturesCache[albumId] = picture;
        TrimAlbumPicturesCache();
    }

    private static void CleanupCompletedWritingTasks()
    {
        WritingTasks.RemoveAll(t => t.IsCompleted);
    }

    private static void TrimAlbumPicturesCache()
    {
        while (AlbumPicturesCache.Count > MaxAlbumPicturesCacheSize)
            AlbumPicturesCache.Remove(AlbumPicturesCache.Keys.First());
    }
}
