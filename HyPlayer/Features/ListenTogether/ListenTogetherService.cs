using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Playback.MediaSources;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.ListenTogether;

public enum ListenTogetherPhase
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
    Ending
}

public enum ListenTogetherTakeoverChoice
{
    Cancel,
    TakeOver,
    End
}

public sealed record ListenTogetherPromptRequest(
    string Title,
    string Description,
    string ContinueText,
    string EndText,
    string IgnoreText);

public interface IListenTogetherPrompt
{
    Task<ListenTogetherTakeoverChoice> AskAsync(ListenTogetherPromptRequest request);
}

public interface IListenTogetherService : INotifyPropertyChanged
{
    ListenTogetherPhase Phase { get; }
    bool IsInRoom { get; }
    bool IsIdle { get; }
    bool IsDisconnected { get; }
    bool IsHost { get; }
    string RoomId { get; }
    string ShareLink { get; }
    string StatusMessage { get; }
    IReadOnlyList<ProviderListenTogetherUser> Users { get; }
    bool HasExistingRoom { get; }
    bool CanCreate { get; }
    string ExistingRoomText { get; }
    bool IsMulti { get; }
    bool IsDualRoom { get; }
    bool CanRequestSkip { get; }
    IReadOnlyList<ProviderMultiListenTogetherQueueItem> MultiQueue { get; }
    Task RefreshExistingRoomAsync();
    Task TakeOverAsync();
    Task EndExistingRoomAsync();
    Task CreateRoomAsync();
    Task CreateMultiRoomAsync();
    Task RecommendAsync(SingleSongBase song);
    Task RequestSkipAsync();
    Task JoinRoomAsync(ListenTogetherInvitation invitation);
    Task<ListenTogetherInvitation?> ResolveInvitationAsync(string text);
    Task ReconnectAsync();
    Task EndRoomAsync();
}

internal sealed partial class ListenTogetherService : ObservableObject, IListenTogetherService, IPlaybackRestrictionPolicy
{
    private const string NeteaseProviderId = "ncm";
    private const string NonSongMessage = "当前正处于一起听模式，无法播放除普通歌曲以外的音乐";
    private const string SpeedLockedMessage = "一起听期间不能调整播放速度";
    private const int MaxConsecutiveFailures = 5;
    private const int MissingStatusLimit = 2;
    private const int SentSeqMemory = 64;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CorrectionInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MembershipGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RemoteSettleWindow = TimeSpan.FromSeconds(10);

    private static readonly HttpClient ShortLinkClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly string[] AllProperties =
    [
        nameof(Phase), nameof(IsInRoom), nameof(IsIdle), nameof(IsDisconnected), nameof(IsHost),
        nameof(RoomId), nameof(ShareLink), nameof(StatusMessage), nameof(Users), nameof(HasExistingRoom),
        nameof(CanCreate), nameof(ExistingRoomText), nameof(IsMulti), nameof(IsDualRoom), nameof(CanRequestSkip),
        nameof(MultiQueue)
    ];

    private readonly IListenTogetherSyncProvidable _provider;
    private readonly IMultiListenTogetherProvidable _multiProvider;
    private readonly IProviderKnownTypeIds _typeIds;
    private readonly IPlaybackControlService _control;
    private readonly PlaybackStateService _state;
    private readonly PlayCoreBase _playCore;
    private readonly PlaybackSettings _settings;
    private readonly IAuthService _auth;
    private readonly IUIThreadDispatcher _ui;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly INotificationService _notification;
    private readonly PlaybackRestriction _restriction;
    private readonly NeteaseSongUrlResolver _urlResolver;
    private readonly IListenTogetherPrompt _prompt;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Session? _session;
    private long _seekVersion;
    private int _detecting;
    private volatile ListenTogetherPhase _phase;
    private volatile bool _isHost;
    private volatile string _roomId = string.Empty;
    private volatile string _shareLink = string.Empty;
    private volatile string _statusMessage = string.Empty;
    private volatile IReadOnlyList<ProviderListenTogetherUser> _users = [];
    private volatile ExistingRoom? _existingRoom;

