using System.Net.Http;
using AwesomeAssertions;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Lyrics.Services.ThirdParty;
using HyPlayer.Features.Playback.Services;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Lyric;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class LyricReloadTests
{
    [Test]
    public async Task Reload_fetches_new_lyrics_and_preserves_the_source_and_paused_position()
    {
        using var http = new HttpClient();
        var state = new PlaybackStateService();
        var settings = new LyricSettings();
        var provider = new RevisionProvider();
        var preferences = new LyricSourcePreferences(new Dictionary<string, object>());
        var service = new LyricService(provider, state, settings, new BackgroundTaskRunner(),
            new KawazuStateService(), new ThirdPartyLyricSources(http, settings), preferences);
        var song = new NeteaseRadioProgram { ActualId = "reload", Duration = 30_000 };
        preferences.Set(song, LyricSourceKind.Own);
        state.SetNowPlaying(song);
        await service.LoadLyricsAsync(song);
        state.LyricInfo.Lyrics[0].LyricLine.CurrentLyric.Should().Be("A intro");
        var position = state.Position = TimeSpan.FromSeconds(12);
        state.IsPlaying = false;

        await service.ReloadLyricsAsync(song);

        provider.Requests.Should().Be(2);
        state.LyricInfo.Lyrics[state.LyricIndex].LyricLine.CurrentLyric.Should().Be("B verse");
        state.IsLyricPending.Should().BeFalse();
        state.Position.Should().Be(position);
        state.IsPlaying.Should().BeFalse();
        service.GetLyricSourceChoice(song).Should().Be(LyricSourceKind.Own);
    }

    [Test]
    public async Task Reload_replaces_an_inflight_load_and_does_not_publish_to_the_next_song()
    {
        using var http = new HttpClient();
        var state = new PlaybackStateService();
        var settings = new LyricSettings();
        var provider = new GatedProvider();
        var service = new LyricService(provider, state, settings, new BackgroundTaskRunner(),
            new KawazuStateService(), new ThirdPartyLyricSources(http, settings), new LyricSourcePreferences());
        var song = new NeteaseRadioProgram { ActualId = "1" };
        state.SetNowPlaying(song);
        var first = service.LoadLyricsAsync(song);
        var timeout = TimeSpan.FromSeconds(5);
        await provider.FirstStarted.Task.WaitAsync(timeout);

        var refresh = service.ReloadLyricsAsync(song);
        try
        {
            await provider.SecondStarted.Task.WaitAsync(timeout);
            Func<Task> superseded = () => first.WaitAsync(timeout);
            await superseded.Should().ThrowAsync<OperationCanceledException>();
            state.SetNowPlaying(new NeteaseRadioProgram { ActualId = "2" });
            var newLyrics = state.LyricInfo = new HyLyricInfo();
            state.IsLyricPending = true;
            await service.ReloadLyricsAsync(song).WaitAsync(timeout);
            provider.Finish.TrySetCanceled();
            Func<Task> stopped = () => refresh.WaitAsync(timeout);
            await stopped.Should().ThrowAsync<OperationCanceledException>();
            state.LyricInfo.Should().BeSameAs(newLyrics);
            state.IsLyricPending.Should().BeTrue();
            provider.Requests.Should().Be(2);
        }
        finally
        {
            provider.Finish.TrySetCanceled();
        }
    }

    private abstract class TestProvider : ILyricProvidable
    {
        public string Id => "ncm";
        public string Name => "test";
        public List<ProvidableTypeId> ProvidableTypeIds => [];
        internal int Requests;
        public abstract Task<List<RawLyricInfo>> GetLyricInfoAsync(SingleSongBase song, CancellationToken ctk = default);
    }

    private sealed class RevisionProvider : TestProvider
    {
        public override Task<List<RawLyricInfo>> GetLyricInfoAsync(SingleSongBase song, CancellationToken ctk = default)
        {
            var revision = Interlocked.Increment(ref Requests) == 1 ? "A" : "B";
            return Task.FromResult<List<RawLyricInfo>>([new NeteaseRawLyricInfo
            {
                LyricTypeActual = LyricType.Original,
                LyricText = $"[00:00.000]{revision} intro\n[00:10.000]{revision} verse"
            }]);
        }
    }

    private sealed class GatedProvider : TestProvider
    {
        internal readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<List<RawLyricInfo>> Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<List<RawLyricInfo>> GetLyricInfoAsync(SingleSongBase song, CancellationToken ctk = default)
        {
            (Interlocked.Increment(ref Requests) == 1 ? FirstStarted : SecondStarted).TrySetResult();
            return Finish.Task.WaitAsync(ctk);
        }
    }
}
