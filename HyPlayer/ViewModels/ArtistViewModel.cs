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

namespace HyPlayer.ViewModels
{
    public partial class ArtistViewModel : ObservableRecipient, IViewModel
    {
#nullable enable
        private readonly INeteaseProviderService _neteaseProviderService;
        
        [ObservableProperty] private NCArtist? _artist;
        [ObservableProperty] private ObservableCollection<NCSong> _hotSongs;
        [ObservableProperty] private ObservableCollection<NCSong> _allSongs;
        [ObservableProperty] private ObservableCollection<NCAlbum> _albums;
        [ObservableProperty] private string _artistName;
        [ObservableProperty] private string _artistAlias;
        [ObservableProperty] private string _artistAvatarUrl;
        [ObservableProperty] private string _artistBriefDesc;
        [ObservableProperty] private int _musicSize;
        [ObservableProperty] private int _albumSize;
        [ObservableProperty] private int _mvSize;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _songHasMore;

        private string _artistId;
        private int _songPage;
        private CancellationTokenSource _cancellationTokenSource;
#nullable restore

        public ArtistViewModel(INeteaseProviderService neteaseProviderService)
        {
            _neteaseProviderService = neteaseProviderService;
            _hotSongs = new ObservableCollection<NCSong>();
            _allSongs = new ObservableCollection<NCSong>();
            _albums = new ObservableCollection<NCAlbum>();
            _cancellationTokenSource = new CancellationTokenSource();
            _songPage = 0;
        }

        public async Task InitializeAsync(string artistId)
        {
            _artistId = artistId;
            IsLoading = true;

            try
            {
                await LoadArtistDetailAsync();
                await Task.WhenAll(
                    LoadHotSongsAsync(),
                    LoadAlbumsAsync(),
                    LoadAllSongsAsync()
                );
            }
            catch (OperationCanceledException)
            {
                // Expected when navigating away
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists("加载艺人信息失败", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadArtistDetailAsync()
        {
            try
            {
                // Use NeteaseProviderService as a bridge to NeteaseApi
                var artist = await _neteaseProviderService.GetArtistDetailsAsync(_artistId, _cancellationTokenSource.Token);

                if (artist == null) return;

                // Map artist info
                Artist = artist;
                ArtistName = Artist.name;
                ArtistAlias = Artist.alias;
                ArtistAvatarUrl = Artist.avatar;
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists("获取艺人信息失败", ex.Message);
            }
        }

        private async Task LoadHotSongsAsync()
        {
            try
            {
                // Use NeteaseProviderService as a bridge to NeteaseApi
                var songs = await _neteaseProviderService.GetArtistHotSongsAsync(_artistId, _cancellationTokenSource.Token);

                if (songs != null)
                {
                    HotSongs.Clear();
                    for (int i = 0; i < songs.Take(50).Count(); i++)
                    {
                        songs[i].Order = i;
                        HotSongs.Add(songs[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore errors for hot songs
            }
        }

        private async Task LoadAlbumsAsync()
        {
            try
            {
                // Use NeteaseProviderService as a bridge to NeteaseApi
                var albums = await _neteaseProviderService.GetArtistAlbumsAsync(_artistId, 50, _cancellationTokenSource.Token);

                if (albums != null)
                {
                    Albums.Clear();
                    foreach (var album in albums)
                    {
                        Albums.Add(album);
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore errors for albums
            }
        }

        private async Task LoadAllSongsAsync()
        {
            await LoadMoreSongsAsync();
        }

        [RelayCommand]
        private async Task LoadMoreSongs()
        {
            await LoadMoreSongsAsync();
        }

        private async Task LoadMoreSongsAsync()
        {
            // For now, just use hot songs as all songs
            // This can be extended later with additional API methods in NeteaseProviderService
            if (_songPage == 0 && HotSongs.Any())
            {
                AllSongs.Clear();
                foreach (var song in HotSongs)
                {
                    AllSongs.Add(song);
                }
                _songPage++;
            }
            SongHasMore = false;
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void PlayHotSongs()
        {
            HyPlayList.RemoveAllSong();
            HyPlayList.AppendNcSongs(HotSongs.ToList());
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }

        [RelayCommand]
        private void PlayAllSongs()
        {
            HyPlayList.RemoveAllSong();
            HyPlayList.AppendNcSongs(AllSongs.ToList());
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }

        public void Cleanup()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
