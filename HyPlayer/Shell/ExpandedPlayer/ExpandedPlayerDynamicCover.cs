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
    private StorageFile? _pendingVideo;
    private MediaPlayer? _player;
    private MediaPlayerSurface? _surface;
    private CompositionSurfaceBrush? _brush;
    private CompositionRoundedRectangleGeometry? _clipGeometry;
    private CompositionGeometricClip? _clip;
    private SpriteVisual? _visual;
    private MediaSource? _source;
    private int _generation;
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
        if (_player is not null)
        {
            _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            _player.MediaFailed -= OnMediaFailed;
        }

        _visual?.Dispose();
        _clip?.Dispose();
        _clipGeometry?.Dispose();
        _brush?.Dispose();
        _surface?.Dispose();
        _player?.Dispose();
        _visual = null;
        _clip = null;
        _clipGeometry = null;
        _brush = null;
        _surface = null;
        _player = null;
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
            if (_dispatcher.TryEnqueue(() => Apply(generation, accepted, video.File))) poster = null;
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

    private void Apply(int generation, InMemoryRandomAccessStream poster, StorageFile video)
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

    private void StartVideo(StorageFile video)
    {
        var target = FindTarget();
        if (target is null) return;
        EnsurePlayer();
        _source = MediaSource.CreateFromStorageFile(video);
        _player!.Source = _source;
        _visual!.Opacity = 0;
        _videoShown = false;
        ElementCompositionPreview.SetElementChildVisual(target, _visual);
        _videoAttached = true;
        UpdateLayout();
        if (_windowVisible) _player.Play();
    }

    private void StopVideo()
    {
        _videoShown = false;
        if (_videoAttached && _target is not null)
            ElementCompositionPreview.SetElementChildVisual(_target, null);
        _videoAttached = false;

        if (_player is not null)
        {
            _player.Pause();
            _player.Source = null;
        }

        _source?.Dispose();
        _source = null;
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

    private void EnsurePlayer()
    {
        if (_player is not null) return;
        var player = new MediaPlayer
        {
            AutoPlay = false,
            IsLoopingEnabled = true,
            IsMuted = true
        };
        player.CommandManager.IsEnabled = false;
        player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        player.MediaFailed += OnMediaFailed;
        _player = player;
        _surface = player.GetSurface(_compositor);
        _brush = _compositor.CreateSurfaceBrush(_surface.CompositionSurface);
        _brush.Stretch = CompositionStretch.UniformToFill;
        _clipGeometry = _compositor.CreateRoundedRectangleGeometry();
        _clip = _compositor.CreateGeometricClip(_clipGeometry);
        _visual = _compositor.CreateSpriteVisual();
        _visual.Brush = _brush;
        _visual.Clip = _clip;
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        if (sender.PlaybackState != MediaPlaybackState.Playing) return;
        var generation = Volatile.Read(ref _generation);
        _dispatcher.TryEnqueue(() => ShowVideo(generation));
    }

    private void ShowVideo(int generation)
    {
        if (_disposed || generation != _generation || _videoShown || _visual is null || _source is null) return;
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
        if (_player is null || _source is null) return;
        if (e.Visible) _player.Play();
        else _player.Pause();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(PlaybackSettings.DynamicCoverEnabled)
            or nameof(PlaybackSettings.DynamicCoverSource)))
            return;
        if (_dispatcher.HasThreadAccess) Restart();
        else _dispatcher.TryEnqueue(Restart);
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
