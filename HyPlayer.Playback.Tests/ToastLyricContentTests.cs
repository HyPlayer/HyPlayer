using System.Globalization;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Features.Playback.Services;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class ToastLyricContentTests
{
    [Test]
    public void MissingAndOutOfRangeLyrics_ShouldNotThrowOrRetainPreviousText()
    {
        var state = new PlaybackStateService { LyricIndex = -1 };
        var content = ToastLyricContent.FromState(state);
        Ensure(content.Lyric == "当前无歌词" && content.Translation == "" && content.Progress == "0");
        state.IsLyricPending = true;
        state.LyricIndex = 99;
        content = ToastLyricContent.FromState(state);
        Ensure(content.Lyric == "加载歌词中…" && content.Translation == "");
    }

    [Test]
    public void Lyrics_ShouldPreferTranslationAndOtherwiseUseNextLine()
    {
        var state = new PlaybackStateService
        {
            LyricInfo = new HyLyricInfo { Lyrics = [Line("original", "translation"), Line("next")] }
        };
        Ensure(ToastLyricContent.FromState(state).Translation == "translation");
        state.LyricInfo.Lyrics[0].Translation = "";
        Ensure(ToastLyricContent.FromState(state).Translation == "next");
        state.LyricIndex = 1;
        Ensure(ToastLyricContent.FromState(state).Translation == "");
        Ensure(ToastLyricContent.FromState(state).Lyric == "next");
    }

    [Test]
    public void Progress_ShouldUseInvariantCultureAndClampSeeking()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var state = new PlaybackStateService { Position = TimeSpan.FromSeconds(30), Duration = TimeSpan.FromMinutes(1) };
            Ensure(ToastLyricContent.FromState(state).Progress == "0.5");
            state.Position = TimeSpan.FromMinutes(2);
            Ensure(ToastLyricContent.FromState(state).Progress == "1");
            state.Position = TimeSpan.FromSeconds(-2);
            Ensure(ToastLyricContent.FromState(state).Position == "0:00");
            state.Duration = TimeSpan.Zero;
            Ensure(ToastLyricContent.FromState(state).Progress == "0");
            state.Position = state.Duration = TimeSpan.FromSeconds(3661);
            Ensure(ToastLyricContent.FromState(state).Duration == "1:01:01");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static SongLyric Line(string text, string translation = "") => new()
    {
        LyricLine = new LrcLyricsLine(text, TimeSpan.Zero), Translation = translation
    };

    private static void Ensure(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Unexpected toast lyric content.");
    }
}
