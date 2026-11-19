using System;
using System.Collections.Generic;
using System.Threading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.Services;

public enum PlaybackRequest
{
    PlayItems,
    PlayPodcast,
    PlayLocal,
    PlayRadio,
    SkipTrack,
    Seek,
    ChangePlayMode,
    ChangeSpeed
}

public sealed record PlaybackDenial(string Message, string? ActionText = null, Action? Action = null);

public interface IPlaybackRestrictionPolicy
{
    PlaybackDenial? Deny(PlaybackRequest request, IReadOnlyList<SingleSongBase>? songs);

    bool HandlePlaybackFailure(SingleSongBase? song);
}

public sealed class PlaybackRestriction
{
    private static readonly AsyncLocal<bool> Exempt = new();
    private readonly INotificationService _notification;
    private volatile IPlaybackRestrictionPolicy? _policy;

    public PlaybackRestriction(INotificationService notification)
    {
        _notification = notification;
    }

    public event EventHandler? PlayRequested;

    public bool IsActive => _policy is not null;

    public void Apply(IPlaybackRestrictionPolicy? policy)
    {
        _policy = policy;
    }

    public IDisposable Exemption()
    {
        var previous = Exempt.Value;
        Exempt.Value = true;
        return new ExemptionScope(previous);
    }

    public bool Allows(PlaybackRequest request, IReadOnlyList<SingleSongBase>? songs = null)
    {
        if (Exempt.Value) return true;
        if (_policy?.Deny(request, songs) is { } denial)
        {
            if (denial is { ActionText: { } actionText, Action: { } action })
                _notification.ShowAction("正在一起听", denial.Message, actionText, action);
            else
                _notification.ShowMessage("正在一起听", denial.Message);
            return false;
        }

        if (request is not (PlaybackRequest.Seek or PlaybackRequest.ChangePlayMode or PlaybackRequest.ChangeSpeed))
            PlayRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Allows(PlaybackRequest request, SingleSongBase song)
    {
        return Allows(request, [song]);
    }

    public bool Allows(MusicResource resource)
    {
        return Allows(resource is MusicResource.Radio ? PlaybackRequest.PlayPodcast : PlaybackRequest.PlayItems);
    }

    public bool HandlesPlaybackFailure(SingleSongBase? song)
    {
        return _policy?.HandlePlaybackFailure(song) == true;
    }

    private sealed class ExemptionScope(bool previous) : IDisposable
    {
        public void Dispose()
        {
            Exempt.Value = previous;
        }
    }
}
