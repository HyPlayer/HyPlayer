using HyPlayer.UWP.Chopin.Utils;
using Microsoft.Graphics.Canvas;
using System;
using System.Diagnostics;
using System.Numerics;
using Windows.Graphics.DirectX;

namespace HyPlayer.UI.Effects.LikeApple
{
    /// <summary>
    /// Direct3D recreation of Apple Music's TSLBackdrop lyric material.
    /// It follows the original four-stage Metal pipeline: three rotating
    /// artwork instances, the quarter-resolution iOS Gaussian blur, then
    /// the animated subdivided pinch mesh and its final saturation/scrim transfer.
    /// </summary>
    public sealed partial class LikeAppleBackgroundRenderer : IDisposable
    {
        private const double ArtworkTransitionSeconds = 0.5;
        private const double LyricsModeTransitionSeconds = 0.25;
        private const int BlurSurfaceDownsample = 4;
        private const float GaussianKernelSigma = 42.5f;
        private const float LyricsBlurSigma = 42.5f;
        private const float OrdinaryBlurSigma = 80f;
        private const float IosBehindLyricsBlackScrimAlpha = 0.4f;
        private const float LightAppearanceBlackScrimAlpha = 0.25f;
        private const float PortraitTextureScale = 1f;
        private const float LandscapeTextureScale = 0.8f;
        private readonly Stopwatch _animationClock = new();
        private readonly CanvasDevice _canvasDevice;
        private readonly float _renderScale;
        private readonly float _blurScale;
        private readonly float _bassPulseScale;
        private volatile bool _lightTheme;
        private LikeApplePinchVertex[] _meshVertices;
        private ushort[] _meshIndices;
        private readonly LikeAppleSpectrumAnalysis _spectrumAnalysis;

        private bool _isVerticalLayout = true;

        private CanvasRenderTarget _outputTarget = null!;
        private bool _presentationVisible = true;
        private bool _isBehindLyrics;
        private bool _lyricsModeTransitioning;
        private float _lyricsModeMix;
        private float _lyricsModeMixFrom;
        private float _lyricsModeMixTo;
        private double _lyricsModeTransitionStartTime;
        private bool _disposed;
        private double _transitionStartTime;
        private bool _transitioning;

        private LikeAppleGpuArtwork? _currentArtwork;
        private LikeAppleGpuArtwork? _previousArtwork;

        private readonly LikeAppleRenderPipeline _pipeline;

        private LikeAppleRenderSurface _rotationSurface = null!;
        private LikeAppleRenderSurface _horizontalBlurSurface = null!;
        private LikeAppleRenderSurface _verticalBlurSurface = null!;
        private LikeAppleRenderSurface _ordinaryBlurSurface = null!;
        private LikeAppleRenderSurface _outputSurface = null!;

        internal LikeAppleBackgroundRenderer(
            CanvasDevice canvasDevice,
            FFTProcessor fftProcessor,
            LikeAppleShaderBytecode shaderBytecode,
            bool lightTheme = false,
            float renderScale = 1f,
            float blurScale = 1f,
            float bassPulseScale = 1f)
        {
            ArgumentNullException.ThrowIfNull(canvasDevice);
            ArgumentNullException.ThrowIfNull(fftProcessor);
            ArgumentNullException.ThrowIfNull(shaderBytecode);
            _canvasDevice = canvasDevice;
            _spectrumAnalysis = new LikeAppleSpectrumAnalysis(fftProcessor);
            PresetIndex = LikeAppleMesh.SelectPreset();
            LandscapePresetIndex = LikeAppleMesh.SelectLandscapePreset();
            (_meshVertices, _meshIndices) = LikeAppleMesh.Create(
                PresetIndex,
                _isVerticalLayout);

            _renderScale = float.IsFinite(renderScale) && renderScale > 0
                ? Math.Clamp(renderScale, 0.125f, 1f)
                : 1f;
            _blurScale = GetSettingScale(blurScale);
            _bassPulseScale = GetSettingScale(bassPulseScale);
            _lightTheme = lightTheme;
            _pipeline = new LikeAppleRenderPipeline(
                canvasDevice,
                shaderBytecode,
                _meshVertices,
                _meshIndices);
            _animationClock.Start();
        }

