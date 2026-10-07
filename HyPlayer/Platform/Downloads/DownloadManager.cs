using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
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
using ObservableCollections;

namespace HyPlayer.Features.Downloads.Services;

internal static class DownloadManager
{
    private const int MaxAlbumPicturesCacheSize = 64;
    private static readonly object StateGate = new();
    private static bool _timerStarted;
    private static int _tickPending;
    public static ObservableList<DownloadObject> DownloadLists { get; } = [];
    public static NotifyCollectionChangedSynchronizedViewList<DownloadObject> DownloadListsView { get; } =
        DownloadLists.ToNotifyCollectionChanged();
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

        var isEmpty = DownloadLists.Count == 0;
        lock (StateGate)
        {
            WritingTasks.RemoveAll(t => t.IsCompleted);
            if (isEmpty)
                AlbumPicturesCache.Clear();
            TrimAlbumPicturesCache();
        }
    }

    public static Task AddDownload(SingleSongBase song)
    {
        if (!CheckDownloadAbilityAndToast()) return Task.CompletedTask;
        return UIThreadDispatcher.TryRunAsync(() =>
        {
            CleanupCompletedWritingTasks();
            EnsureTimerStarted();
            DownloadLists.Add(CreateDownloadObject(song));
        });
    }

    public static Task AddDownload(List<SingleSongBase> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return Task.CompletedTask;
        return UIThreadDispatcher.TryRunAsync(async () =>
        {
            CleanupCompletedWritingTasks();
            EnsureTimerStarted();
            var added = 0;
            foreach (var song in songs)
            {
                DownloadLists.Add(CreateDownloadObject(song));
                if (++added % 32 == 0) await Task.Yield();
            }
        });
    }

    private static void Timer_Elapsed(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _tickPending, 1) != 0) return;
        _ = DispatchTickAsync();
    }

    private static async Task DispatchTickAsync()
    {
        try
        {
            await UIThreadDispatcher.TryRunAsync(Timer_ElapsedOnUi);
        }
        finally
        {
            Volatile.Write(ref _tickPending, 0);
        }
    }

    private static void Timer_ElapsedOnUi()
    {
        if (DownloadLists.Count == 0)
        {
            StopTimer();
            return;
        }

        var downloads = DownloadLists.ToArray();
        foreach (var download in downloads)
            if (download.Status == DownloadObject.DownloadStatus.Finished)
                DownloadLists.Remove(download);

        foreach (var download in DownloadQueueScheduler.GetReadyDownloads(downloads, DownloadSettings.MaxDownloadCount))
            _ = download.StartDownload();
        if (DownloadLists.Count == 0) StopTimer();
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

        lock (StateGate)
        {
            AlbumPicturesCache[albumId] = picture;
            TrimAlbumPicturesCache();
        }
    }

    public static bool TryGetAlbumPicture(string albumId, out Picture picture)
    {
        lock (StateGate)
            return AlbumPicturesCache.TryGetValue(albumId, out picture!);
    }

    public static void RegisterWritingTask(Task task)
    {
        lock (StateGate)
            WritingTasks.Add(task);
    }

    public static void RemoveDownload(DownloadObject download)
    {
        _ = UIThreadDispatcher.TryRunAsync(() =>
        {
            DownloadLists.Remove(download);
            if (DownloadLists.Count == 0)
                StopTimer();
        });
    }

    internal static void CleanupCompletedWritingTasks()
    {
        lock (StateGate)
            WritingTasks.RemoveAll(t => t.IsCompleted);
    }

    private static void TrimAlbumPicturesCache()
    {
        while (AlbumPicturesCache.Count > MaxAlbumPicturesCacheSize)
            AlbumPicturesCache.Remove(AlbumPicturesCache.Keys.First());
    }
}
