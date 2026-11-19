using System.Net;
using System.Net.Http;
using AwesomeAssertions;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Services;
using HyPlayer.NeteaseProvider.Models;
using TUnit.Core;
using Windows.Storage.Streams;

namespace HyPlayer.Playback.Tests;

public sealed class CoverReloadTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Manual_refresh_fetches_fresh_bytes_instead_of_the_preload_without_moving_playback()
    {
        var handler = new CoverHandler((request, _) => Task.FromResult((byte)request));
        using var http = new HttpClient(handler);
        var state = new PlaybackStateService();
        var service = CreateService(state, http);
        var song = Song("first");
        state.SetNowPlaying(song);
        state.Position = TimeSpan.FromSeconds(37);
        state.IsPlaying = true;
        try
        {
            service.PreloadCover(song);
            await service.RefreshCoverAsync(song, forceRefresh: true).WaitAsync(Timeout);

            handler.Requests.Should().Be(2);
            state.CoverStream.Should().NotBeNull();
            using var reader = new DataReader(state.CoverStream!.GetInputStreamAt(0));
            (await reader.LoadAsync(1)).Should().Be(1);
            reader.ReadByte().Should().Be(2);
            state.NowPlayingProviderItem.Should().BeSameAs(song);
            state.Position.Should().Be(TimeSpan.FromSeconds(37));
            state.IsPlaying.Should().BeTrue();
        }
        finally
        {
            state.ClearNowPlaying();
        }
    }

    [Test]
    public async Task Old_cover_and_a_delayed_click_cannot_publish_after_a_track_change()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CoverHandler((_, ct) =>
        {
            started.TrySetResult();
            return release.Task.WaitAsync(ct);
        });
        using var http = new HttpClient(handler);
        var state = new PlaybackStateService();
        var service = CreateService(state, http);
        var old = Song("old");
        state.SetNowPlaying(old);
        try
        {
            var pending = service.RefreshCoverAsync(old, forceRefresh: true);
            await started.Task.WaitAsync(Timeout);
            state.SetNowPlaying(Song("next"));
            release.TrySetResult(42);
            await pending.WaitAsync(Timeout);
            await service.RefreshCoverAsync(old, forceRefresh: true).WaitAsync(Timeout);

            state.CoverStream.Should().BeNull();
            state.CoverStreamReference.Should().BeNull();
            handler.Requests.Should().Be(1);
        }
        finally
        {
            release.TrySetCanceled();
            state.ClearNowPlaying();
        }
    }

    private static NeteaseSong Song(string id) =>
        new() { ActualId = id, Artists = [], CoverUrl = $"https://cover.invalid/{id}.jpg" };

    private static PlaybackNotificationService CreateService(PlaybackStateService state, HttpClient http) =>
        new(state, new ImageSettings(), new LastFMSettings(), http, null!, null!, null!, null!);

    private sealed class ImageSettings : UISettings
    {
        protected override string SectionName => "tests.coverReload";
    }

    private sealed class CoverHandler(Func<int, CancellationToken, Task<byte>> respond) : HttpMessageHandler
    {
        internal int Requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var marker = await respond(Interlocked.Increment(ref Requests), ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([marker]) };
        }
    }
}