    public ListenTogetherService(
        IListenTogetherSyncProvidable provider,
        IProviderKnownTypeIds typeIds,
        IPlaybackControlService control,
        PlaybackStateService state,
        PlayCoreBase playCore,
        PlaybackSettings settings,
        IAuthService auth,
        IUIThreadDispatcher ui,
        IBackgroundTaskRunner taskRunner,
        INotificationService notification,
        IAppLifecycleStateService lifecycle,
        PlaybackRestriction restriction,
        NeteaseSongUrlResolver urlResolver,
        IListenTogetherPrompt prompt,
        IMultiListenTogetherProvidable multiProvider)
    {
        _provider = provider;
        _multiProvider = multiProvider;
        _typeIds = typeIds;
        _control = control;
        _state = state;
        _playCore = playCore;
        _settings = settings;
        _auth = auth;
        _ui = ui;
        _taskRunner = taskRunner;
        _notification = notification;
        _restriction = restriction;
        _urlResolver = urlResolver;
        _prompt = prompt;
        _control.SeekRequested += (_, _) => Interlocked.Increment(ref _seekVersion);
        _restriction.PlayRequested += (_, _) => OnPlayRequested();
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaybackStateService.IsPlaying)) OnMultiPlaybackStateChanged();
        };
        _auth.LoginCompleted += (_, _) => _taskRunner.Forget(CheckAfterLoginAsync(), "check listen together after login");
        lifecycle.EnteredForeground += (_, _) => _taskRunner.Forget(NotifyAsync(AllProperties), "refresh listen together state");
        if (_auth.IsLoggedIn) _taskRunner.Forget(CheckAfterLoginAsync(), "check listen together on startup");
    }

    public ListenTogetherPhase Phase => _phase;

    public bool IsInRoom => _phase is not ListenTogetherPhase.Idle;

    public bool IsIdle => _phase is ListenTogetherPhase.Idle;

    public bool IsDisconnected => _phase is ListenTogetherPhase.Disconnected;

    public bool IsHost => _isHost;

    public string RoomId => _roomId;

    public string ShareLink => _shareLink;

    public string StatusMessage => _statusMessage;

    public IReadOnlyList<ProviderListenTogetherUser> Users => _users;

    public bool HasExistingRoom => _existingRoom is not null;

    public bool CanCreate => IsIdle && _existingRoom is null;

    public string ExistingRoomText => _existingRoom?.Description ?? string.Empty;

    public bool IsDualRoom => IsInRoom && !IsMulti;

    private string? MyUserId => _auth.CurrentUser?.ActualId;

    private bool HasRoom => _session is not null || _multi is not null;

    PlaybackDenial? IPlaybackRestrictionPolicy.Deny(PlaybackRequest request, IReadOnlyList<SingleSongBase>? songs)
    {
        if (_multi is not null) return DenyInMultiRoom(request, songs);
        if (_session is null) return null;
        return request switch
        {
            PlaybackRequest.PlayItems when songs is not null && songs.Any(song => !IsRoomSong(song)) =>
                new PlaybackDenial(NonSongMessage),
            PlaybackRequest.PlayPodcast or PlaybackRequest.PlayLocal => new PlaybackDenial(NonSongMessage),
            PlaybackRequest.PlayRadio => new PlaybackDenial("一起听期间不能开启私人 FM 或心动模式"),
            PlaybackRequest.ChangeSpeed => new PlaybackDenial(SpeedLockedMessage),
            _ => null
        };
    }

    bool IPlaybackRestrictionPolicy.HandlePlaybackFailure(SingleSongBase? song)
    {
        if (song is null || !IsRoomSong(song)) return false;
        if (_multi is { } multi)
        {
            multi.FailedSongId = song.ActualId;
            return true;
        }

        if (_session is not { } session) return false;
        session.FailedSongId = song.ActualId;
        return true;
    }

    public async Task RefreshExistingRoomAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SetExistingRoomAsync(HasRoom ? null : await FindExistingRoomAsync().ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listen together existing room check failed: {ex}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TakeOverAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        Session? session = null;
        try
        {
            if (HasRoom || !await EnsureLoggedInAsync().ConfigureAwait(false)) return;
            await UpdateAsync(ListenTogetherPhase.Connecting, "正在接管一起听…").ConfigureAwait(false);
            var status = await _provider.GetListenTogetherStatusAsync(string.Empty).ConfigureAwait(false);
            if (status is not { IsInRoom: true, IsMultiRoom: false, RoomId: { Length: > 0 } roomId })
            {
                if (await _multiProvider.GetMultiListenTogetherRoomAsync().ConfigureAwait(false) is not { } multiRoom)
                    throw new InvalidOperationException("房间已结束");
                await SetExistingRoomAsync(null).ConfigureAwait(false);
                await StartMultiSessionAsync(multiRoom, "已在本机继续多人一起听").ConfigureAwait(false);
                return;
            }

            await SetExistingRoomAsync(null).ConfigureAwait(false);
            session = OpenSession(roomId, status.CreatorId);
            await PrepareSessionAsync(session).ConfigureAwait(false);
            await ApplyUsersAsync(session, status.Users).ConfigureAwait(false);
            await SyncOnceAsync(session, true, CancellationToken.None).ConfigureAwait(false);
            StartLoop(session);
            await UpdateAsync(ListenTogetherPhase.Connected, "已在本机继续一起听").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailStartAsync(session, "接管一起听失败", ex, false).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EndExistingRoomAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (HasRoom) return;
            var room = _existingRoom ?? await FindExistingRoomAsync().ConfigureAwait(false);
            if (room is not { RoomId: var roomId }) return;
            if (room.IsMulti) await _multiProvider.LeaveMultiListenTogetherRoomAsync(roomId).ConfigureAwait(false);
            else await _provider.EndListenTogetherRoomAsync(roomId).ConfigureAwait(false);
            if (_settings.ListenTogetherLocalRoomId == roomId) _settings.ListenTogetherLocalRoomId = string.Empty;
            await SetExistingRoomAsync(null).ConfigureAwait(false);
            await UpdateMessageAsync("之前的一起听已结束").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listen together end existing failed: {ex}");
            await UpdateMessageAsync($"结束之前的一起听失败：{ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CreateRoomAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        Session? session = null;
        try
        {
            if (!await CanStartAsync().ConfigureAwait(false)) return;
            await UpdateAsync(ListenTogetherPhase.Connecting, "正在创建房间…").ConfigureAwait(false);
            var queue = await GetLocalQueueAsync(CancellationToken.None).ConfigureAwait(false);
            var roomId = await _provider.CreateListenTogetherRoomAsync(queue).ConfigureAwait(false);
            session = OpenSession(roomId, MyUserId);
            await PrepareSessionAsync(session).ConfigureAwait(false);
            await ReportQueueAsync(session, CancellationToken.None).ConfigureAwait(false);
            await RefreshStatusAsync(session, CancellationToken.None).ConfigureAwait(false);
            StartLoop(session);
            await UpdateAsync(ListenTogetherPhase.Connected, "房间已创建，可以邀请好友一起听啦~").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailStartAsync(session, "创建房间失败", ex).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ListenTogetherInvitation?> ResolveInvitationAsync(string text)
    {
        if (ListenTogetherInvitation.TryParse(text, out var invitation)) return invitation;
        if (!ListenTogetherInvitation.TryGetShortLink(text, out var shortLink) || shortLink is null) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, shortLink);
            using var response = await ShortLinkClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            return response.Headers.Location is { } target &&
                   ListenTogetherInvitation.TryParse(new Uri(shortLink, target).AbsoluteUri, out var resolved)
                ? resolved
                : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listen together short link resolve failed: {ex.Message}");
            return null;
        }
    }

    public async Task JoinRoomAsync(ListenTogetherInvitation invitation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        Session? session = null;
        try
        {
            if (!await CanStartAsync(invitation.RoomId).ConfigureAwait(false)) return;
            if (invitation.IsMulti)
            {
                await JoinMultiRoomCoreAsync(invitation).ConfigureAwait(false);
                return;
            }

            await UpdateAsync(ListenTogetherPhase.Connecting, "正在加入房间…").ConfigureAwait(false);
            if (!await _provider.CanJoinListenTogetherRoomAsync(invitation.RoomId).ConfigureAwait(false))
                throw new InvalidOperationException("房间不存在、已结束或已满员");
            var status = await _provider.JoinListenTogetherRoomAsync(invitation.RoomId, invitation.InviterId)
                .ConfigureAwait(false);
            session = OpenSession(status.RoomId ?? invitation.RoomId, status.CreatorId ?? invitation.InviterId);
            await PrepareSessionAsync(session).ConfigureAwait(false);
            await ApplyUsersAsync(session, status.Users).ConfigureAwait(false);
            await SyncOnceAsync(session, true, CancellationToken.None).ConfigureAwait(false);
            StartLoop(session);
            await UpdateAsync(ListenTogetherPhase.Connected, "已加入房间").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailStartAsync(session, "加入房间失败", ex).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_multi is { } multi && _phase == ListenTogetherPhase.Disconnected)
            {
                await ReconnectMultiAsync(multi).ConfigureAwait(false);
                return;
            }

            if (_session is not { } session || _phase != ListenTogetherPhase.Disconnected) return;
            await UpdateAsync(ListenTogetherPhase.Reconnecting, "正在重新连接…").ConfigureAwait(false);
            session.Failures = 0;
            session.MissingStatus = 0;
            session.Cts = new CancellationTokenSource();
            await RefreshStatusAsync(session, session.Cts.Token).ConfigureAwait(false);
            if (!ReferenceEquals(_session, session)) return;
            session.LastServerSeq = 0;
            session.LastVersionKey = null;
            session.Published = ReadLocal();
            await SyncOnceAsync(session, true, session.Cts.Token).ConfigureAwait(false);
            StartLoop(session);
            await UpdateAsync(ListenTogetherPhase.Connected, "已重新连接").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await UpdateAsync(ListenTogetherPhase.Disconnected, $"重新连接失败：{ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EndRoomAsync()
    {
        if (_multi is not null)
        {
            await LeaveMultiRoomAsync().ConfigureAwait(false);
            return;
        }

        Session? session;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            session = _session;
            if (session is null) return;
            await UpdateAsync(ListenTogetherPhase.Ending, "正在结束一起听…").ConfigureAwait(false);
            session.Cts.Cancel();
        }
        finally
        {
            _gate.Release();
        }

        await WaitForLoopAsync(session).ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_session, session)) return;
            try
            {
                await _provider.EndListenTogetherRoomAsync(session.RoomId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Listen together end failed: {ex}");
                _notification.ShowMessage("结束一起听时出错", ex.Message);
            }

            await CloseSessionAsync(session, "一起听已结束").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CheckAfterLoginAsync()
    {
        ExistingRoom? room;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (HasRoom) return;
            room = await FindExistingRoomAsync().ConfigureAwait(false);
            if (room is null && !string.IsNullOrEmpty(_settings.ListenTogetherLocalRoomId))
                _settings.ListenTogetherLocalRoomId = string.Empty;
            await SetExistingRoomAsync(room).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listen together startup check failed: {ex}");
            return;
        }
        finally
        {
            _gate.Release();
        }

        if (room is null) return;
        if (_settings.ListenTogetherAutoResume && room.RoomId == _settings.ListenTogetherLocalRoomId)
        {
            await PromptAsync(room, new ListenTogetherPromptRequest("继续上次的一起听？", $"{room.Description}。", "继续",
                room.IsMulti ? "退出房间" : "结束一起听", "忽略")).ConfigureAwait(false);
            return;
        }

        if (_settings.ListenTogetherDetectOngoing) await PromptTakeoverAsync(room).ConfigureAwait(false);
    }

    private void OnPlayRequested()
    {
        if (HasRoom || !_settings.ListenTogetherDetectOngoing || string.IsNullOrEmpty(MyUserId)) return;
        if (Interlocked.CompareExchange(ref _detecting, 1, 0) != 0) return;
        _taskRunner.Forget(DetectOngoingAsync(), "detect ongoing listen together");
    }

    private async Task DetectOngoingAsync()
    {
        try
        {
            ExistingRoom? room;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (HasRoom) return;
                room = await FindExistingRoomAsync().ConfigureAwait(false);
                await SetExistingRoomAsync(room).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            if (room is not null) await PromptTakeoverAsync(room).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listen together detection failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref _detecting, 0);
        }
    }

    private Task PromptTakeoverAsync(ExistingRoom room)
    {
        return PromptAsync(room, new ListenTogetherPromptRequest("检测到正在进行的一起听", $"{room.Description}。",
            "在本机接管", room.IsMulti ? "退出这个房间" : "结束这个一起听", "暂不处理"));
    }

    private async Task PromptAsync(ExistingRoom room, ListenTogetherPromptRequest request)
    {
        var choice = await _prompt.AskAsync(request).ConfigureAwait(false);
        if (choice == ListenTogetherTakeoverChoice.TakeOver) await TakeOverAsync().ConfigureAwait(false);
        else if (choice == ListenTogetherTakeoverChoice.End) await EndExistingRoomAsync().ConfigureAwait(false);
    }

    private async Task<ExistingRoom?> FindExistingRoomAsync()
    {
        if (string.IsNullOrEmpty(MyUserId)) return null;
        var status = await _provider.GetListenTogetherStatusAsync(string.Empty).ConfigureAwait(false);
        if (status is { IsInRoom: true, IsMultiRoom: false, RoomId: { Length: > 0 } roomId })
        {
            var partner = status.Users.FirstOrDefault(user => user.UserId != MyUserId)?.Nickname;
            return new ExistingRoom(false, roomId, string.IsNullOrEmpty(partner)
                ? "你的账号有一个正在进行的一起听"
                : $"你的账号正在和「{partner}」一起听");
        }

        try
        {
            if (await _multiProvider.GetMultiListenTogetherRoomAsync().ConfigureAwait(false) is { RoomId.Length: > 0 } multi)
                return new ExistingRoom(true, multi.RoomId,
                    $"你的账号正在一个多人一起听房间里（当前 {multi.Users.Count} 人在一起听歌）");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Multi listen together status check failed: {ex.Message}");
        }

        return null;
    }

    private async Task<bool> CanStartAsync(string? joiningRoomId = null)
    {
        if (HasRoom || !await EnsureLoggedInAsync().ConfigureAwait(false)) return false;
        if (await FindExistingRoomAsync().ConfigureAwait(false) is not { } existing) return true;
        await SetExistingRoomAsync(existing).ConfigureAwait(false);
        await UpdateMessageAsync(existing.RoomId == joiningRoomId
            ? "你已经在这个房间里了，可以直接在本机接管"
            : "账号已有正在进行的一起听，请先接管或结束它").ConfigureAwait(false);
        return false;
    }

    private Task SetExistingRoomAsync(ExistingRoom? room)
    {
        _existingRoom = room;
        return NotifyAsync(nameof(HasExistingRoom), nameof(CanCreate), nameof(ExistingRoomText));
    }

    private Session OpenSession(string roomId, string? creatorId)
    {
        var session = new Session
        {
            RoomId = roomId,
            CreatorId = creatorId,
            UserId = MyUserId,
            StartedAtMs = NowMs(),
            PreviousPlayModeId = _state.ActiveStrategyId,
            ClientSeq = Random.Shared.Next(100_000, 1_000_000)
        };
        _session = session;
        _settings.ListenTogetherLocalRoomId = roomId;
        _restriction.Apply(this);
        ResetPlaybackSpeed();
        return session;
    }

    private async Task PrepareSessionAsync(Session session)
    {
        using (_restriction.Exemption())
        {
            await _control.SetTransitionAsync("dir").ConfigureAwait(false);
            if (_state.ActiveStrategyId is not ("seq" or "sgl" or "shn"))
                await _control.SetPlayModeAsync("seq").ConfigureAwait(false);
        }

        session.LastModeId = _state.ActiveStrategyId;
        _roomId = session.RoomId;
        _isHost = IsCreator(session);
        _shareLink = ListenTogetherInvitation.BuildShareLink(session.RoomId, session.CreatorId ?? session.UserId ?? "0",
            ReadLocal()?.SongId);
        await NotifyAsync(nameof(RoomId), nameof(IsHost), nameof(ShareLink)).ConfigureAwait(false);
    }

    private void StartLoop(Session session)
    {
        var loop = RunAsync(session, session.Cts.Token);
        session.Loop = loop;
        _taskRunner.Forget(loop, "listen together sync");
    }

    private async Task RunAsync(Session session, CancellationToken ct)
    {
        using var exemption = _restriction.Exemption();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(session.Failures == 0 ? TickInterval : Backoff(session.Failures), ct).ConfigureAwait(false);
                await _gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(_session, session) || ct.IsCancellationRequested) return;
                    if (MyUserId is not { } userId || userId != session.UserId)
                    {
                        await CloseSessionAsync(session, "登录账号已变化，已退出一起听").ConfigureAwait(false);
                        return;
                    }

                    await TickAsync(session, ct).ConfigureAwait(false);
                    if (!ReferenceEquals(_session, session)) return;
                    if (session.Failures > 0)
                        await UpdateAsync(ListenTogetherPhase.Connected, "已恢复连接").ConfigureAwait(false);
                    session.Failures = 0;
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Listen together sync failed: {ex}");
                session.Failures++;
                if (session.Failures >= MaxConsecutiveFailures)
                {
                    _control.Pause();
                    await UpdateAsync(ListenTogetherPhase.Disconnected, $"与房间的连接已断开：{ex.Message}")
                        .ConfigureAwait(false);
                    return;
                }

                await UpdateAsync(ListenTogetherPhase.Reconnecting, "网络不稳定，正在重试…").ConfigureAwait(false);
            }
        }
    }

    private async Task TickAsync(Session session, CancellationToken ct)
    {
        await HandleFailedSongAsync(session).ConfigureAwait(false);
        if (NowMs() >= session.NextStatusMs)
        {
            await RefreshStatusAsync(session, ct).ConfigureAwait(false);
            if (!ReferenceEquals(_session, session)) return;
        }

        await SyncOnceAsync(session, false, ct).ConfigureAwait(false);
        if (NowMs() >= session.NextHeartbeatMs) await SendHeartbeatAsync(session, ct).ConfigureAwait(false);
    }

    private async Task HandleFailedSongAsync(Session session)
    {
        if (Interlocked.Exchange(ref session.FailedSongId, null) is not { } failed) return;
        await HoldAsync(session, failed, SongAccess.Unavailable, failed == session.Published?.SongId)
            .ConfigureAwait(false);
    }

    private async Task RefreshStatusAsync(Session session, CancellationToken ct)
    {
        var status = await _provider.GetListenTogetherStatusAsync(session.RoomId, ct).ConfigureAwait(false);
        session.NextStatusMs = NowMs() + (long)StatusInterval.TotalMilliseconds;
        var inThisRoom = status is { IsInRoom: true } && (string.IsNullOrEmpty(status.RoomId) || status.RoomId == session.RoomId);
        if (!inThisRoom)
        {
            if (NowMs() - session.StartedAtMs < MembershipGrace.TotalMilliseconds) return;
            if (++session.MissingStatus < MissingStatusLimit) return;
            await CloseSessionAsync(session, "房间已结束").ConfigureAwait(false);
            _notification.ShowMessage("一起听已结束", "房间已关闭或你已离开房间");
            return;
        }

        session.MissingStatus = 0;
        if (!string.IsNullOrEmpty(status!.CreatorId)) session.CreatorId = status.CreatorId;
        await ApplyUsersAsync(session, status.Users).ConfigureAwait(false);
    }

    private async Task ApplyUsersAsync(Session session, List<ProviderListenTogetherUser> users)
    {
        AnnounceMemberChanges(_users, users, session.UserId);
        _users = users.ToList();
        _isHost = IsCreator(session);
        await NotifyAsync(nameof(Users), nameof(IsHost)).ConfigureAwait(false);
    }

    private async Task SyncOnceAsync(Session session, bool initial, CancellationToken ct)
    {
        using var exemption = _restriction.Exemption();
        var room = await _provider.GetListenTogetherRoomStateAsync(session.RoomId, ct).ConfigureAwait(false);
        await ApplyRemoteQueueAsync(session, room, initial, ct).ConfigureAwait(false);
        var appliedRemote = false;

        if (room.LastCommand is { } command && command.ServerSeq > session.LastServerSeq)
        {
            if (ListenTogetherPlanner.IsOwnCommand(command, session.UserId, session.SentClientSeqs))
            {
                if (command.ClientSeq == session.LastSentClientSeq &&
                    ListenTogetherPlanner.EstimateClockOffset(command, session.LastSentAtMs) is { } offset)
                    session.ClockOffsetMs = offset;
                session.LastRemoteCommand = null;
            }
            else if (!ListenTogetherPlanner.TargetsAbandonedSong(command, session.AbandonedSongId,
                         session.AbandonedAtMs, NowMs()))
            {
                if (!initial) ShowRemoteTip(session, command);
                await ApplyRemoteCommandAsync(session, command, ct).ConfigureAwait(false);
                appliedRemote = true;
            }

            session.LastServerSeq = command.ServerSeq;
        }
        else if (!initial)
        {
            await CorrectDriftAsync(session, ct).ConfigureAwait(false);
        }

        if (initial && session.Published is null) session.Published = ReadLocal();
        await PublishLocalChangesAsync(session, !appliedRemote, ct).ConfigureAwait(false);
    }

    private async Task ApplyRemoteQueueAsync(Session session, ProviderListenTogetherRoomState room, bool initial,
        CancellationToken ct)
    {
        var versionKey = ListenTogetherPlanner.VersionKey(room.Versions);
        if (!initial && versionKey == session.LastVersionKey) return;
        var displayIds = room.DisplayItemIds;
        if (displayIds.Count > 0 && ListenTogetherPlanner.QueueKey(displayIds) != session.LastQueueKey)
        {
            var songs = await _provider.GetListenTogetherQueueSongsAsync(displayIds, ct).ConfigureAwait(false);
            if (songs.Count > 0)
            {
                await ReplaceLocalQueueAsync(songs, ct).ConfigureAwait(false);
                if (!initial) ShowTip("对方替换了播放列表");
            }

            session.LastQueueKey = ListenTogetherPlanner.QueueKey(await GetLocalQueueIdsAsync(ct).ConfigureAwait(false));
        }

        if (room.PlayModeId == "shn")
        {
            await ApplyRemoteRandomOrderAsync(session, room.RandomItemIds.Count > 0 ? room.RandomItemIds : displayIds,
                initial, ct).ConfigureAwait(false);
        }
        else if (room.PlayModeId is not null)
        {
            var mode = room.PlayModeId == "sgl" ? "sgl" : "seq";
            if (_state.ActiveStrategyId != mode)
            {
                await _control.SetPlayModeAsync(mode, ct).ConfigureAwait(false);
                if (!initial) ShowTip(mode == "sgl" ? "对方切换为单曲循环" : "对方切换为顺序播放");
            }

            session.LastRandomKey = null;
        }

        session.LastModeId = _state.ActiveStrategyId;
        session.LastVersionKey = versionKey;
    }

    private async Task ApplyRemoteRandomOrderAsync(Session session, IReadOnlyList<string> randomIds, bool initial,
        CancellationToken ct)
    {
        var switching = _state.ActiveStrategyId != "shn";
        if (!switching && ListenTogetherPlanner.QueueKey(randomIds) == session.LastRandomKey) return;
        if (switching)
        {
            await _control.SetPlayModeAsync("shn", ct).ConfigureAwait(false);
            if (!initial) ShowTip("对方开启了随机播放");
        }

        var local = await GetLocalQueueAsync(ct).ConfigureAwait(false);
        var order = randomIds
            .Select(id => local.FirstOrDefault(song => song.ActualId == id))
            .OfType<SingleSongBase>()
            .ToList();
        await _playCore.ApplyRandomOrderAsync(order, ct).ConfigureAwait(false);
        session.LastRandomKey = ListenTogetherPlanner.QueueKey(await GetLocalRandomOrderIdsAsync(ct).ConfigureAwait(false));
    }

    private async Task ReplaceLocalQueueAsync(List<SingleSongBase> songs, CancellationToken ct)
    {
        var current = _state.NowPlayingProviderItem;
        if (current is not null && songs.Any(song => IsSameSong(song, current)) &&
            await _control.ReplaceQueueKeepingPlaybackAsync(songs, current, null, ct).ConfigureAwait(false))
            return;
        await _control.ClearQueueAsync(ct).ConfigureAwait(false);
        await _playCore.InsertSongRangeAsync(songs, ctk: ct).ConfigureAwait(false);
    }

    private async Task ApplyRemoteCommandAsync(Session session, ProviderListenTogetherRemoteCommand command,
        CancellationToken ct)
    {
        session.LastRemoteCommand = null;
        var targetId = command.TargetItemId is { Length: > 0 } id && id != "-1" ? id : ReadLocal()?.SongId;
        if (targetId is null) return;
        if (session.HeldSongId == targetId) return;
        if (ReadLocal()?.SongId != targetId)
        {
            var access = await CheckAccessAsync(targetId, ct).ConfigureAwait(false);
            if (access != SongAccess.Full)
            {
                await HoldAsync(session, targetId, access, true).ConfigureAwait(false);
                return;
            }

            session.HeldSongId = null;
            if (!await SwitchToSongAsync(targetId, command.IsPlaying, ct).ConfigureAwait(false))
            {
                await UpdateMessageAsync("无法获取对方正在播放的歌曲").ConfigureAwait(false);
                session.Published = ReadLocal();
                return;
            }

            await SeekToExpectedAsync(session, command, true).ConfigureAwait(false);
        }
        else
        {
            session.HeldSongId = null;
            await SeekToExpectedAsync(session, command, false).ConfigureAwait(false);
        }

        if (command.IsPlaying && !_state.IsPlaying) _control.Play();
        else if (!command.IsPlaying && _state.IsPlaying) _control.Pause();
        session.LastRemoteCommand = command;
        session.Published = new ListenTogetherPlaybackSnapshot(targetId, command.IsPlaying, _state.Position,
            Interlocked.Read(ref _seekVersion));
        session.PendingRemote = new PendingRemote(targetId, command.IsPlaying,
            NowMs() + (long)RemoteSettleWindow.TotalMilliseconds);
        session.NextCorrectionMs = NowMs() + (long)CorrectionInterval.TotalMilliseconds;
    }

    private async Task HoldAsync(Session session, string songId, SongAccess access, bool fromRoom)
    {
        session.HeldSongId = songId;
        session.HoldBaseSongId = ReadLocal()?.SongId;
        session.PendingRemote = null;
        session.LastRemoteCommand = null;
        if (_state.IsPlaying) _control.Pause();
        session.Published = ReadLocal() is { } local ? local with { IsPlaying = false } : session.Published;
        var detail = fromRoom
            ? $"{DescribeAccess(access)}，等对方切到下一首后会自动跟上"
            : $"{DescribeAccess(access)}，对方切歌或调整播放时会自动跟上";
        await UpdateMessageAsync(detail).ConfigureAwait(false);
        _notification.ShowMessage(fromRoom ? "对方正在播放的歌曲本机无法收听" : "这首歌没有同步给对方", detail);
    }

    private async Task<SongAccess> CheckAccessAsync(string songId, CancellationToken ct)
    {
        try
        {
            var resolved = await _urlResolver.ResolveAsync(songId, ct).ConfigureAwait(false);
            if (resolved is null) return SongAccess.Unavailable;
            return resolved.Item.FreeTrialInfo is null ? SongAccess.Full : SongAccess.Trial;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listen together access check failed for {songId}: {ex.Message}");
            return SongAccess.Full;
        }
    }

    private static string DescribeAccess(SongAccess access)
    {
        return access == SongAccess.Trial
            ? "当前歌曲需要 VIP 或单独购买，当前仅试听"
            : "当前歌曲无法播放（可能需要 VIP、购买专辑或已下架）";
    }

    private async Task<bool> SwitchToSongAsync(string songId, bool autoPlay, CancellationToken ct)
    {
        var song = await FindQueuedSongAsync(songId, ct).ConfigureAwait(false);
        if (song is null)
        {
            song = (await _provider.GetListenTogetherQueueSongsAsync([songId], ct).ConfigureAwait(false)).FirstOrDefault();
            if (song is null) return false;
            await _playCore.InsertSongAsync(song, ctk: ct).ConfigureAwait(false);
        }

        await _playCore.MovePointerToAsync(song, ct).ConfigureAwait(false);
        await _control.LoadAndPlayAsync(_playCore.CurrentSong ?? song, autoPlay, false).ConfigureAwait(false);
        return ReadLocal()?.SongId == songId;
    }

    private async Task SeekToExpectedAsync(Session session, ProviderListenTogetherRemoteCommand command, bool afterLoad)
    {
        var expected = ListenTogetherPlanner.ExpectedPosition(command, NowMs() + session.ClockOffsetMs);
        var duration = _state.Duration;
        if (duration > TimeSpan.Zero && expected >= duration - TimeSpan.FromSeconds(1)) return;
        if (afterLoad ? expected < ListenTogetherPlanner.DriftTolerance : !ListenTogetherPlanner.NeedsSeek(_state.Position, expected))
            return;
        await _control.SeekAsync(expected).ConfigureAwait(false);
    }

    private async Task CorrectDriftAsync(Session session, CancellationToken ct)
    {
        if (session.HeldSongId is not null) return;
        if (session.LastRemoteCommand is not { IsPlaying: true } command || NowMs() < session.NextCorrectionMs) return;
        session.NextCorrectionMs = NowMs() + (long)CorrectionInterval.TotalMilliseconds;
        if (_state.IsLoadingTrack || ReadLocal() is not { IsPlaying: true } local || session.Published is not { } published)
            return;
        if (local.SongId != command.TargetItemId || local.SongId != published.SongId ||
            local.SeekVersion != published.SeekVersion)
            return;
        var expected = ListenTogetherPlanner.ExpectedPosition(command, NowMs() + session.ClockOffsetMs);
        if (!ListenTogetherPlanner.NeedsSeek(local.Position, expected)) return;
        if (_state.Duration > TimeSpan.Zero && expected >= _state.Duration - TimeSpan.FromSeconds(1)) return;
        await _control.SeekAsync(expected).ConfigureAwait(false);
        session.Published = published with { SeekVersion = Interlocked.Read(ref _seekVersion) };
    }

    private async Task PublishLocalChangesAsync(Session session, bool publishPlayback, CancellationToken ct)
    {
        if (_state.ActiveStrategyId is not ("seq" or "sgl" or "shn"))
            await _control.SetPlayModeAsync("seq", ct).ConfigureAwait(false);

        var queueKey = ListenTogetherPlanner.QueueKey(await GetLocalQueueIdsAsync(ct).ConfigureAwait(false));
        var randomChanged = _state.ActiveStrategyId == "shn" &&
                            ListenTogetherPlanner.QueueKey(await GetLocalRandomOrderIdsAsync(ct).ConfigureAwait(false)) !=
                            session.LastRandomKey;
        if (queueKey != session.LastQueueKey || _state.ActiveStrategyId != session.LastModeId || randomChanged)
            await ReportQueueAsync(session, ct).ConfigureAwait(false);

        if (!publishPlayback || _state.IsLoadingTrack || ReadLocal() is not { } current) return;
        if (session.HeldSongId is not null)
        {
            if (current.SongId == session.HeldSongId || current.SongId == session.HoldBaseSongId) return;
            session.HeldSongId = null;
            session.HoldBaseSongId = null;
        }

        if (session.PendingRemote is { } pending)
        {
            if (current.SongId == pending.SongId && current.IsPlaying == pending.IsPlaying)
            {
                session.PendingRemote = null;
            }
            else if (NowMs() < pending.DeadlineMs)
            {
                return;
            }
            else
            {
                session.PendingRemote = null;
                if (current.SongId != pending.SongId)
                {
                    session.Published = current;
                    await UpdateMessageAsync("对方正在播放的歌曲在本机无法播放").ConfigureAwait(false);
                    return;
                }
            }
        }

        var command = ListenTogetherPlanner.DetectLocalCommand(session.Published, current);
        if (command is null) return;
        if (command == ListenTogetherCommands.Goto &&
            await CheckAccessAsync(current.SongId, ct).ConfigureAwait(false) is var access && access != SongAccess.Full)
        {
            await HoldAsync(session, current.SongId, access, false).ConfigureAwait(false);
            return;
        }

        var clientSeq = ++session.ClientSeq;
        var sentAt = NowMs();
        await _provider.SendListenTogetherPlaybackCommandAsync(session.RoomId, new ProviderListenTogetherPlaybackCommand
        {
            CommandId = command,
            IsPlaying = current.IsPlaying,
            Position = current.Position,
            FormerItemId = session.Published?.SongId ?? "-1",
            TargetItemId = current.SongId,
            ClientSeq = clientSeq
        }, ct).ConfigureAwait(false);
        session.RememberSent(clientSeq);
        session.LastSentAtMs = sentAt;
        if (command == ListenTogetherCommands.Goto && session.Published?.SongId is { } abandoned && abandoned != current.SongId)
        {
            session.AbandonedSongId = abandoned;
            session.AbandonedAtMs = sentAt;
        }

        session.Published = current;
        session.LastRemoteCommand = null;
    }

    private async Task ReportQueueAsync(Session session, CancellationToken ct)
    {
        var queue = await GetLocalQueueAsync(ct).ConfigureAwait(false);
        var queueKey = ListenTogetherPlanner.QueueKey(queue.Select(song => song.ActualId!));
        var modeId = _state.ActiveStrategyId;
        var randomQueue = modeId == "shn"
            ? (await _playCore.GetOrderedPlaylistAsync(ct).ConfigureAwait(false)).Where(IsRoomSong).ToList()
            : null;
        if (queue.Count > 0)
        {
            var version = session.QueueVersion + 1;
            await _provider.ReportListenTogetherQueueAsync(session.RoomId, new ProviderListenTogetherQueueReport
            {
                Queue = queue,
                RandomQueue = randomQueue,
                UserId = session.UserId,
                PlayModeId = modeId is "sgl" or "shn" ? modeId : "seq",
                ClientSeq = (int)Math.Min(version, int.MaxValue),
                AnchorPosition = -1,
                AnchorItemId = string.Empty
            }, ct).ConfigureAwait(false);
            session.QueueVersion = version;
        }

        session.LastQueueKey = queueKey;
        session.LastModeId = modeId;
        session.LastRandomKey = randomQueue is null
            ? null
            : ListenTogetherPlanner.QueueKey(randomQueue.Select(song => song.ActualId!));
    }

    private async Task SendHeartbeatAsync(Session session, CancellationToken ct)
    {
        if (ReadLocal() is not { } local)
        {
            session.NextHeartbeatMs = NowMs() + 5_000;
            return;
        }

        var interval = await _provider.SendListenTogetherHeartbeatAsync(session.RoomId, new ProviderListenTogetherHeartbeat
        {
            ItemId = local.SongId,
            IsPlaying = local.IsPlaying,
            Position = local.Position,
            UserId = session.UserId,
            PlaylistVersion = session.QueueVersion
        }, ct).ConfigureAwait(false);
        var next = interval ?? DefaultHeartbeat;
        if (next < TimeSpan.FromSeconds(5)) next = TimeSpan.FromSeconds(5);
        if (next > TimeSpan.FromSeconds(60)) next = TimeSpan.FromSeconds(60);
        session.NextHeartbeatMs = NowMs() + (long)next.TotalMilliseconds;
    }

    private void ShowRemoteTip(Session session, ProviderListenTogetherRemoteCommand command)
    {
        if (!_settings.ListenTogetherRemoteTips) return;
        var who = command.UserId == session.UserId
            ? "你在其他设备上"
            : _users.FirstOrDefault(user => user.UserId == command.UserId)?.Nickname ?? "对方";
        var text = command.CommandId switch
        {
            ListenTogetherCommands.Goto => " 切换了歌曲",
            ListenTogetherCommands.Play => " 继续播放",
            ListenTogetherCommands.Pause => " 暂停了播放",
            ListenTogetherCommands.Seek => $" 把进度调到了 {(int)command.Position.TotalMinutes}:{command.Position.Seconds:00}",
            _ => null
        };
        if (text is not null) _notification.ShowMessage("一起听", $"{who}{text}");
    }

    private void ResetPlaybackSpeed()
    {
        if (Math.Abs(_state.PlaybackSpeed - 1d) <= 0.001) return;
        _control.SetPlaybackSpeed(1d);
        _notification.ShowMessage("一起听", "已恢复为 1.0 倍速，一起听期间不能调整播放速度");
    }

    private void AnnounceMemberChanges(IReadOnlyList<ProviderListenTogetherUser> previous,
        IReadOnlyList<ProviderListenTogetherUser> current, string? myUserId)
    {
        if (previous.Count == 0) return;
        var before = previous.Select(user => user.UserId).ToHashSet();
        var after = current.Select(user => user.UserId).ToHashSet();
        foreach (var user in current.Where(user => user.UserId != myUserId && !before.Contains(user.UserId)))
            _notification.ShowMessage("一起听", $"{DisplayName(user)} 加入了一起听");
        foreach (var user in previous.Where(user => user.UserId != myUserId && !after.Contains(user.UserId)))
            _notification.ShowMessage("一起听", $"{DisplayName(user)} 退出了一起听");
    }

    private static string DisplayName(ProviderListenTogetherUser user)
    {
        return string.IsNullOrWhiteSpace(user.Nickname) ? "有人" : user.Nickname;
    }

    private void ShowTip(string text)
    {
        if (_settings.ListenTogetherRemoteTips) _notification.ShowMessage("一起听", text);
    }

    private async Task CloseSessionAsync(Session session, string message)
    {
        if (!ReferenceEquals(_session, session)) return;
        _session = null;
        _restriction.Apply(null);
        _settings.ListenTogetherLocalRoomId = string.Empty;
        session.Cts.Cancel();
        await RestorePlaybackAsync(session.PreviousPlayModeId).ConfigureAwait(false);
        _roomId = string.Empty;
        _shareLink = string.Empty;
        _isHost = false;
        _users = [];
        _phase = ListenTogetherPhase.Idle;
        _statusMessage = message;
        await NotifyAsync(AllProperties).ConfigureAwait(false);
    }

    private async Task RestorePlaybackAsync(string? previousPlayModeId)
    {
        try
        {
            await _control.SetTransitionAsync(_settings.TransitionId).ConfigureAwait(false);
            if (previousPlayModeId is "seq" or "sgl" or "shn" && previousPlayModeId != _state.ActiveStrategyId)
                await _control.SetPlayModeAsync(previousPlayModeId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listen together restore failed: {ex}");
        }
    }

    private async Task FailStartAsync(Session? session, string title, Exception ex, bool endOwnRoom = true)
    {
        System.Diagnostics.Debug.WriteLine($"{title}: {ex}");
        if (session is null)
        {
            await UpdateAsync(ListenTogetherPhase.Idle, $"{title}：{ex.Message}").ConfigureAwait(false);
            return;
        }

        if (endOwnRoom && IsCreator(session))
        {
            try
            {
                await _provider.EndListenTogetherRoomAsync(session.RoomId).ConfigureAwait(false);
            }
            catch (Exception endEx)
            {
                System.Diagnostics.Debug.WriteLine($"Listen together cleanup failed: {endEx}");
            }
        }

        await CloseSessionAsync(session, $"{title}：{ex.Message}").ConfigureAwait(false);
    }

    private static async Task WaitForLoopAsync(Session session)
    {
        if (session.Loop is not { } loop) return;
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> EnsureLoggedInAsync()
    {
        if (!string.IsNullOrEmpty(MyUserId)) return true;
        await UpdateAsync(ListenTogetherPhase.Idle, "请先登录网易云音乐").ConfigureAwait(false);
        return false;
    }

    private async Task<List<SingleSongBase>> GetLocalQueueAsync(CancellationToken ct)
    {
        return (await _playCore.GetPlaylistAsync(ct).ConfigureAwait(false)).Where(IsRoomSong).ToList();
    }

    private async Task<List<string>> GetLocalRandomOrderIdsAsync(CancellationToken ct)
    {
        return (await _playCore.GetOrderedPlaylistAsync(ct).ConfigureAwait(false)).Where(IsRoomSong)
            .Select(song => song.ActualId!).ToList();
    }

    private async Task<List<string>> GetLocalQueueIdsAsync(CancellationToken ct)
    {
        return (await GetLocalQueueAsync(ct).ConfigureAwait(false)).Select(song => song.ActualId!).ToList();
    }

    private async Task<SingleSongBase?> FindQueuedSongAsync(string songId, CancellationToken ct)
    {
        return (await GetLocalQueueAsync(ct).ConfigureAwait(false)).FirstOrDefault(song => song.ActualId == songId);
    }

    private ListenTogetherPlaybackSnapshot? ReadLocal()
    {
        var item = _state.NowPlayingProviderItem;
        if (item is null || !IsRoomSong(item)) return null;
        return new ListenTogetherPlaybackSnapshot(item.ActualId!, _state.IsPlaying, _state.Position,
            Interlocked.Read(ref _seekVersion));
    }

    private bool IsRoomSong(SingleSongBase song)
    {
        return song.ProviderId == NeteaseProviderId && song.TypeId == _typeIds.SingleSongTypeId &&
               !string.IsNullOrEmpty(song.ActualId);
    }

    private static bool IsSameSong(SingleSongBase left, SingleSongBase right)
    {
        return left.ProviderId == right.ProviderId && left.ActualId == right.ActualId;
    }

    private static bool IsCreator(Session session)
    {
        return !string.IsNullOrEmpty(session.CreatorId) && session.CreatorId == session.UserId;
    }

    private Task UpdateAsync(ListenTogetherPhase phase, string message)
    {
        _phase = phase;
        _statusMessage = message;
        return NotifyAsync(nameof(Phase), nameof(IsInRoom), nameof(IsIdle), nameof(IsDisconnected), nameof(StatusMessage),
            nameof(CanCreate), nameof(IsDualRoom));
    }

    private Task UpdateMessageAsync(string message)
    {
        _statusMessage = message;
        return NotifyAsync(nameof(StatusMessage));
    }

    private Task NotifyAsync(params string[] properties)
    {
        return _ui.TryRunAsync(() =>
        {
            foreach (var property in properties) OnPropertyChanged(property);
        });
    }

    private static TimeSpan Backoff(int failures)
    {
        return TimeSpan.FromSeconds(Math.Min(30, 2 << Math.Min(failures, 4)));
    }

    private static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private enum SongAccess
    {
        Full,
        Trial,
        Unavailable
    }

    private sealed record PendingRemote(string SongId, bool IsPlaying, long DeadlineMs);

    private sealed record ExistingRoom(bool IsMulti, string RoomId, string Description);

    private sealed class Session
    {
        private readonly Queue<long> _sentOrder = new();
        private readonly HashSet<long> _sent = [];
        public string? FailedSongId;
        public required string RoomId { get; init; }
        public string? CreatorId { get; set; }
        public string? UserId { get; init; }
        public long StartedAtMs { get; init; }
        public string? PreviousPlayModeId { get; init; }
        public CancellationTokenSource Cts { get; set; } = new();
        public Task? Loop { get; set; }
        public int Failures { get; set; }
        public int MissingStatus { get; set; }
        public ListenTogetherPlaybackSnapshot? Published { get; set; }
        public PendingRemote? PendingRemote { get; set; }
        public ProviderListenTogetherRemoteCommand? LastRemoteCommand { get; set; }
        public string? HeldSongId { get; set; }
        public string? HoldBaseSongId { get; set; }
        public string? AbandonedSongId { get; set; }
        public long AbandonedAtMs { get; set; }
        public long LastServerSeq { get; set; }
        public int ClientSeq { get; set; }
        public long LastSentClientSeq { get; private set; }
        public IReadOnlyCollection<long> SentClientSeqs => _sent;
        public long LastSentAtMs { get; set; }
        public long ClockOffsetMs { get; set; }
        public string? LastQueueKey { get; set; }
        public string? LastVersionKey { get; set; }
        public string? LastRandomKey { get; set; }
        public string? LastModeId { get; set; }
        public long QueueVersion { get; set; }
        public long NextHeartbeatMs { get; set; }
        public long NextStatusMs { get; set; }
        public long NextCorrectionMs { get; set; }

        public void RememberSent(long clientSeq)
        {
            LastSentClientSeq = clientSeq;
            if (!_sent.Add(clientSeq)) return;
            _sentOrder.Enqueue(clientSeq);
            if (_sentOrder.Count > SentSeqMemory) _sent.Remove(_sentOrder.Dequeue());
        }
    }
}
