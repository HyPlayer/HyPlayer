using System;
using System.Globalization;

namespace HyPlayer.Features.Playback.Services;

public sealed record ToastLyricContent(string Title, string Lyric, string Translation,
    string Position, string Duration, string Progress)
{
    public static ToastLyricContent FromState(PlaybackStateService state)
    {
        var lyrics = state.LyricInfo.Lyrics;
        var index = state.LyricIndex;
        var line = index >= 0 && index < lyrics.Count ? lyrics[index] : null;
        var translation = line?.Translation;
        if (string.IsNullOrWhiteSpace(translation))
            translation = index >= 0 && index + 1 < lyrics.Count
                ? lyrics[index + 1].LyricLine?.CurrentLyric : string.Empty;
        var duration = Math.Max(0, state.Duration.TotalSeconds);
        var position = Math.Clamp(state.Position.TotalSeconds, 0, duration);
        return new ToastLyricContent(
            state.NowPlayingSnapshot?.Name ?? state.NowPlayingProviderItem?.Name ?? "当前无音乐播放",
            line?.LyricLine?.CurrentLyric ?? (state.IsLyricPending ? "加载歌词中…" : "当前无歌词"),
            translation ?? string.Empty,
            FormatTime(position), FormatTime(duration),
            (duration > 0 ? position / duration : 0).ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : time.ToString(@"m\:ss");
    }
}
