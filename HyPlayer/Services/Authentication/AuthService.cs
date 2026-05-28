using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.Infrastructure.Network;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Login;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Utils;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Playback;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;

namespace HyPlayer.Services.Authentication;

/// <summary>
/// 认证服务实现，管理用户登录状态与收藏数据
/// </summary>
public class AuthService : IAuthService
{
    public event EventHandler? LoginCompleted;
    public event EventHandler<SongLikeStatusChangedEventArgs>? SongLikeStatusChanged;

    private readonly PlaybackStateService _state;
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly ITeachingTipService _teachingTipService;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly IPlaylistCollectionChangeNotifier _playlistCollectionChangeNotifier;
    private readonly SemaphoreSlim _likeSongGate = new(1, 1);

    public AuthService(
        PlaybackStateService state,
        NeteaseCloudMusicApiHandler api,
        ITeachingTipService teachingTipService,
        IBackgroundTaskRunner taskRunner,
        IPlaylistCollectionChangeNotifier playlistCollectionChangeNotifier)
    {
        _state = state;
        _api = api;
        _teachingTipService = teachingTipService;
        _taskRunner = taskRunner;
        _playlistCollectionChangeNotifier = playlistCollectionChangeNotifier;
    }

    /// <inheritdoc />
    public bool IsLoggedIn { get; set; }

    /// <inheritdoc />
    public NCUser? CurrentUser { get; set; }

    /// <inheritdoc />
    public List<string> LikedSongs { get; } = [];

    /// <inheritdoc />
    public List<NCPlayList> MySongLists { get; } = [];

    /// <inheritdoc />
    public void ClearRuntimeCookies()
    {
        _api.Option.Cookies.Clear();
    }

    /// <inheritdoc />
    public void SetRuntimeCookie(string name, string value)
    {
        _api.Option.Cookies[name] = value;
    }

