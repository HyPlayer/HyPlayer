using System;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services;

public sealed class AppNavigator : IAppNavigator
{
    private readonly INavigationService _navigation;
    private readonly IPlaylistService _playlist;
    private readonly IAuthService _auth;

    public AppNavigator(INavigationService navigation, IPlaylistService playlist, IAuthService auth)
    {
        _navigation = navigation;
        _playlist = playlist;
        _auth = auth;
    }

    public Task NavigateAsync(AppRoute route) =>
        route switch
        {
            AppRoute.Album album           => NavigatePage(typeof(AlbumPage), album.Id),
            AppRoute.Artist artist         => NavigatePage(typeof(ArtistPage), artist.Id),
            AppRoute.DailyRecommend        => NavigatePage(typeof(SongListDetail), CreateDailyRecommendPlaylist()),
            AppRoute.Favorite              => NavigatePage(typeof(PageFavorite)),
            AppRoute.History               => NavigatePage(typeof(History)),
            AppRoute.Home                  => NavigatePage(typeof(HomePage)),
            AppRoute.LikedSongs            => LikedSongsPage(),
            AppRoute.LocalMusic            => NavigatePage(typeof(LocalMusicPage)),
            AppRoute.Me me                 => NavigatePage(typeof(Me), me.UserId),
            AppRoute.MusicCloud            => NavigatePage(typeof(MusicCloudPage)),
            AppRoute.MV mv                 => NavigatePage(typeof(MVPage), mv.Id),
            AppRoute.Playlist playlist     => NavigatePage(typeof(SongListDetail), playlist.Id),
            AppRoute.Radio radio           => NavigatePage(typeof(RadioPage), radio.Id),
            AppRoute.Settings              => NavigatePage(typeof(Settings)),
            AppRoute.Song song             => PlaySongAsync(song.Id),
            _                                => throw new InvalidOperationException($"Unrecognized route: {route.GetType().Name}")
        };

    private Task NavigatePage(Type pageType, object? parameter = null)
    {
        _navigation.Navigate(pageType, parameter);
        return Task.CompletedTask;
    }

    private Task LikedSongsPage()
    {
        if (_auth.MySongLists.Count > 0)
            _navigation.Navigate(typeof(SongListDetail), _auth.MySongLists[0].PlaylistId);
        return Task.CompletedTask;
    }

    public async Task PlaySongAsync(string songId)
    {
        await AppendAndMoveToAsync(new MusicResource.Song(songId));
    }

    public AppRoute? InferRoute(Type pageType, object? parameter)
    {
        if (pageType == typeof(HomePage)) return new AppRoute.Home();
        if (pageType == typeof(LocalMusicPage)) return new AppRoute.LocalMusic();
        if (pageType == typeof(History)) return new AppRoute.History();
        if (pageType == typeof(PageFavorite)) return new AppRoute.Favorite();
        if (pageType == typeof(MusicCloudPage)) return new AppRoute.MusicCloud();
        if (pageType == typeof(Settings)) return new AppRoute.Settings();
        if (pageType == typeof(Me)) return new AppRoute.Me();
        if (pageType == typeof(AlbumPage)) return new AppRoute.Album(parameter?.ToString() ?? "");
        if (pageType == typeof(ArtistPage)) return new AppRoute.Artist(parameter?.ToString() ?? "");
        if (pageType == typeof(MVPage)) return new AppRoute.MV(parameter?.ToString() ?? "");
        if (pageType == typeof(RadioPage)) return new AppRoute.Radio(parameter?.ToString() ?? "");
        if (pageType == typeof(SongListDetail))
        {
            var playlistId = parameter switch
            {
                string id => id,
                NCPlayList pl => pl.PlaylistId,
                _ => null
            };

            if (!string.IsNullOrEmpty(playlistId))
            {
                if (_auth.MySongLists.Count > 0 && playlistId == _auth.MySongLists[0].PlaylistId?.ToString())
                    return new AppRoute.LikedSongs();
                return new AppRoute.Playlist(playlistId);
            }

            if (parameter is NCPlayList { IsDailyRecommend: true })
                return new AppRoute.DailyRecommend();
        }
        return null;
    }

    public async Task PlayAsync(MusicResource resource)
    {
        _playlist.Clear();
        await AppendAsync(resource);
        await _playlist.MoveNextAsync(true);
    }

    public async Task AppendAsync(MusicResource resource)
    {
        SetPlaybackSource(resource);
        await _playlist.AppendNcSourceAsync(_playlist.PlaySourceId);
    }

    public void SetPlaybackSource(MusicResource resource)
    {
        _playlist.PlaySourceId = resource.ToPlaybackSourceKey();
    }

    private async Task AppendAndMoveToAsync(MusicResource resource)
    {
        var sourceKey = resource.ToPlaybackSourceKey();
        await _playlist.AppendNcSourceAsync(sourceKey);
        var item = _playlist.Items.FirstOrDefault(t => "ns" + t.Id == sourceKey);
        if (item is not null)
            await _playlist.MoveToAsync(item);
    }

    private static NCPlayList CreateDailyRecommendPlaylist() => new()
    {
        Cover = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
        Creator = new NCUser
        {
            Avatar = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
            Id = "1",
            Name = "网易云音乐",
            Signature = "网易云音乐官方账号 "
        },
        IsDailyRecommend = true,
        HasSubscribed = false,
        Name = "每日歌曲推荐",
        Description = "根据你的口味生成，每天6:00更新"
    };
}
