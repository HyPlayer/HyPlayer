using System;
using System.ComponentModel;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.DynamicCover;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;

namespace HyPlayer.Shell.ExpandedPlayer;

internal sealed class ExpandedPlayerDynamicCover : IDisposable
{
    internal const double MaxSurfaceEdge = 1440;
    internal static readonly TimeSpan CrossfadeDuration = TimeSpan.FromMilliseconds(800);
    internal static readonly TimeSpan LoopCheckInterval = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan MinimumCrossfadeLoop = TimeSpan.FromSeconds(3);

    private readonly Control _host;
    private readonly IDynamicCoverService _service;
    private readonly PlaybackSettings _settings;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly Action _coverChanged;
    private readonly DispatcherQueue _dispatcher;
    private readonly Compositor _compositor;
    private FrameworkElement? _target;
    private LoadOperation? _loading;
    private SingleSongBase? _song;
    private string? _songKey;
    private DynamicCoverVideo? _pendingVideo;
    private VideoSlot? _active;
    private VideoSlot? _standby;
    private CompositionRoundedRectangleGeometry? _clipGeometry;
    private CompositionGeometricClip? _clip;
    private SpriteVisual? _visual;
    private SpriteVisual? _overlay;
    private DispatcherQueueTimer? _loopTimer;
    private CompositionScopedBatch? _fadeBatch;
    private int _generation;
    private bool _crossfade;
    private bool _videoAttached;
    private bool _videoShown;
    private bool _windowVisible;
    private bool _disposed;

    public ExpandedPlayerDynamicCover(
        Control host,
        IDynamicCoverService service,
        PlaybackSettings settings,
        IBackgroundTaskRunner taskRunner,
        Action coverChanged)
    {
        _host = host;
        _service = service;
        _settings = settings;
        _taskRunner = taskRunner;
        _coverChanged = coverChanged;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _windowVisible = Window.Current.Visible;
        _settings.PropertyChanged += OnSettingsChanged;
        Window.Current.VisibilityChanged += OnWindowVisibilityChanged;
    }

    public InMemoryRandomAccessStream? Poster { get; private set; }

