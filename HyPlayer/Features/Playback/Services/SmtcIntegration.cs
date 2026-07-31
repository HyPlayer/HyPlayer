using System;
using System.Threading.Tasks;
using Windows.Media;

namespace HyPlayer.Features.Playback.Services;

internal sealed class SmtcPlaybackCommandDispatcher
{
    private readonly Func<Task> _moveNextAsync;
    private readonly Func<Task> _movePreviousAsync;
    private readonly Func<Task> _pauseAsync;
    private readonly Func<Task> _playAsync;

    public SmtcPlaybackCommandDispatcher(
        Func<Task> playAsync,
        Func<Task> pauseAsync,
        Func<Task> moveNextAsync,
        Func<Task> movePreviousAsync)
    {
        _playAsync = playAsync ?? throw new ArgumentNullException(nameof(playAsync));
        _pauseAsync = pauseAsync ?? throw new ArgumentNullException(nameof(pauseAsync));
        _moveNextAsync = moveNextAsync ?? throw new ArgumentNullException(nameof(moveNextAsync));
        _movePreviousAsync = movePreviousAsync ?? throw new ArgumentNullException(nameof(movePreviousAsync));
    }

    public Task DispatchAsync(SystemMediaTransportControlsButton button)
    {
        return button switch
        {
            SystemMediaTransportControlsButton.Play => _playAsync(),
            SystemMediaTransportControlsButton.Pause => _pauseAsync(),
            SystemMediaTransportControlsButton.Next => _moveNextAsync(),
            SystemMediaTransportControlsButton.Previous => _movePreviousAsync(),
            _ => Task.CompletedTask
        };
    }
}

internal static class SmtcTrackIdentity
{
    public static string? Create(string? providerId, string? actualId)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(actualId))
            return null;

        return $"{providerId.ToUpperInvariant()}-{actualId}";
    }
}