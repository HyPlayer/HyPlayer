using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Network;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;

namespace HyPlayer.Features.Account.Services;

/// <summary>
///     认证服务实现，管理用户登录状态与收藏数据
/// </summary>
public class AuthService : IAuthService
{
    private readonly IProviderAdditionalConfigurationProvidable _additionalConfigurationProvider;
    private readonly IAuthenticationProvidable _authenticationProvider;
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly IProvableItemLikable _likableProvider;
    private readonly SemaphoreSlim _likeSongGate = new(1, 1);
    private readonly INotificationService _notification;
    private readonly IPlaylistCollectionChangeNotifier _playlistCollectionChangeNotifier;
    private readonly IQrAuthenticationProvidable _qrAuthenticationProvider;

    private readonly PlaybackStateService _state;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly IUserLibraryStateService _userLibraryState;

    public AuthService(
        PlaybackStateService state,
        IAuthenticationProvidable authenticationProvider,
        IQrAuthenticationProvidable qrAuthenticationProvider,
        IProvableItemLikable likableProvider,
        IProviderAdditionalConfigurationProvidable additionalConfigurationProvider,
        IProviderKnownTypeIds knownTypeIds,
        IProvidableItemProvidable itemProvider,
        INotificationService notification,
        IBackgroundTaskRunner taskRunner,
        IPlaylistCollectionChangeNotifier playlistCollectionChangeNotifier,
        IUserLibraryStateService userLibraryState)
    {
        _state = state;
        _authenticationProvider = authenticationProvider;
        _qrAuthenticationProvider = qrAuthenticationProvider;
        _likableProvider = likableProvider;
        _additionalConfigurationProvider = additionalConfigurationProvider;
        _knownTypeIds = knownTypeIds;
        _itemProvider = itemProvider;
        _notification = notification;
        _taskRunner = taskRunner;
        _playlistCollectionChangeNotifier = playlistCollectionChangeNotifier;
        _userLibraryState = userLibraryState;
    }

    public event EventHandler? LoginCompleted;
    public event EventHandler<SongLikeStatusChangedEventArgs>? SongLikeStatusChanged;

    /// <inheritdoc />
    public bool IsLoggedIn { get; set; }

    /// <inheritdoc />
    public PersonBase? CurrentUser { get; set; }

    /// <inheritdoc />
    public List<string> LikedSongs { get; } = [];

    /// <inheritdoc />
    public Task ClearRuntimeCookiesAsync()
    {
        return _authenticationProvider.ImportSessionAsync(new Dictionary<string, string>());
    }

    /// <inheritdoc />
    public Task ImportRuntimeCookiesAsync(IReadOnlyDictionary<string, string> cookies)
    {
        return _authenticationProvider.ImportSessionAsync(cookies);
    }

    /// <inheritdoc />
    public async Task<AuthResult> TryLoadSavedLoginAsync()
    {
        try
        {
            var sessionValues = Setting.LoadCookies();
            if (sessionValues.Count == 0 && !_additionalConfigurationProvider.HasAdditionalConfiguration)
                return new AuthResult(false);

            if (sessionValues.Count > 0)
                await _authenticationProvider.ImportSessionAsync(sessionValues);
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
            var countryCode = string.Empty;
            if (account.StartsWith('+'))
            {
                var spaceIdx = account.IndexOf(' ');
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
        }, forceRefresh: clearLoginCache || _additionalConfigurationProvider.HasAdditionalConfiguration);

        if (result is not { IsAuthenticated: true })
            return new AuthResult(false);

        Setting.SaveCookies(await _authenticationProvider.ExportSessionAsync());

        var providerUser = await TryGetCurrentProviderUserAsync(result);
        CurrentUser = providerUser is not null
            ? providerUser
            : new ProviderSessionPerson(_knownTypeIds.UserTypeId)
            {
                ActualId = result.UserId ?? string.Empty,
                Name = result.DisplayName ?? "已登录",
                Description = string.Empty
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
        _userLibraryState.Clear();

        if (ApplicationData.Current.LocalSettings.Containers.TryGetValue("Cookies", out var container))
            container.Values.Clear();
        await _authenticationProvider.LogoutAsync();
        Setting.SaveCookies(await _authenticationProvider.ExportSessionAsync());

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
        var providerItem = _state.NowPlayingProviderItem;
        var songId = NormalizeSongActualId(providerItem?.ActualId);
        if (string.IsNullOrWhiteSpace(songId)) return;
        var isLiked = LikedSongs.Contains(songId);
        try
        {
            await RetryPolicies.ApiCallPolicy.ExecuteAsync(async () =>
            {
                if (providerItem.TypeId != _knownTypeIds.SingleSongTypeId)
                {
                    _notification.ShowMessage("暂不支持红心此内容", "当前 provider 未将该内容声明为单曲");
                    SongLikeStatusChanged?.Invoke(this, new SongLikeStatusChangedEventArgs(!isLiked));
                    return;
                }

                var itemId = providerItem.TypeId + songId;
                if (isLiked)
                    await _likableProvider.UnlikeProvidableItemAsync(itemId, null);
                else
                    await _likableProvider.LikeProvidableItemAsync(itemId, null);
                if (isLiked) LikedSongs.Remove(songId);
                else LikedSongs.Add(songId);
                SongLikeStatusChanged?.Invoke(this, new SongLikeStatusChangedEventArgs(!isLiked));
            });
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("红心操作失败", ex.Message);
        }
    }

    private async Task LoadMyLikelistAsync()
    {
        var userId = CurrentUser?.ActualId;
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var likedSongs = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"likedSongs_{userId}", async () =>
            await _likableProvider.GetLikedProvidableIdsAsync(_knownTypeIds.SingleSongTypeId));
        LikedSongs.Clear();
        LikedSongs.AddRange((likedSongs ?? [])
            .Select(NormalizeSongActualId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct());
    }

    private string? NormalizeSongActualId(string? providerItemId)
    {
        if (string.IsNullOrWhiteSpace(providerItemId))
            return providerItemId;

        return providerItemId.StartsWith(_knownTypeIds.SingleSongTypeId, StringComparison.Ordinal)
            ? providerItemId[_knownTypeIds.SingleSongTypeId.Length..]
            : providerItemId;
    }

    private async Task<PersonBase?> TryGetCurrentProviderUserAsync(ProviderSessionInfo session)
    {
        return string.IsNullOrWhiteSpace(session.UserId)
            ? null
            : await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.UserTypeId + session.UserId) as PersonBase;
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

    private sealed class ProviderSessionPerson(string typeId) : PersonBase, IHasDescription
    {
        public override string ProviderId => string.Empty;
        public override string TypeId => typeId;
        public string? Description { get; set; }

        public override Task<List<ContainerBase>> GetSubContainerAsync(CancellationToken ctk = default)
        {
            return Task.FromResult(new List<ContainerBase>());
        }
    }
}