    public void Load(SingleSongBase? song)
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => Load(song));
            return;
        }

        if (_disposed) return;
        var key = song?.ItemId;
        if (key is not null && key == _songKey) return;
        _song = song;
        _songKey = key;
        Restart();
    }

    public void OnCoverDisplayed(object cover)
    {
        if (_disposed || Poster is null || !ReferenceEquals(cover, Poster) || _pendingVideo is not { } video) return;
        _pendingVideo = null;
        StartVideo(video);
    }

    public void UpdateLayout()
    {
        if (_visual is null || _clipGeometry is null || _target is null) return;
        var size = new Vector2((float)_target.ActualWidth, (float)_target.ActualHeight);
        _visual.Size = size;
        _clipGeometry.Size = size;
        var border = _host.BorderThickness;
        var radius = _host.CornerRadius.TopLeft - Math.Max(border.Left, border.Top);
        _clipGeometry.CornerRadius = new Vector2((float)Math.Clamp(radius, 0, Math.Min(size.X, size.Y) / 2));
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsChanged;
        Window.Current.VisibilityChanged -= OnWindowVisibilityChanged;
        if (_target is not null) _target.SizeChanged -= OnTargetSizeChanged;
        _target = null;
        if (_loopTimer is not null)
        {
            _loopTimer.Stop();
            _loopTimer.Tick -= OnLoopTimerTick;
            _loopTimer = null;
        }

        DisposeSlot(_active);
        DisposeSlot(_standby);
        _active = null;
        _standby = null;
        _overlay?.Dispose();
        _visual?.Dispose();
        _clip?.Dispose();
        _clipGeometry?.Dispose();
        _overlay = null;
        _visual = null;
        _clip = null;
        _clipGeometry = null;
    }

    private void Restart()
    {
        if (_disposed) return;
        var hadPoster = Reset();
        if (_settings.DynamicCoverEnabled && _song is { } song)
        {
            var loading = new LoadOperation();
            _loading = loading;
            _taskRunner.Forget(LoadAsync(song, _settings.DynamicCoverSource, _generation, loading),
                "load dynamic cover");
        }

        if (hadPoster) _coverChanged();
    }

    private async Task LoadAsync(
        SingleSongBase song,
        DynamicCoverSource source,
        int generation,
        LoadOperation loading)
    {
        InMemoryRandomAccessStream? poster = null;
        var ct = loading.Token;
        try
        {
            var video = await _service.ResolveAsync(song, source, ct).ConfigureAwait(false);
            if (video is null) return;
            poster = await _service.CreatePosterAsync(video, ct).ConfigureAwait(false);
            if (poster is null) return;
            var accepted = poster;
            if (_dispatcher.TryEnqueue(() => Apply(generation, accepted, video))) poster = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            poster?.Dispose();
            loading.Complete();
        }
    }

    private void Apply(int generation, InMemoryRandomAccessStream poster, DynamicCoverVideo video)
    {
        if (_disposed || generation != _generation)
        {
            poster.Dispose();
            return;
        }

        Poster = poster;
        _pendingVideo = video;
        _coverChanged();
    }

    private bool Reset()
    {
        Interlocked.Increment(ref _generation);
        _loading?.Cancel();
        _loading = null;
        StopVideo();
        _pendingVideo = null;
        var poster = Poster;
        Poster = null;
        poster?.Dispose();
        return poster is not null;
    }

    private void StartVideo(DynamicCoverVideo video)
    {
        var target = FindTarget();
        if (target is null) return;
        EnsureVisuals();
        _crossfade = _settings.DynamicCoverLoopCrossfade && video.Source == DynamicCoverSource.Netease;
        _active ??= CreateSlot();
        _active.Load(video.File, !_crossfade);
        _visual!.Brush = _active.Brush;
        if (_crossfade)
        {
            _standby ??= CreateSlot();
            _standby.Load(video.File, false);
            _overlay!.StopAnimation("Opacity");
            _overlay.Opacity = 0;
            _overlay.Brush = _standby.Brush;
            EnsureLoopTimer().Start();
        }

        _visual.Opacity = 0;
        _videoShown = false;
        ElementCompositionPreview.SetElementChildVisual(target, _visual);
        _videoAttached = true;
        UpdateLayout();
        if (_windowVisible) _active.Player.Play();
    }

    private void StopVideo()
    {
        _videoShown = false;
        _loopTimer?.Stop();
        CancelCrossfade();
        _crossfade = false;
        if (_videoAttached && _target is not null)
            ElementCompositionPreview.SetElementChildVisual(_target, null);
        _videoAttached = false;
        _active?.Unload();
        _standby?.Unload();
    }

    private void CancelCrossfade()
    {
        _fadeBatch?.Dispose();
        _fadeBatch = null;
        if (_overlay is null) return;
        _overlay.StopAnimation("Opacity");
        _overlay.Opacity = 0;
    }

    private FrameworkElement? FindTarget()
    {
        if (_target is not null) return _target;
        _target = FindChild(_host, "Image");
        if (_target is not null) _target.SizeChanged += OnTargetSizeChanged;
        return _target;
    }

    private static FrameworkElement? FindChild(DependencyObject parent, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var childName } element && childName == name) return element;
            if (FindChild(child, name) is { } nested) return nested;
        }

        return null;
    }

    private void OnTargetSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayout();
    }

    private void EnsureVisuals()
    {
        if (_visual is not null) return;
        _clipGeometry = _compositor.CreateRoundedRectangleGeometry();
        _clip = _compositor.CreateGeometricClip(_clipGeometry);
        _visual = _compositor.CreateSpriteVisual();
        _visual.Clip = _clip;
        _overlay = _compositor.CreateSpriteVisual();
        _overlay.RelativeSizeAdjustment = Vector2.One;
        _overlay.Opacity = 0;
        _visual.Children.InsertAtTop(_overlay);
    }

    private VideoSlot CreateSlot()
    {
        var slot = new VideoSlot(_compositor);
        slot.Player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        slot.Player.PlaybackSession.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
        slot.Player.MediaFailed += OnMediaFailed;
        slot.Player.MediaEnded += OnMediaEnded;
        return slot;
    }

    private void DisposeSlot(VideoSlot? slot)
    {
        if (slot is null) return;
        slot.Player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        slot.Player.PlaybackSession.NaturalVideoSizeChanged -= OnNaturalVideoSizeChanged;
        slot.Player.MediaFailed -= OnMediaFailed;
        slot.Player.MediaEnded -= OnMediaEnded;
        slot.Dispose();
    }

    private DispatcherQueueTimer EnsureLoopTimer()
    {
        if (_loopTimer is not null) return _loopTimer;
        _loopTimer = _dispatcher.CreateTimer();
        _loopTimer.Interval = LoopCheckInterval;
        _loopTimer.IsRepeating = true;
        _loopTimer.Tick += OnLoopTimerTick;
        return _loopTimer;
    }

    private void OnLoopTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || !_crossfade || _fadeBatch is not null || _active is null || _standby is null ||
            _overlay is null)
            return;
        var session = _active.Player.PlaybackSession;
        if (session.PlaybackState != MediaPlaybackState.Playing ||
            !ShouldStartCrossfade(session.Position, session.NaturalDuration))
            return;
        BeginCrossfade(_standby, _overlay);
    }

    internal static bool ShouldStartCrossfade(TimeSpan position, TimeSpan duration)
    {
        return duration >= MinimumCrossfadeLoop &&
               duration - position <= CrossfadeDuration + LoopCheckInterval;
    }

    private void BeginCrossfade(VideoSlot standby, SpriteVisual overlay)
    {
        standby.Player.PlaybackSession.Position = TimeSpan.Zero;
        standby.Player.Play();
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = CrossfadeDuration;
        overlay.StartAnimation("Opacity", fade);
        batch.End();
        _fadeBatch = batch;
        var generation = _generation;
        batch.Completed += (_, _) => CompleteCrossfade(generation, batch);
    }

    private void CompleteCrossfade(int generation, CompositionScopedBatch batch)
    {
        if (_disposed || generation != _generation || !ReferenceEquals(batch, _fadeBatch) ||
            _active is null || _standby is null || _visual is null || _overlay is null)
            return;
        _fadeBatch = null;
        batch.Dispose();
        (_active, _standby) = (_standby, _active);
        _visual.Brush = _active.Brush;
        _overlay.StopAnimation("Opacity");
        _overlay.Opacity = 0;
        _overlay.Brush = _standby.Brush;
        _standby.Player.Pause();
        _standby.Player.PlaybackSession.Position = TimeSpan.Zero;
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        if (sender.PlaybackState != MediaPlaybackState.Playing) return;
        var generation = Volatile.Read(ref _generation);
        _dispatcher.TryEnqueue(() => ShowVideo(generation));
    }

    private void OnNaturalVideoSizeChanged(MediaPlaybackSession sender, object args)
    {
        var size = SurfaceSizeFor(sender.NaturalVideoWidth, sender.NaturalVideoHeight);
        if (size is not { } surfaceSize) return;
        var player = sender.MediaPlayer;
        var generation = Volatile.Read(ref _generation);
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed && generation == _generation) player.SetSurfaceSize(surfaceSize);
        });
    }

    internal static Size? SurfaceSizeFor(uint width, uint height)
    {
        if (width == 0 || height == 0) return null;
        var scale = Math.Min(1, MaxSurfaceEdge / Math.Max(width, height));
        return new Size(Math.Max(1, Math.Round(width * scale)), Math.Max(1, Math.Round(height * scale)));
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        var generation = Volatile.Read(ref _generation);
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || generation != _generation || !_crossfade || _fadeBatch is not null ||
                !ReferenceEquals(sender, _active?.Player))
                return;
            sender.PlaybackSession.Position = TimeSpan.Zero;
            if (_windowVisible) sender.Play();
        });
    }

    private void ShowVideo(int generation)
    {
        if (_disposed || generation != _generation || _videoShown || _visual is null || _active?.Source is null) return;
        _videoShown = true;
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(320);
        _visual.StartAnimation("Opacity", fade);
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        System.Diagnostics.Debug.WriteLine($"Dynamic cover playback failed: {args.Error} {args.ErrorMessage}");
        var generation = Volatile.Read(ref _generation);
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed && generation == _generation) StopVideo();
        });
    }

    private void OnWindowVisibilityChanged(object sender, VisibilityChangedEventArgs e)
    {
        _windowVisible = e.Visible;
        if (_active?.Source is null) return;
        if (e.Visible)
        {
            _active.Player.Play();
            if (_fadeBatch is not null) _standby?.Player.Play();
        }
        else
        {
            _active.Player.Pause();
            if (_fadeBatch is not null) _standby?.Player.Pause();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(PlaybackSettings.DynamicCoverEnabled)
            or nameof(PlaybackSettings.DynamicCoverSource)
            or nameof(PlaybackSettings.DynamicCoverLoopCrossfade)))
            return;
        if (_dispatcher.HasThreadAccess) Restart();
        else _dispatcher.TryEnqueue(Restart);
    }

    private sealed class VideoSlot : IDisposable
    {
        public VideoSlot(Compositor compositor)
        {
            Player = new MediaPlayer
            {
                AutoPlay = false,
                IsMuted = true
            };
            Player.CommandManager.IsEnabled = false;
            Surface = Player.GetSurface(compositor);
            Brush = compositor.CreateSurfaceBrush(Surface.CompositionSurface);
            Brush.Stretch = CompositionStretch.UniformToFill;
        }

        public MediaPlayer Player { get; }
        public MediaPlayerSurface Surface { get; }
        public CompositionSurfaceBrush Brush { get; }
        public MediaSource? Source { get; private set; }

        public void Load(StorageFile file, bool loop)
        {
            Unload();
            Player.IsLoopingEnabled = loop;
            Source = MediaSource.CreateFromStorageFile(file);
            Player.Source = Source;
        }

        public void Unload()
        {
            Player.Pause();
            Player.Source = null;
            Source?.Dispose();
            Source = null;
        }

        public void Dispose()
        {
            Unload();
            Brush.Dispose();
            Surface.Dispose();
            Player.Dispose();
        }
    }

    private sealed class LoadOperation
    {
        private readonly CancellationTokenSource _source = new();
        private readonly object _gate = new();
        private bool _completed;

        public LoadOperation()
        {
            Token = _source.Token;
        }

        public CancellationToken Token { get; }

        public void Cancel()
        {
            lock (_gate)
            {
                if (!_completed) _source.Cancel();
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
                _source.Dispose();
            }
        }
    }
}