    /// <inheritdoc />
    public async Task<AuthResult> TryLoadSavedLoginAsync()
    {
        try
        {
            if (!Setting.LoadCookies() && _api.Option.AdditionalParameters.Cookies.Count == 0)
                return new AuthResult(false);

            return await CompleteLoginAsync(false);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> LoginWithPasswordAsync(string account, string password)
    {
        try
        {
            bool isPhone = System.Text.RegularExpressions.Regex.IsMatch(account, "^[0-9]+$");
            string contryCode = string.Empty;
            if (account.StartsWith('+'))
            {
                isPhone = true;
                int spaceIdx = account.IndexOf(' ');
                contryCode = account[1..spaceIdx];
                account = account[(spaceIdx + 1)..];
            }

            if (isPhone)
            {
                var response = await _api.RequestAsync(NeteaseApis.LoginCellphoneApi,
                    new LoginCellphoneRequest
                    {
                        Cellphone = account,
                        CountryCode = string.IsNullOrEmpty(contryCode) ? null : contryCode,
                        Password = password
                    });
                if (response.IsError)
                    return new AuthResult(false, response.Error.Message);
            }
            else
            {
                var response = await _api.RequestAsync(NeteaseApis.LoginEmailApi,
                    new LoginEmailRequest { Email = account, Password = password });
                if (response.IsError)
                    return new AuthResult(false, response.Error.Message);
            }

            return await CompleteLoginAsync(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.ToString());
        }
    }

    /// <inheritdoc />
    public async Task<AuthQrKeyResult> CreateQrLoginKeyAsync()
    {
        var key = await _api.RequestAsync(NeteaseApis.LoginQrCodeUnikeyApi, new LoginQrCodeUnikeyRequest());
        return key.IsError
            ? new AuthQrKeyResult(false, ErrorMessage: key.Error.Message)
            : new AuthQrKeyResult(true, key.Value.Unikey);
    }

    /// <inheritdoc />
    public async Task<AuthQrCheckResult> CheckQrLoginAsync(string key)
    {
        var res = await _api.RequestAsync(NeteaseApis.LoginQrCodeCheckApi,
            new LoginQrCodeCheckRequest { Unikey = key });
        return res.IsError && res.Value.Code != 803
            ? new AuthQrCheckResult(res.Value.Code, res.Error?.Message)
            : new AuthQrCheckResult(res.Value.Code);
    }

    /// <inheritdoc />
    public async Task<AuthDeviceRegisterResult> RegisterCurrentDeviceAsync()
    {
        try
        {
            var deviceInfo = new EasClientDeviceInformation();
            var deviceId = deviceInfo.Id;
            var androidId = deviceId.ToString("N")[..16];
            var imei = deviceId.ToString("N")[16..];
            var rst = await _api.RequestAsync(NeteaseApis.LoginAnnounceDeviceApi, new LoginAnnounceDeviceRequest
            {
                Imei = imei,
                AndroidId = androidId,
                LocalId = null,
                DeviceName = deviceInfo.FriendlyName,
            });
            return rst.IsError
                ? new AuthDeviceRegisterResult(false, ErrorMessage: rst.Error.Message)
                : new AuthDeviceRegisterResult(true, rst.Value.Data?.Id?.ToString());
        }
        catch (Exception ex)
        {
            return new AuthDeviceRegisterResult(false, ErrorMessage: ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> CompleteLoginAsync(bool clearLoginCache)
    {
        if (clearLoginCache)
            await SimpleCacher.ClearCacheAsync(CacheType.Login);

        var result = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "userStatus", async () =>
        {
            var statusResult = await _api.RequestAsync(NeteaseApis.LoginStatusApi);
            if (statusResult.IsError)
            {
                _teachingTipService.Items.Enqueue(new("登录失败", statusResult.Error?.Message));
                return null;
            }
            return statusResult.Value;
        });

        if (result?.Account == null)
            return new AuthResult(false);

        Setting.SaveCookies();

        CurrentUser = result.Profile != null
            ? result.Profile.MapToNcUser()
            : new NCUser
            {
                Avatar = "ms-appx:///Assets/icon.png",
                Id = result.Account.Id,
                Name = result.Account.UserName,
                Signature = "此账号未进行手机号验证, 请使用网易云音乐客户端登录后再继续操作"
            };

        IsLoggedIn = true;

        _taskRunner.Forget(LoadMyLikelistAsync(), "load liked songs after login");
        _playlistCollectionChangeNotifier.NotifyChanged();
        NotifyLoginCompleted();

        return new AuthResult(true);
    }

    /// <inheritdoc />
    public async Task<AuthResult> LogoutAsync()
    {
        IsLoggedIn = false;
        CurrentUser = null;
        LikedSongs.Clear();
        MySongLists.Clear();

        if (ApplicationData.Current.LocalSettings.Containers.TryGetValue("Cookies", out var container))
            container.Values.Clear();
        _api.Option.Cookies.Clear();
        Setting.SaveCookies();

        try
        {
            await SimpleCacher.ClearCacheAsync(CacheType.Login);
            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    /// <inheritdoc />
    public void NotifyLoginCompleted()
    {
        LoginCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void LikeSong()
    {
        _taskRunner.Forget(LikeSongAsync, "toggle current song like status");
    }

    /// <inheritdoc />
    public async Task LikeSongAsync()
    {
        await _likeSongGate.WaitAsync();
        try
        {
            await LikeSongCoreAsync();
        }
        finally
        {
            _likeSongGate.Release();
        }
    }

    private async Task LikeSongCoreAsync()
    {
        var item = _state.NowPlayingItem;
        if (item == null) return;
        var isLiked = LikedSongs.Contains(item.Id);
        try
        {
            await RetryPolicies.ApiCallPolicy.ExecuteAsync(async () =>
            {
                switch (item.ItemType)
                {
                    case HyPlayItemType.Netease:
                        bool res = await Api.LikeSong(item.Id, !isLiked);
                        if (res)
                        {
                            if (isLiked) LikedSongs.Remove(item.Id);
                            else LikedSongs.Add(item.Id);
                            SongLikeStatusChanged?.Invoke(this, new SongLikeStatusChangedEventArgs(!isLiked));
                        }
                        else throw new Exception("红心操作失败");
                        break;
                    case HyPlayItemType.Radio:
                        _teachingTipService.Items.Enqueue(new("暂不支持红心电台歌曲", "将在后续版本中支持"));
                        SongLikeStatusChanged?.Invoke(this, new SongLikeStatusChangedEventArgs(!isLiked));
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            _teachingTipService.Items.Enqueue(new("红心操作失败", ex.Message));
        }
    }

    private async Task LoadMyLikelistAsync()
    {
        var ids = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "likedSongs", async () =>
        {
            var js = await _api.RequestAsync(NeteaseApis.LikelistApi,
                new LikelistRequest { Uid = CurrentUser!.Id });
            if (js.IsError)
            {
                _teachingTipService.Items.Enqueue(new("获取喜欢列表失败", js.Error?.Message));
                return null;
            }
            return js.Value;
        });

        var likedSongs = ids?.TrackIds?.ToList() ?? [];
        LikedSongs.Clear();
        LikedSongs.AddRange(likedSongs);
    }
}
