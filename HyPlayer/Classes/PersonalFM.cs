#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using System;
using System.Threading.Tasks;

#endregion

namespace HyPlayer.Classes;

/// <summary>
/// 私人 FM 控制器。
/// <para>
/// 保留静态入口 <see cref="InitPersonalFM"/> / <see cref="ExitFm"/> 以兼容现有调用方，
/// 内部通过 <see cref="Ioc.Default"/> 解析 DI 服务，不再直接引用 HyPlayList。
/// </para>
/// </summary>
internal sealed class PersonalFM : IRecipient<TrackEndedMessage>
{
    private static PersonalFM? _instance;
    private static bool _isNew = true;

    private readonly IPlaylistService _playlist;
    private readonly PlaybackStateService _state;

    private PersonalFM()
    {
        _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    }

    // ---------------------------------------------------------------
    //  Static entry points (backward-compatible)
    // ---------------------------------------------------------------

    public static void InitPersonalFM()
    {
        // Tear down any previous instance first
        CleanupInstance();

        var fm = new PersonalFM();
        _instance = fm;

        fm._playlist.SetStrategy("pfm");
        fm._state.IsInFm = true;
        fm._playlist.Clear();

        // Subscribe to track-ended via messenger
        WeakReferenceMessenger.Default.Register<TrackEndedMessage>(fm);

        fm.LoadNextFM().SafeFireAndForget();
    }

    public static void ExitFm()
    {
        CleanupInstance();
    }

    /// <summary>
    /// 静态入口：加载下一首 FM 歌曲（供旧代码调用）
    /// </summary>
    public static void LoadNextFMStatic()
    {
        _instance?.LoadNextFM().SafeFireAndForget();
    }

    // ---------------------------------------------------------------
    //  Messenger handler
    // ---------------------------------------------------------------

    void IRecipient<TrackEndedMessage>.Receive(TrackEndedMessage message)
    {
        if (_state.IsInFm)
            LoadNextFM().SafeFireAndForget();
    }

    // ---------------------------------------------------------------
    //  Core logic
    // ---------------------------------------------------------------

    public async Task LoadNextFM()
    {
        if (_playlist.NowPlayingIndex + 1 >= _playlist.Items.Count)
        {
            var finalIndex = Math.Max(_playlist.Items.Count - 1, 0);
            if (!Common.Setting.useAiDj)
            {
                var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PersonalFmApi);
                if (result.IsError || result.Value?.Items?.Length is not > 0)
                {
                    Common.AddToTeachingTipLists("加载私人 FM错误", result.Error?.Message ?? "未知错误");
                    return;
                }

                foreach (var personalFmDataItem in result.Value.Items)
                {
                    var hpi = NCSongToPlayItem(personalFmDataItem.MapToNcSong());
                    _playlist.AppendItem(hpi);
                }
            }
            else
            {
                // AIDJ
                var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.AiDjContentRcmdInfoApi,
                    new AiDjContentRcmdInfoRequest
                    {
                        IsNewToAidj = _isNew
                    });
                _isNew = false;
                if (result.IsError || result.Value?.Data?.AiDjResources?.Length is not > 0)
                {
                    Common.AddToTeachingTipLists("加载私人 FM错误", result.Error?.Message ?? "未知错误");
                    return;
                }

                foreach (var aiDjContentRcmdInfoResource in result.Value.Data.AiDjResources)
                {
                    if (aiDjContentRcmdInfoResource is AiDjContentRcmdInfoResponse.AiDjContentRcmdInfoData.AiDjContentRcmdAudioResource audioValue)
                    {
                        foreach (var audioItem in audioValue.Value?.Audio ?? [])
                        {
                            var playItem = new HyPlayItem()
                            {
                                ItemType = HyPlayItemType.Netease,
                                Album = new NCAlbum
                                {
                                    AlbumType = HyPlayItemType.Netease,
                                    Alias = "私人 DJ",
                                    Cover =
                                            "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                    Description = "私人 DJ",
                                    Id = "126368130",
                                    Name = "私人 DJ 推荐语"
                                },
                                Artist =
                                    [
                                        new NCArtist()
                                                {
                                                    Alias = "私人 DJ",
                                                    Avatar =
                                                        "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                                    Id = "1",
                                                    Name = "私人 DJ",
                                                    TranslatedName = null,
                                                    Type = HyPlayItemType.Netease
                                                }
                                    ],
                                Bitrate = 0,
                                CDName = null,
                                Id = "-1",
                                IsLocalFile = false,
                                LengthInMilliseconds = audioItem.Duration,
                                Name = "私人 DJ 推荐语",
                                InfoTag = "私人 DJ",
                                Url = audioItem.Url,
                                Size = audioItem.Size ?? 0
                            };
                            _playlist.AppendItem(playItem);
                        }
                    }
                    else if (aiDjContentRcmdInfoResource is AiDjContentRcmdInfoResponse.AiDjContentRcmdInfoData.AiDjContentRcmdAudioSong songValue)
                    {
                        var ncSong = songValue.Value?.SongName?.MapToNcSong();
                        if (ncSong is not null)
                        {
                            var hpi = NCSongToPlayItem(ncSong);
                            _playlist.AppendItem(hpi);
                        }
                    }
                }
            }

            _playlist.NotifyAppendDone();
            if (_playlist.Items.Count > finalIndex)
            {
                var item = _playlist.Items[finalIndex];
                await _playlist.MoveToAsync(item);
            }
        }

        _state.IsInFm = true;
    }

    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------

    private static void CleanupInstance()
    {
        if (_instance is not null)
        {
            WeakReferenceMessenger.Default.Unregister<TrackEndedMessage>(_instance);
            _instance._state.IsInFm = false;
            _instance._playlist.Clear();
            _instance = null;
        }
    }

    /// <summary>
    /// Convert an <see cref="NCSong"/> to a <see cref="HyPlayItem"/>.
    /// Pure data mapping — no side effects.
    /// </summary>
    private static HyPlayItem NCSongToPlayItem(NCSong ncSong)
    {
        return new HyPlayItem
        {
            ItemType = ncSong.Type,
            InfoTag = ncSong.Alias,
            Album = ncSong.Album,
            Artist = ncSong.Artist,
            Id = ncSong.SongId,
            Translation = ncSong.TranslatedName,
            Name = ncSong.SongName,
            TrackId = ncSong.TrackId,
            CDName = ncSong.CDName,
            LengthInMilliseconds = ncSong.LengthInMilliseconds
        };
    }
}
