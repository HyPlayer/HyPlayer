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
    public partial class AlbumViewModel : ObservableRecipient, IViewModel
    {
#nullable enable
        private readonly INeteaseProviderService _neteaseProviderService;
        
        [ObservableProperty] private NCAlbum? _album;
        [ObservableProperty] private ObservableCollection<NCSong> _songs;
        [ObservableProperty] private List<NCArtist> _artists;
        [ObservableProperty] private string _albumName;
        [ObservableProperty] private string _albumDescription;
        [ObservableProperty] private string _albumCoverUrl;
        [ObservableProperty] private string _artistNames;
        [ObservableProperty] private string _publishTime;
        [ObservableProperty] private int _songCount;
        [ObservableProperty] private long _playCount;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private Color _albumColor;

        private string _albumId;
        private CancellationTokenSource _cancellationTokenSource;
#nullable restore

        public AlbumViewModel(INeteaseProviderService neteaseProviderService)
        {
            _neteaseProviderService = neteaseProviderService;
            _songs = new ObservableCollection<NCSong>();
            _artists = new List<NCArtist>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task InitializeAsync(string albumId, NCAlbum album = null)
        {
            _albumId = albumId;
            _album = album;
            IsLoading = true;

            try
            {
                await LoadAlbumInfoAsync();
                await LoadAlbumDynamicAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when navigating away
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists("加载专辑信息失败", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadAlbumInfoAsync()
        {
            try
            {
                // Use NeteaseProviderService as a bridge to NeteaseApi
                var (album, songs) = await _neteaseProviderService.GetAlbumDetailsAsync(_albumId, _cancellationTokenSource.Token);

                if (album == null || songs == null) return;

                // Map album info
                Album = album;
                AlbumName = Album.name;
                AlbumDescription = Album.description;
                AlbumCoverUrl = Album.cover;
                Artists = songs.FirstOrDefault()?.Artist ?? new List<NCArtist>();
                ArtistNames = string.Join(" / ", Artists.Select(a => a.name));
                SongCount = songs.Count;

                // Map songs
                Songs.Clear();
                for (int i = 0; i < songs.Count; i++)
                {
                    songs[i].Order = i;
                    Songs.Add(songs[i]);
                }

                // Load album cover color
                if (!Common.Setting.noImage && !string.IsNullOrEmpty(AlbumCoverUrl))
                {
                    _ = LoadAlbumColorAsync();
                }
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists("获取专辑信息失败", ex.Message);
            }
        }

        private async Task LoadAlbumDynamicAsync()
        {
            // Load dynamic info like play count
            // For now, skip this as it requires additional API calls
            // Can be implemented later if needed
            await Task.CompletedTask;
        }

        private async Task LoadAlbumColorAsync()
        {
            try
            {
                using var result = await Common.HttpClient.GetAsync(
                    new Uri(AlbumCoverUrl + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER),
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
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }

        [RelayCommand]
        private async Task AddToPlaylist()
        {
            await HyPlayList.AppendNcSongs(Songs.ToList());
            Common.AddToTeachingTipLists("已添加到播放列表");
        }

        public void Cleanup()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
