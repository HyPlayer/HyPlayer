using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.Infrastructure.Network;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
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
    private readonly IAuthenticationProvidable _authenticationProvider;
    private readonly IQrAuthenticationProvidable _qrAuthenticationProvider;
    private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider;
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly INotificationService _notification;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly IPlaylistCollectionChangeNotifier _playlistCollectionChangeNotifier;
    private readonly SemaphoreSlim _likeSongGate = new(1, 1);

    public AuthService(
        PlaybackStateService state,
        IAuthenticationProvidable authenticationProvider,
        IQrAuthenticationProvidable qrAuthenticationProvider,
        global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider,
        IProvidableItemProvidable itemProvider,
        INotificationService notification,
        IBackgroundTaskRunner taskRunner,
        IPlaylistCollectionChangeNotifier playlistCollectionChangeNotifier)
    {
        _state = state;
        _authenticationProvider = authenticationProvider;
        _qrAuthenticationProvider = qrAuthenticationProvider;
        _neteaseProvider = neteaseProvider;
        _itemProvider = itemProvider;
        _notification = notification;
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
        _neteaseProvider.ClearRuntimeCookies();
    }

    /// <inheritdoc />
    public void SetRuntimeCookie(string name, string value)
    {
        _neteaseProvider.SetRuntimeCookie(name, value);
    }

    /// <inheritdoc />
    public async Task<AuthResult> TryLoadSavedLoginAsync()
    {
        try
        {
            if (!Setting.LoadCookies() && !_neteaseProvider.HasAdditionalCookies)
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
            string countryCode = string.Empty;
            if (account.StartsWith('+'))
            {
                int spaceIdx = account.IndexOf(' ');
                countryCode = account[1..spaceIdx];
                account = account[(spaceIdx + 1)..];
            }
            if (!string.IsNullOrEmpty(countryCode) && countryCode != "86")
                return new AuthResult(false, "Provider login currently supports the default phone country code only.");

            var sessionInfo = await _authenticationProvider.LoginAsync(account, password);
            if (!sessionInfo.IsAuthenticated)
                return new AuthResult(false, "登录失败");

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
        try
        {
            var challenge = await _qrAuthenticationProvider.CreateQrLoginChallengeAsync();
            return new AuthQrKeyResult(true, challenge.ChallengeId);
        }
        catch (Exception ex)
        {
            return new AuthQrKeyResult(false, ErrorMessage: ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<AuthQrCheckResult> CheckQrLoginAsync(string key)
    {
        try
        {
            var state = await _qrAuthenticationProvider.GetQrLoginStateAsync(key);
            return new AuthQrCheckResult(MapQrStatusCode(state.Status), state.Message);
        }
        catch (Exception ex)
        {
            return new AuthQrCheckResult(0, ex.Message);
        }
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
            await _authenticationProvider.AnnounceDeviceAsync(deviceInfo.FriendlyName);
            return new AuthDeviceRegisterResult(true, deviceId.ToString("N"));
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
            var statusResult = await _authenticationProvider.GetSessionInfoAsync();
            if (!statusResult.IsAuthenticated)
                return null;
            return statusResult;
        });

        if (result is not { IsAuthenticated: true })
            return new AuthResult(false);

        Setting.SaveCookies();

        var providerUser = await TryGetCurrentProviderUserAsync();
        CurrentUser = providerUser is not null
            ? await MapProviderUserAsync(providerUser)
            : new NCUser
            {
                Avatar = "ms-appx:///Assets/icon.png",
                Id = result.UserId ?? string.Empty,
                Name = result.DisplayName ?? "已登录",
                Signature = string.Empty
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
        await _authenticationProvider.LogoutAsync();
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
                        if (isLiked)
                            await _neteaseProvider.UnlikeProvidableItemAsync(item.Id, null);
                        else
                            await _neteaseProvider.LikeProvidableItemAsync(item.Id, null);
                        if (isLiked) LikedSongs.Remove(item.Id);
                        else LikedSongs.Add(item.Id);
                        SongLikeStatusChanged?.Invoke(this, new SongLikeStatusChangedEventArgs(!isLiked));
                        break;
                    case HyPlayItemType.Radio:
                        _notification.ShowMessage("暂不支持红心电台歌曲", "将在后续版本中支持");
                        SongLikeStatusChanged?.Invoke(this, new SongLikeStatusChangedEventArgs(!isLiked));
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("红心操作失败", ex.Message);
        }
    }

    private async Task LoadMyLikelistAsync()
    {
        var likedSongs = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "likedSongs", async () =>
            await _neteaseProvider.GetLikedProvidableIdsAsync("sg"));
        LikedSongs.Clear();
        LikedSongs.AddRange(likedSongs ?? []);
    }

    private async Task<NeteaseUser?> TryGetCurrentProviderUserAsync()
    {
        var session = await _authenticationProvider.GetSessionInfoAsync();
        return string.IsNullOrWhiteSpace(session.UserId)
            ? null
            : await _itemProvider.GetProvidableItemByIdAsync(HyPlayer.NeteaseProvider.Constants.NeteaseTypeIds.User + session.UserId) as NeteaseUser;
    }

    private static async Task<NCUser> MapProviderUserAsync(NeteaseUser user)
    {
        return new NCUser
        {
            Id = user.ActualId ?? string.Empty,
            Name = user.Name,
            Signature = user.Description ?? string.Empty,
            Avatar = await GetProviderUserAvatarAsync(user) ?? string.Empty
        };
    }

    private static async Task<string?> GetProviderUserAvatarAsync(NeteaseUser user)
    {
        var resource = await user.GetCoverAsync();
        if (resource is IResourceResultOf<Uri?> uriResource)
            return (await uriResource.GetResourceAsync())?.ToString();
        return user.AvatarUrl;
    }

    private static int MapQrStatusCode(ProviderQrLoginStatus status)
    {
        return status switch
        {
            ProviderQrLoginStatus.Authorized => 803,
            ProviderQrLoginStatus.Expired => 800,
            ProviderQrLoginStatus.WaitingForScan => 801,
            ProviderQrLoginStatus.WaitingForConfirmation => 802,
            _ => 0
        };
    }
}
