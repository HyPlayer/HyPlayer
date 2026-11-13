using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Features.Playback.Services;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.ListenTogether;

internal sealed partial class ListenTogetherService
{
    private static readonly TimeSpan MultiTickInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MultiStatusInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MultiDriftTolerance = TimeSpan.FromSeconds(2);
    private volatile MultiSession? _multi;
    private volatile IReadOnlyList<ProviderMultiListenTogetherQueueItem> _multiQueue = [];

    public bool IsMulti => _multi is not null;

    public bool CanRequestSkip => _multi is { CanRequestSkip: true };

    public IReadOnlyList<ProviderMultiListenTogetherQueueItem> MultiQueue => _multiQueue;

    public async Task CreateMultiRoomAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!await CanStartAsync().ConfigureAwait(false)) return;
            var seed = ReadLocal()?.SongId ??
                       (await GetLocalQueueAsync(CancellationToken.None).ConfigureAwait(false)).FirstOrDefault()?.ActualId;
            if (seed is null)
            {
                await UpdateMessageAsync("请先播放一首歌，再来创建多人房间吧~").ConfigureAwait(false);
                return;
            }

            await UpdateAsync(ListenTogetherPhase.Connecting, "正在创建多人房间…").ConfigureAwait(false);
            var room = await _multiProvider.CreateMultiListenTogetherRoomAsync(seed).ConfigureAwait(false);
            await StartMultiSessionAsync(room, "多人房间已创建，可以邀请好友一起听啦~").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Create multi listen together failed: {ex}");
            await UpdateAsync(ListenTogetherPhase.Idle, $"创建多人房间失败：{ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecommendAsync(SingleSongBase song)
    {
        if (_multi is not { } multi) return;
        if (!IsRoomSong(song))
        {
            _notification.ShowMessage("无法推荐", NonSongMessage);
            return;
        }

        try
        {
            var result = await _multiProvider.RecommendMultiListenTogetherItemAsync(multi.RoomId, song.ActualId!)
                .ConfigureAwait(false);
            _notification.ShowMessage(result.Succeeded ? "已推荐到房间" : "推荐失败",
                string.IsNullOrEmpty(result.Message) ? song.Name : result.Message);
            if (result.Succeeded) multi.QueueVersion = -1;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Multi listen together recommend failed: {ex}");
            _notification.ShowMessage("推荐失败", ex.Message);
        }
    }

    public async Task RequestSkipAsync()
    {
        if (_multi is not { Playback: { } playback } multi) return;
        try
        {
            var result = await _multiProvider
                .RequestMultiListenTogetherSkipAsync(multi.RoomId, playback.ItemId, playback.EntryId)
                .ConfigureAwait(false);
            _notification.ShowMessage(result.Succeeded ? "想切歌" : "切歌失败",
                string.IsNullOrEmpty(result.Message) ? "已告诉大家你想切歌" : result.Message);
            if (result.Playback is not { } next || next.EntryId == playback.EntryId) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_multi, multi))
                    await ApplyMultiPlaybackAsync(multi, next, NowMs(), false, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Multi listen together skip request failed: {ex}");
            _notification.ShowMessage("切歌失败", ex.Message);
        }
    }

    private PlaybackDenial? DenyInMultiRoom(PlaybackRequest request, IReadOnlyList<SingleSongBase>? songs)
    {
        return request switch
        {
            PlaybackRequest.PlayItems when songs is [{ } song] && IsRoomSong(song) =>
                new PlaybackDenial("多人一起听由房间统一播放，可以把这首歌推荐到房间", "推荐到房间",
                    () => _taskRunner.Forget(RecommendAsync(song), "recommend song to multi listen together room")),
            PlaybackRequest.PlayItems or PlaybackRequest.PlayPodcast or PlaybackRequest.PlayLocal =>
                new PlaybackDenial("多人一起听由房间统一播放，不能直接播放其他歌曲哦~"),
            PlaybackRequest.PlayRadio => new PlaybackDenial("一起听期间不能开启私人 FM 或心动模式"),
            PlaybackRequest.SkipTrack when CanRequestSkip =>
                new PlaybackDenial("多人一起听不能直接切歌哦~", "想切歌",
                    () => _taskRunner.Forget(RequestSkipAsync(), "request skip in multi listen together room")),
            PlaybackRequest.SkipTrack => new PlaybackDenial("多人一起听不能直接切歌哦"),
            PlaybackRequest.Seek => new PlaybackDenial("多人一起听的进度由房间统一控制"),
            PlaybackRequest.ChangePlayMode => new PlaybackDenial("多人一起听不能切换播放模式"),
            PlaybackRequest.ChangeSpeed => new PlaybackDenial(SpeedLockedMessage),
            _ => null
        };
    }

    private async Task JoinMultiRoomCoreAsync(ListenTogetherInvitation invitation)
    {
        await UpdateAsync(ListenTogetherPhase.Connecting, "正在加入多人房间…").ConfigureAwait(false);
        var room = await _multiProvider.JoinMultiListenTogetherRoomAsync(invitation.RoomId, invitation.InviterId)
            .ConfigureAwait(false);
        await StartMultiSessionAsync(room, "已加入多人房间").ConfigureAwait(false);
    }

    private async Task StartMultiSessionAsync(ProviderMultiListenTogetherRoom room, string message)
    {
        var multi = new MultiSession
        {
            RoomId = room.RoomId,
            CreatorId = room.CreatorId,
            UserId = MyUserId,
            StartedAtMs = NowMs(),
            PreviousPlayModeId = _state.ActiveStrategyId,
            CanRecommend = room.CanRecommend,
            CanRequestSkip = room.CanRequestSkip
        };
        _multi = multi;
        _settings.ListenTogetherLocalRoomId = room.RoomId;
        _restriction.Apply(this);
        ResetPlaybackSpeed();
        try
        {
            using (_restriction.Exemption())
            {
                await _control.SetTransitionAsync("dir").ConfigureAwait(false);
                if (_state.ActiveStrategyId != "seq") await _control.SetPlayModeAsync("seq").ConfigureAwait(false);
            }

            _roomId = room.RoomId;
            _isHost = !string.IsNullOrEmpty(room.CreatorId) && room.CreatorId == multi.UserId;
            _shareLink = ListenTogetherInvitation.BuildMultiShareLink(room.RoomId, multi.UserId ?? room.CreatorId ?? "0");
            _users = room.Users;
            multi.NextStatusMs = NowMs() + (long)MultiStatusInterval.TotalMilliseconds;
            await NotifyAsync(nameof(RoomId), nameof(IsHost), nameof(ShareLink), nameof(Users), nameof(IsMulti),
                nameof(IsDualRoom), nameof(CanRequestSkip)).ConfigureAwait(false);
            if (room.Playback is { } playback)
                await ApplyMultiPlaybackAsync(multi, playback, NowMs(), true, CancellationToken.None).ConfigureAwait(false);
            await RefreshMultiQueueAsync(multi, CancellationToken.None).ConfigureAwait(false);
            StartMultiLoop(multi);
            await UpdateAsync(ListenTogetherPhase.Connected, message).ConfigureAwait(false);
        }
        catch
        {
            await CloseMultiAsync(multi, "多人一起听连接失败").ConfigureAwait(false);
            throw;
        }
    }

    private void StartMultiLoop(MultiSession multi)
    {
        var loop = RunMultiAsync(multi, multi.Cts.Token);
        multi.Loop = loop;
        _taskRunner.Forget(loop, "multi listen together sync");
    }

    private async Task RunMultiAsync(MultiSession multi, CancellationToken ct)
    {
        using var exemption = _restriction.Exemption();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(multi.Failures == 0 ? MultiTickInterval : Backoff(multi.Failures), ct).ConfigureAwait(false);
                await _gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(_multi, multi) || ct.IsCancellationRequested) return;
                    if (MyUserId is not { } userId || userId != multi.UserId)
                    {
                        await CloseMultiAsync(multi, "登录账号已变化，已退出一起听").ConfigureAwait(false);
                        return;
                    }

                    await TickMultiAsync(multi, ct).ConfigureAwait(false);
                    if (!ReferenceEquals(_multi, multi)) return;
                    if (multi.Failures > 0)
                        await UpdateAsync(ListenTogetherPhase.Connected, "已恢复连接").ConfigureAwait(false);
                    multi.Failures = 0;
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
                System.Diagnostics.Debug.WriteLine($"Multi listen together sync failed: {ex}");
                multi.Failures++;
                if (multi.Failures >= MaxConsecutiveFailures)
                {
                    await UpdateAsync(ListenTogetherPhase.Disconnected, $"与房间的连接已断开：{ex.Message}")
                        .ConfigureAwait(false);
                    return;
                }

                await UpdateAsync(ListenTogetherPhase.Reconnecting, "网络不稳定，正在重试…").ConfigureAwait(false);
            }
        }
    }

    private async Task TickMultiAsync(MultiSession multi, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref multi.FailedSongId, null) is { } failed)
            await HoldMultiAsync(multi, failed, SongAccess.Unavailable).ConfigureAwait(false);

        var sentAt = NowMs();
        var heartbeat = await _multiProvider.SendMultiListenTogetherHeartbeatAsync(multi.RoomId, ct).ConfigureAwait(false);
        if (heartbeat.IsClosed)
        {
            await CloseMultiAsync(multi, "多人一起听已结束").ConfigureAwait(false);
            _notification.ShowMessage("一起听已结束", "多人房间已关闭");
            return;
        }

        if (heartbeat.Playback is { } playback)
            await ApplyMultiPlaybackAsync(multi, playback, (sentAt + NowMs()) / 2, false, ct).ConfigureAwait(false);
        if (NowMs() >= multi.NextStatusMs)
        {
            await RefreshMultiStatusAsync(multi, ct).ConfigureAwait(false);
            if (!ReferenceEquals(_multi, multi)) return;
        }

        if (multi.Playback is { } current && current.Version != multi.QueueVersion)
            await RefreshMultiQueueAsync(multi, ct).ConfigureAwait(false);
    }

    private async Task RefreshMultiStatusAsync(MultiSession multi, CancellationToken ct)
    {
        multi.NextStatusMs = NowMs() + (long)MultiStatusInterval.TotalMilliseconds;
        var room = await _multiProvider.GetMultiListenTogetherRoomAsync(ct).ConfigureAwait(false);
        if (room is null || room.RoomId != multi.RoomId)
        {
            if (NowMs() - multi.StartedAtMs < MembershipGrace.TotalMilliseconds) return;
            if (++multi.MissingStatus < MissingStatusLimit) return;
            await CloseMultiAsync(multi, "你已离开多人一起听").ConfigureAwait(false);
            _notification.ShowMessage("一起听已结束", "你已不在这个多人房间里");
            return;
        }

        multi.MissingStatus = 0;
        multi.CanRecommend = room.CanRecommend;
        multi.CanRequestSkip = room.CanRequestSkip;
        AnnounceMemberChanges(_users, room.Users, multi.UserId);
        _users = room.Users;
        _isHost = !string.IsNullOrEmpty(room.CreatorId) && room.CreatorId == multi.UserId;
        await NotifyAsync(nameof(Users), nameof(IsHost), nameof(CanRequestSkip)).ConfigureAwait(false);
    }

    private async Task ApplyMultiPlaybackAsync(MultiSession multi, ProviderMultiListenTogetherPlayback playback,
        long receivedAtMs, bool initial, CancellationToken ct)
    {
        using var exemption = _restriction.Exemption();
        var previous = multi.Playback;
        if (previous is not null && ListenTogetherPlanner.IsStaleRoomPlayback(previous.Version, playback.Version)) return;
        multi.Playback = playback;
        multi.PlaybackAtMs = receivedAtMs;
        var expected = playback.Position + TimeSpan.FromMilliseconds(Math.Max(0, NowMs() - receivedAtMs));
        var remaining = playback.Duration - expected;
        if (multi.HeldItemId == playback.ItemId) return;
        multi.HeldItemId = null;

        var local = ReadLocal();
        if (local?.SongId != playback.ItemId)
        {
            if (!initial && _state.IsLoadingTrack) return;
            if (ListenTogetherPlanner.ShouldWaitForRoom(local?.SongId, playback.NextItemIds.FirstOrDefault(),
                    playback.Duration, remaining))
                return;
            var access = await CheckAccessAsync(playback.ItemId, ct).ConfigureAwait(false);
            if (access != SongAccess.Full)
            {
                await HoldMultiAsync(multi, playback.ItemId, access).ConfigureAwait(false);
                return;
            }

            multi.UserPaused = false;
            if (!await SwitchToSongAsync(playback.ItemId, true, ct).ConfigureAwait(false))
            {
                await UpdateMessageAsync("无法获取房间正在播放的歌曲").ConfigureAwait(false);
                return;
            }

            if (!initial && previous is not null && previous.EntryId != playback.EntryId) ShowMultiSongTip(playback);
            if (expected > MultiDriftTolerance && (playback.Duration <= TimeSpan.Zero || remaining > TimeSpan.FromSeconds(2)))
                await _control.SeekAsync(expected).ConfigureAwait(false);
            return;
        }

        if (_state.IsLoadingTrack) return;
        if (!_state.IsPlaying)
        {
            if (initial) multi.UserPaused = false;
            if (!multi.UserPaused) _control.Play();
            return;
        }

        if ((_state.Position - expected).Duration() < MultiDriftTolerance) return;
        if (playback.Duration > TimeSpan.Zero && remaining < TimeSpan.FromSeconds(1)) return;
        await _control.SeekAsync(expected).ConfigureAwait(false);
    }

    private void OnMultiPlaybackStateChanged()
    {
        if (_multi is not { } multi || _state.IsLoadingTrack) return;
        if (_state.IsPlaying)
        {
            var resumedByUser = multi.UserPaused;
            multi.UserPaused = false;
            _taskRunner.Forget(SyncMultiAfterResumeAsync(multi, resumedByUser), "sync multi listen together after resume");
            return;
        }

        if (multi.HeldItemId is not null || multi.Playback is not { } playback || ReadLocal()?.SongId != playback.ItemId)
            return;
        if (_state.Duration > TimeSpan.Zero && _state.Position >= _state.Duration - TimeSpan.FromSeconds(2)) return;
        multi.UserPaused = true;
    }

    private async Task SyncMultiAfterResumeAsync(MultiSession multi, bool announce)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_multi, multi) || multi.Playback is not { } playback || _state.IsLoadingTrack) return;
            if (ReadLocal()?.SongId != playback.ItemId) return;
            using var exemption = _restriction.Exemption();
            var expected = playback.Position + TimeSpan.FromMilliseconds(Math.Max(0, NowMs() - multi.PlaybackAtMs));
            if (playback.Duration > TimeSpan.Zero && expected >= playback.Duration - TimeSpan.FromSeconds(1)) return;
            if ((_state.Position - expected).Duration() >= MultiDriftTolerance)
                await _control.SeekAsync(expected).ConfigureAwait(false);
            if (announce) _notification.ShowMessage("一起听", "已自动同步一起听进度");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Multi listen together resume sync failed: {ex}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HoldMultiAsync(MultiSession multi, string itemId, SongAccess access)
    {
        multi.HeldItemId = itemId;
        if (_state.IsPlaying) _control.Pause();
        var detail = $"{DescribeAccess(access)}，房间切到下一首后会自动跟上";
        await UpdateMessageAsync(detail).ConfigureAwait(false);
        _notification.ShowMessage("房间正在播放的歌曲本机无法收听", detail);
    }

    private async Task RefreshMultiQueueAsync(MultiSession multi, CancellationToken ct)
    {
        using var exemption = _restriction.Exemption();
        var items = await _multiProvider.GetMultiListenTogetherQueueAsync(multi.RoomId, ct).ConfigureAwait(false);
        if (!ReferenceEquals(_multi, multi)) return;
        multi.QueueVersion = multi.Playback?.Version ?? -1;
        if (multi.KnownEntries is { } known)
            foreach (var item in items.Where(item => !known.Contains(item.EntryId) && !item.IsSystemRecommendation &&
                                                     item.RecommenderId != multi.UserId))
                ShowTip($"{item.RecommenderName} 推荐了《{item.Title}》");
        multi.KnownEntries = items.Select(item => item.EntryId).ToHashSet();
        _multiQueue = items;
        await NotifyAsync(nameof(MultiQueue)).ConfigureAwait(false);

        var ids = new List<string>();
        if (multi.Playback is { } playback) ids.Add(playback.ItemId);
        ids.AddRange(items.Select(item => item.ItemId).Where(id => !ids.Contains(id)));
        var key = ListenTogetherPlanner.QueueKey(ids);
        if (ids.Count == 0 || key == multi.LocalQueueKey) return;
        var songs = await _provider.GetListenTogetherQueueSongsAsync(ids, ct).ConfigureAwait(false);
        if (songs.Count > 0) await ReplaceLocalQueueAsync(songs, ct).ConfigureAwait(false);
        multi.LocalQueueKey = key;
    }

    private void ShowMultiSongTip(ProviderMultiListenTogetherPlayback playback)
    {
        if (!_settings.ListenTogetherRemoteTips) return;
        var title = _multiQueue.FirstOrDefault(item => item.EntryId == playback.EntryId)?.Title ??
                    _state.NowPlayingProviderItem?.Name ?? "下一首";
        var recommender = _users.FirstOrDefault(user => user.UserId == playback.RecommenderId)?.Nickname;
        ShowTip(string.IsNullOrEmpty(recommender) ? $"房间切到了《{title}》" : $"房间切到了《{title}》（{recommender} 推荐）");
    }

    private async Task LeaveMultiRoomAsync()
    {
        MultiSession? multi;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            multi = _multi;
            if (multi is null) return;
            await UpdateAsync(ListenTogetherPhase.Ending, "正在退出多人一起听…").ConfigureAwait(false);
            multi.Cts.Cancel();
        }
        finally
        {
            _gate.Release();
        }

        if (multi.Loop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_multi, multi)) return;
            try
            {
                await _multiProvider.LeaveMultiListenTogetherRoomAsync(multi.RoomId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Multi listen together leave failed: {ex}");
                _notification.ShowMessage("退出多人一起听时出错", ex.Message);
            }

            await CloseMultiAsync(multi, "已退出多人一起听").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReconnectMultiAsync(MultiSession multi)
    {
        await UpdateAsync(ListenTogetherPhase.Reconnecting, "正在重新连接…").ConfigureAwait(false);
        multi.Failures = 0;
        multi.MissingStatus = 0;
        multi.Cts = new CancellationTokenSource();
        using (_restriction.Exemption())
            await TickMultiAsync(multi, multi.Cts.Token).ConfigureAwait(false);
        if (!ReferenceEquals(_multi, multi)) return;
        StartMultiLoop(multi);
        await UpdateAsync(ListenTogetherPhase.Connected, "已重新连接").ConfigureAwait(false);
    }

    private async Task CloseMultiAsync(MultiSession multi, string message)
    {
        if (!ReferenceEquals(_multi, multi)) return;
        _multi = null;
        _restriction.Apply(null);
        _settings.ListenTogetherLocalRoomId = string.Empty;
        multi.Cts.Cancel();
        await RestorePlaybackAsync(multi.PreviousPlayModeId).ConfigureAwait(false);
        _multiQueue = [];
        _roomId = string.Empty;
        _shareLink = string.Empty;
        _isHost = false;
        _users = [];
        _phase = ListenTogetherPhase.Idle;
        _statusMessage = message;
        await NotifyAsync(AllProperties).ConfigureAwait(false);
    }

    private sealed class MultiSession
    {
        public string? FailedSongId;
        public required string RoomId { get; init; }
        public string? CreatorId { get; init; }
        public string? UserId { get; init; }
        public long StartedAtMs { get; init; }
        public string? PreviousPlayModeId { get; init; }
        public CancellationTokenSource Cts { get; set; } = new();
        public Task? Loop { get; set; }
        public int Failures { get; set; }
        public int MissingStatus { get; set; }
        public bool CanRecommend { get; set; }
        public bool CanRequestSkip { get; set; }
        public ProviderMultiListenTogetherPlayback? Playback { get; set; }
        public string? HeldItemId { get; set; }
        public volatile bool UserPaused;
        public long PlaybackAtMs { get; set; }
        public long QueueVersion { get; set; } = -1;
        public HashSet<string>? KnownEntries { get; set; }
        public string? LocalQueueKey { get; set; }
        public long NextStatusMs { get; set; }
    }
}