        public int PresetIndex { get; }

        public int LandscapePresetIndex { get; }


        /// <summary>
        /// Sets a Win2D bitmap as the shader input. Ownership is transferred to this renderer.
        /// The bitmap must have been created from the same CanvasDevice.
        /// </summary>
        public void SetArtwork(CanvasBitmap artwork)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(artwork);
            if (artwork.Device != _canvasDevice)
            {
                throw new ArgumentException(
                    "Artwork must use this renderer's CanvasDevice.",
                    nameof(artwork));
            }

            using (_canvasDevice.Lock())
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                LikeAppleGpuArtwork uploaded = _pipeline.CreateArtwork(artwork);
                _previousArtwork?.Dispose();
                _previousArtwork = _currentArtwork;
                _currentArtwork = uploaded;
                _transitioning = _previousArtwork != null;
                _transitionStartTime = _animationClock.Elapsed.TotalSeconds;
            }
        }

        public void SetVerticalLayout(bool isVertical, bool animate = true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using (_canvasDevice.Lock())
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                SetVerticalLayoutCore(isVertical);
            }
        }

        private void SetVerticalLayoutCore(bool isVertical)
        {
            bool orientationChanged = _isVerticalLayout != isVertical;
            if (orientationChanged)
            {
                _isVerticalLayout = isVertical;
                (_meshVertices, _meshIndices) = LikeAppleMesh.Create(
                    _isVerticalLayout ? PresetIndex : LandscapePresetIndex,
                    _isVerticalLayout);
            }

            if (orientationChanged)
            {
                _pipeline.UpdatePinchMesh(_meshVertices, _meshIndices);
            }
        }

        public void SetPresentationVisible(bool visible)
        {
            if (_presentationVisible == visible)
            {
                return;
            }

            _presentationVisible = visible;
            if (visible)

            {
                _animationClock.Start();
            }
            else
            {
                _animationClock.Stop();
            }
        }

        public void SetLightTheme(bool lightTheme)
        {
            _lightTheme = lightTheme;
        }

        /// <summary>
        /// Mirrors MediaCoreUI.Backdrop.CompositeRenderer.isBehindLyrics.
        /// Music only enables the lyrics treatment for its timeSynced mode;
        /// loading and unavailable lyrics both use the regular backdrop.
        /// </summary>
        public void SetIsBehindLyrics(bool isBehindLyrics, bool animate = true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using (_canvasDevice.Lock())
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_isBehindLyrics == isBehindLyrics)
                {
                    return;
                }

                double time = _animationClock.Elapsed.TotalSeconds;
                float currentMix = GetLyricsModeMix(time);
                float targetMix = isBehindLyrics ? 1f : 0f;
                _isBehindLyrics = isBehindLyrics;

                if (!animate || !_animationClock.IsRunning)
                {
                    _lyricsModeMix = targetMix;
                    _lyricsModeMixFrom = targetMix;
                    _lyricsModeMixTo = targetMix;
                    _lyricsModeTransitioning = false;
                }
                else
                {
                    _lyricsModeMix = currentMix;
                    _lyricsModeMixFrom = currentMix;
                    _lyricsModeMixTo = targetMix;
                    _lyricsModeTransitionStartTime = time;
                    _lyricsModeTransitioning = Math.Abs(targetMix - currentMix) > 0.0001f;
                }
            }
        }


        public CanvasRenderTarget? Render(
            int pixelWidth,
            int pixelHeight,
            bool isPlaying = true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);

            using (_canvasDevice.Lock())
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_currentArtwork is null || !_presentationVisible)
                {
                    return null;
                }

                SetVerticalLayoutCore(pixelHeight >= pixelWidth);
                EnsureSurfaceSize(pixelWidth, pixelHeight, 96f);
                try
                {
                    RenderFrame(isPlaying);
                }
                finally
                {
                    _pipeline.ClearState();
                }

                return _outputTarget;
            }
        }

        private void RenderFrame(bool isPlaying)
        {
            double time = _animationClock.Elapsed.TotalSeconds;
            float transitionMix = GetTransitionMix(time);
            float lyricsModeMix = GetLyricsModeMix(time);
            float viewAspectRatio = _outputSurface.Width / (float)_outputSurface.Height;
            Vector2 viewScale = viewAspectRatio >= 1f
                ? new Vector2(1f, viewAspectRatio)
                : new Vector2(1f / viewAspectRatio, 1f);
            float pinchTextureScale = _isVerticalLayout
                ? PortraitTextureScale
                : LandscapeTextureScale;
            float pinchTextureOffset = (1f - pinchTextureScale) * 0.5f;
            Vector4 lyricsImageScales = _spectrumAnalysis.GetImageScales(
                isPlaying,
                _bassPulseScale);
            var pinchTextureTransform = new Vector4(
                pinchTextureScale,
                pinchTextureScale,
                pinchTextureOffset,
                pinchTextureOffset);

            // CompositeRenderer uses sigma 80 for its ordinary backdrop and
            // sigma 42.5 while behind lyrics. Interpolate the actual kernel
            // radius during a mode transition instead of crossfading two
            // fixed-radius blur results.
            float currentBlurSigma = Lerp(
                OrdinaryBlurSigma,
                LyricsBlurSigma,
                lyricsModeMix);
            var constants = new LikeAppleFrameConstants
            {
                Time = (float)time,
                TextureTransitionMix = transitionMix,
                ViewScale = viewScale,
                BlackScrimAlpha = _lightTheme
                    ? LightAppearanceBlackScrimAlpha
                    : IosBehindLyricsBlackScrimAlpha,
                OutputDitherStrength = 1f,
                BlurScale = GetBlurScale(currentBlurSigma, _blurScale),
                ImageScales = Vector4.One,
                PinchTextureTransform = pinchTextureTransform,
                LyricsModeMix = lyricsModeMix,
            };

            try
            {
                _pipeline.PrepareFrame();

                LikeAppleRenderSurface lyricsBackdrop;
                LikeAppleRenderSurface ordinaryBackdrop;
                bool needsOrdinaryBackdrop = lyricsModeMix < 0.9999f;
                bool needsLyricsBackdrop = lyricsModeMix > 0.0001f;

                if (needsOrdinaryBackdrop && needsLyricsBackdrop)
                {
                    // Both CompositeRenderer states keep the treated artwork.
                    // The ordinary state differs by omitting the spectrum
                    // scale and lyric pinch mesh, not by bypassing treatment.
                    constants.ImageScales = Vector4.One;
                    _pipeline.UpdateConstants(in constants);
                    _pipeline.RenderBackdrop(
                        _currentArtwork!,
                        _previousArtwork,
                        _rotationSurface,
                        _horizontalBlurSurface,
                        _ordinaryBlurSurface);
                    ordinaryBackdrop = _ordinaryBlurSurface;

                    constants.ImageScales = lyricsImageScales;
                    _pipeline.UpdateConstants(in constants);
                    _pipeline.RenderBackdrop(
                        _currentArtwork!,
                        _previousArtwork,
                        _rotationSurface,
                        _horizontalBlurSurface,
                        _verticalBlurSurface);
                    lyricsBackdrop = _verticalBlurSurface;
                }
                else
                {
                    constants.ImageScales = needsLyricsBackdrop
                        ? lyricsImageScales
                        : Vector4.One;
                    _pipeline.UpdateConstants(in constants);
                    _pipeline.RenderBackdrop(
                        _currentArtwork!,
                        _previousArtwork,
                        _rotationSurface,
                        _horizontalBlurSurface,
                        _verticalBlurSurface);
                    lyricsBackdrop = _verticalBlurSurface;
                    ordinaryBackdrop = lyricsBackdrop;
                }

                _pipeline.RenderComposite(
                    lyricsBackdrop,
                    ordinaryBackdrop,
                    _outputSurface,
                    _isVerticalLayout,
                    lyricsModeMix);

                _pipeline.CompleteFrame();
                _pipeline.ThrowIfDeviceRemoved();

            }
            finally
            {
                _pipeline.CompleteFrame();
            }
        }

        private static float GetSettingScale(float value)
        {
            return float.IsFinite(value)
                ? Math.Clamp(value, 0f, 10f)
                : 1f;
        }

        private Vector2 GetBlurScale(float sigma, float settingScale)
        {
            // The iOS sigma is measured in pixels of its quarter-resolution
            // backdrop. Convert it to output pixels, retain RenderScale's
            // physical-size compensation, then account for the exact rounded
            // dimensions of our backdrop surface on each axis. The HLSL
            // Gaussian coefficients themselves were generated for sigma 42.5.
            float targetOutputSigma =
                sigma * BlurSurfaceDownsample * settingScale * (float)_renderScale;
            return new Vector2(
                targetOutputSigma * _rotationSurface.Width /

                    (_outputSurface.Width * GaussianKernelSigma),
                targetOutputSigma * _rotationSurface.Height /
                    (_outputSurface.Height * GaussianKernelSigma));
        }

        private float GetLyricsModeMix(double time)
        {
            if (!_lyricsModeTransitioning)
            {
                return _lyricsModeMix;
            }

            float progress = (float)(
                (time - _lyricsModeTransitionStartTime) /
                LyricsModeTransitionSeconds);
            if (progress >= 1f)
            {
                _lyricsModeTransitioning = false;
                _lyricsModeMix = _lyricsModeMixTo;
                return _lyricsModeMix;
            }

            float easedProgress = EvaluateUIKitEaseInOut(Math.Clamp(progress, 0f, 1f));
            _lyricsModeMix = Lerp(
                _lyricsModeMixFrom,
                _lyricsModeMixTo,
                easedProgress);
            return _lyricsModeMix;
        }

        // UIView.animate(withDuration:animations:) uses the default
        // cubic-bezier(0.42, 0, 0.58, 1) timing curve.
        private static float EvaluateUIKitEaseInOut(float progress)
        {
            if (progress <= 0f || progress >= 1f)
            {
                return progress;
            }

            float lower = 0f;
            float upper = 1f;
            float parameter = progress;
            for (int iteration = 0; iteration < 12; iteration++)
            {
                parameter = (lower + upper) * 0.5f;
                float inverse = 1f - parameter;
                float x =
                    3f * inverse * inverse * parameter * 0.42f +
                    3f * inverse * parameter * parameter * 0.58f +
                    parameter * parameter * parameter;
                if (x < progress)
                {
                    lower = parameter;
                }
                else
                {
                    upper = parameter;
                }
            }

            return parameter * parameter * (3f - 2f * parameter);
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }

        private float GetTransitionMix(double time)
        {
            if (!_transitioning || _previousArtwork == null)
            {
                return 1f;
            }

            float progress = (float)((time - _transitionStartTime) / ArtworkTransitionSeconds);
            if (progress < 1f)
            {
                return Math.Clamp(progress, 0f, 1f);
            }

            _transitioning = false;
            _previousArtwork.Dispose();
            _previousArtwork = null;
            return 1f;
        }

        private void EnsureSurfaceSize(float width, float height, float dpi)
        {
            int pixelWidth = Math.Max(1, (int)Math.Round(
                width * dpi / 96f * _renderScale,
                MidpointRounding.AwayFromZero));
            int pixelHeight = Math.Max(1, (int)Math.Round(
                height * dpi / 96f * _renderScale,
                MidpointRounding.AwayFromZero));

            if (_outputSurface != null &&
                _outputSurface.Width == pixelWidth &&
                _outputSurface.Height == pixelHeight)
            {
                return;
            }

            CreateRenderSurfaces(pixelWidth, pixelHeight, dpi);
        }

        private void CreateRenderSurfaces(int width, int height, float dpi)
        {
            LikeAppleRenderSurface? newRotation = null;
            LikeAppleRenderSurface? newHorizontalBlur = null;
            LikeAppleRenderSurface? newVerticalBlur = null;
            LikeAppleRenderSurface? newOrdinaryBlur = null;
            LikeAppleRenderSurface? newOutput = null;
            CanvasRenderTarget? newOutputTarget = null;
            try
            {
                float backdropDownsample =
                    BlurSurfaceDownsample * Math.Max(1f, _blurScale);
                int backdropWidth = Math.Max(1, (int)Math.Floor(width / backdropDownsample));
                int backdropHeight = Math.Max(1, (int)Math.Floor(height / backdropDownsample));

                newRotation = _pipeline.CreateSurface(
                    backdropWidth,
                    backdropHeight);
                newHorizontalBlur = _pipeline.CreateSurface(
                    backdropWidth,
                    backdropHeight);
                newVerticalBlur = _pipeline.CreateSurface(
                    backdropWidth,
                    backdropHeight);
                newOrdinaryBlur = _pipeline.CreateSurface(
                    backdropWidth,
                    backdropHeight);

                float effectiveDpi = dpi * _renderScale;
                float widthInDips = width * 96f / effectiveDpi;
                float heightInDips = height * 96f / effectiveDpi;
                newOutputTarget = new CanvasRenderTarget(
                    _canvasDevice,
                    widthInDips,
                    heightInDips,
                    effectiveDpi,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    CanvasAlphaMode.Premultiplied);
                newOutput = _pipeline.CreateOutputSurface(
                    newOutputTarget,
                    width,
                    height);

                LikeAppleRenderSurface oldRotation = _rotationSurface;
                LikeAppleRenderSurface oldHorizontalBlur = _horizontalBlurSurface;
                LikeAppleRenderSurface oldVerticalBlur = _verticalBlurSurface;

                LikeAppleRenderSurface oldOrdinaryBlur = _ordinaryBlurSurface;
                LikeAppleRenderSurface oldOutput = _outputSurface;
                CanvasRenderTarget oldOutputTarget = _outputTarget;

                _rotationSurface = newRotation;
                _horizontalBlurSurface = newHorizontalBlur;
                _verticalBlurSurface = newVerticalBlur;
                _ordinaryBlurSurface = newOrdinaryBlur;
                _outputSurface = newOutput;
                _outputTarget = newOutputTarget;
                newRotation = null;
                newHorizontalBlur = null;
                newVerticalBlur = null;
                newOrdinaryBlur = null;
                newOutput = null;
                newOutputTarget = null;

                oldOutput?.Dispose();
                oldOutputTarget?.Dispose();
                oldOrdinaryBlur?.Dispose();
                oldVerticalBlur?.Dispose();
                oldHorizontalBlur?.Dispose();
                oldRotation?.Dispose();
            }
            finally
            {
                newOutput?.Dispose();
                newOutputTarget?.Dispose();
                newOrdinaryBlur?.Dispose();
                newVerticalBlur?.Dispose();
                newHorizontalBlur?.Dispose();
                newRotation?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            using (_canvasDevice.Lock())
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _animationClock.Stop();
                ReleaseDirectXResources();
            }
            _spectrumAnalysis.Stop();
        }


        private void ReleaseDirectXResources()
        {
            try
            {
                _pipeline.ClearAndFlush();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            _currentArtwork?.Dispose();
            _previousArtwork?.Dispose();
            _previousArtwork = null;
            _outputSurface?.Dispose();
            _outputTarget?.Dispose();
            _ordinaryBlurSurface?.Dispose();
            _verticalBlurSurface?.Dispose();
            _horizontalBlurSurface?.Dispose();
            _rotationSurface?.Dispose();
            _pipeline.Dispose();
        }

    }
}
