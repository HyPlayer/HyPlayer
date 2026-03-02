using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Classes;
using HyPlayer.Contracts.Services;
using HyPlayer.Contracts.ViewModels;
using HyPlayer.HyPlayControl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace HyPlayer.ViewModels
{
    public partial class SongListDetailViewModel : ObservableRecipient, IViewModel
    {
#nullable enable
        private readonly INeteaseProviderService _neteaseProviderService;
        
        [ObservableProperty] private NCPlayList? _playlist;
        [ObservableProperty] private ObservableCollection<NCSong> _songs;
        [ObservableProperty] private string _playlistName;
        [ObservableProperty] private string _description;
        [ObservableProperty] private string _coverUrl;
        [ObservableProperty] private string _creatorName;
        [ObservableProperty] private string _creatorId;
        [ObservableProperty] private long _playCount;
        [ObservableProperty] private long _subscribedCount;
        [ObservableProperty] private long _trackCount;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private Color _albumColor;

        private string _playlistId;
        private CancellationTokenSource _cancellationTokenSource;
#nullable restore

        public SongListDetailViewModel(INeteaseProviderService neteaseProviderService)
        {
            _neteaseProviderService = neteaseProviderService;
            _songs = new ObservableCollection<NCSong>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task InitializeAsync(string playlistId, NCPlayList playlist = null)
        {
            _playlistId = playlistId;
            _playlist = playlist;
            IsLoading = true;

            try
            {
                await LoadPlaylistDetailAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when navigating away
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists("加载歌单信息失败", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadPlaylistDetailAsync()
        {
            try
            {
                // Use NeteaseProviderService as a bridge to NeteaseApi
                var (playlist, songs) = await _neteaseProviderService.GetPlaylistDetailsAsync(_playlistId, _cancellationTokenSource.Token);

                if (playlist == null) return;

                // Map playlist info
                Playlist = playlist;
                PlaylistName = Playlist.name;
                Description = Playlist.desc;
                CoverUrl = Playlist.cover;
                CreatorName = Playlist.creater.name;
                CreatorId = Playlist.creater.id;
                PlayCount = Playlist.playCount;
                SubscribedCount = Playlist.bookCount;
                TrackCount = Playlist.trackCount;

                // Note: For now, songs would need to be loaded separately
                // This demonstrates the pattern - actual implementation would need
                // NeteaseProviderService to handle song loading

                // Load album cover color
                if (!Common.Setting.noImage && !string.IsNullOrEmpty(CoverUrl))
                {
                    _ = LoadAlbumColorAsync();
                }
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists("获取歌单信息失败", ex.Message);
            }
        }



        private async Task LoadAlbumColorAsync()
        {
            try
            {
                using var result = await Common.HttpClient.GetAsync(
                    new Uri(CoverUrl + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER),
                    _cancellationTokenSource.Token);

                if (result.IsSuccessStatusCode)
                {
                    using var stream = await result.Content.ReadAsStreamAsync();
                    using var inputStream = stream.AsRandomAccessStream();
                    AlbumColor = await ColorExtractor.ExtractColorFromStream(inputStream);
                }
            }
            catch
            {
                // Ignore color extraction errors
            }
        }

        [RelayCommand]
        private void PlayAll()
        {
            HyPlayList.RemoveAllSong();
            HyPlayList.AppendNcSongs(Songs.ToList());
            HyPlayList.PlaySourceId = _playlistId;
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }

        [RelayCommand]
        private async Task AddToPlaylist()
        {
            await HyPlayList.AppendNcSongs(Songs.ToList());
            Common.AddToTeachingTipLists("已添加到播放列表");
        }

        [RelayCommand]
        private void Subscribe()
        {
            // Subscribe/unsubscribe functionality
            // This would need to be added to NeteaseProviderService
            Common.AddToTeachingTipLists("功能待实现");
        }

        public void Cleanup()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
