using HyPlayer.UWP.Chopin.Utils;
using Microsoft.Graphics.Canvas;
using SharpGen.Runtime;
using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using Windows.Graphics.DirectX;
using Format = Vortice.DXGI.Format;

namespace HyPlayer.UI.Effects.LikeApple
{
    /// <summary>
    /// Direct3D recreation of Apple Music's TSLBackdrop lyric material.
    /// It follows the original four-stage Metal pipeline: three rotating
    /// artwork instances, the quarter-resolution iOS Gaussian blur, then
    /// the animated subdivided pinch mesh and its final saturation/scrim transfer.
    /// </summary>
    public sealed unsafe partial class LikeAppleBackgroundRenderer : IDisposable
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
        private const string ShaderDirectoryRelativePath = "Shaders/LikeApple";

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

        private GpuArtwork? _currentArtwork;
        private GpuArtwork? _previousArtwork;

        private ID3D11Device _device = null!;
        private ID3D11DeviceContext _context = null!;

        private RenderSurface _rotationSurface = null!;
        private RenderSurface _horizontalBlurSurface = null!;
        private RenderSurface _verticalBlurSurface = null!;
        private RenderSurface _ordinaryBlurSurface = null!;
        private RenderSurface _outputSurface = null!;

        private ID3D11VertexShader _rotationVertexShader = null!;
        private ID3D11VertexShader _artworkFillVertexShader = null!;
        private ID3D11VertexShader _fullscreenVertexShader = null!;
        private ID3D11VertexShader _pinchVertexShader = null!;
        private ID3D11PixelShader _rotationPixelShader = null!;
        private ID3D11PixelShader _horizontalBlurPixelShader = null!;
        private ID3D11PixelShader _verticalBlurPixelShader = null!;
        private ID3D11PixelShader _ordinaryMaterialPixelShader = null!;
        private ID3D11PixelShader _materialTreatedPixelShader = null!;
        private ID3D11PixelShader _materialCompositePixelShader = null!;
        private ID3D11PixelShader _pinchPixelShader = null!;
        private ID3D11PixelShader _pinchCompositePixelShader = null!;
        private ID3D11InputLayout _quadInputLayout = null!;
        private ID3D11InputLayout _pinchInputLayout = null!;
        private ID3D11Buffer _quadVertexBuffer = null!;
        private ID3D11Buffer _quadIndexBuffer = null!;
        private ID3D11Buffer _pinchVertexBuffer = null!;
        private ID3D11Buffer _pinchIndexBuffer = null!;
        private ID3D11Buffer _frameConstantBuffer = null!;
        private ID3D11Query _frameCompletionQuery = null!;
        private ID3D11SamplerState _linearClampSampler = null!;
        private ID3D11RasterizerState _rasterizerState = null!;

        public LikeAppleBackgroundRenderer(

            CanvasDevice canvasDevice,
            FFTProcessor fftProcessor,
            bool lightTheme = false,
            float renderScale = 1f,
            float blurScale = 1f,
            float bassPulseScale = 1f)
        {
            ArgumentNullException.ThrowIfNull(canvasDevice);
            ArgumentNullException.ThrowIfNull(fftProcessor);
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
            InitializeDeviceResources();
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
                GpuArtwork uploaded = CreateGpuArtwork(artwork);
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
                RecreatePinchMeshBuffers();
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
                    _context.ClearState();
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
            var constants = new FrameConstants
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
                BindConstantBuffer();
                BindSampler();
                _context.RSSetState(_rasterizerState);

                ID3D11ShaderResourceView lyricsBackdrop;
                ID3D11ShaderResourceView ordinaryBackdrop;
                bool needsOrdinaryBackdrop = lyricsModeMix < 0.9999f;
                bool needsLyricsBackdrop = lyricsModeMix > 0.0001f;

                if (needsOrdinaryBackdrop && needsLyricsBackdrop)
                {
                    // Both CompositeRenderer states keep the treated artwork.
                    // The ordinary state differs by omitting the spectrum
                    // scale and lyric pinch mesh, not by bypassing treatment.
                    constants.ImageScales = Vector4.One;
                    _context.UpdateSubresource(in constants, _frameConstantBuffer);
                    RenderBackdropPass(_ordinaryBlurSurface);
                    ordinaryBackdrop = _ordinaryBlurSurface.ShaderResourceView;

                    constants.ImageScales = lyricsImageScales;
                    _context.UpdateSubresource(in constants, _frameConstantBuffer);
                    RenderBackdropPass(_verticalBlurSurface);
                    lyricsBackdrop = _verticalBlurSurface.ShaderResourceView;
                }
                else
                {
                    constants.ImageScales = needsLyricsBackdrop
                        ? lyricsImageScales
                        : Vector4.One;
                    _context.UpdateSubresource(in constants, _frameConstantBuffer);
                    RenderBackdropPass(_verticalBlurSurface);
                    lyricsBackdrop = _verticalBlurSurface.ShaderResourceView;
                    ordinaryBackdrop = lyricsBackdrop;
                }

                RenderCompositePass(
                    lyricsBackdrop,
                    ordinaryBackdrop,
                    lyricsModeMix);

                UnbindPixelShaderResources(2);
                SetRenderTarget(null);
                WaitForFrameCompletion();
                Result removedReason = _device.DeviceRemovedReason;
                if (removedReason.Failure)
                {
                    removedReason.CheckError();
                }

            }
            finally
            {
                UnbindPixelShaderResources(2);
                SetRenderTarget(null);
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

        private void RenderBackdropPass(RenderSurface verticalBlurTarget)
        {
            RenderRotationPass();
            RenderBlurPass(
                _rotationSurface.ShaderResourceView,
                _horizontalBlurSurface,
                _horizontalBlurPixelShader);
            RenderBlurPass(
                _horizontalBlurSurface.ShaderResourceView,
                verticalBlurTarget,
                _verticalBlurPixelShader);
        }


        private void RenderRotationPass()
        {
            GpuArtwork currentArtwork = _currentArtwork ??
                throw new InvalidOperationException("Artwork is required to render a frame.");

            SetViewport(_rotationSurface.Width, _rotationSurface.Height);
            SetRenderTarget(_rotationSurface.RenderTargetView);
            _context.ClearRenderTargetView(
                _rotationSurface.RenderTargetView,
                new Color4(0f, 0f, 0f, 1f));
            _context.IASetInputLayout(_quadInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_quadVertexBuffer, sizeof(QuadVertex));
            _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_rotationVertexShader, null, 0);
            _context.PSSetShader(_rotationPixelShader, null, 0);
            BindPixelShaderResources(
                currentArtwork.ShaderResourceView,
                _previousArtwork?.ShaderResourceView ?? currentArtwork.ShaderResourceView);

            // Keep an aspect-fill copy underneath the moving layers. It only
            // supplies pixels exposed by a rotated square, so full-frame iOS
            // coverage does not enlarge the moving color regions.
            _context.VSSetShader(_artworkFillVertexShader, null, 0);
            _context.DrawIndexed(6, 0, 0);

            _context.VSSetShader(_rotationVertexShader, null, 0);
            _context.DrawIndexedInstanced(6, 3, 0, 0, 0);
            UnbindPixelShaderResources(2);
        }

        private void RenderBlurPass(
            ID3D11ShaderResourceView source,
            RenderSurface target,
            ID3D11PixelShader shader)
        {
            SetViewport(target.Width, target.Height);
            SetRenderTarget(target.RenderTargetView);
            _context.IASetInputLayout(_quadInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_quadVertexBuffer, sizeof(QuadVertex));
            _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_fullscreenVertexShader, null, 0);
            _context.PSSetShader(shader, null, 0);
            BindPixelShaderResources(source);
            _context.DrawIndexed(6, 0, 0);
            UnbindPixelShaderResources(1);
        }

        private void RenderCompositePass(
            ID3D11ShaderResourceView lyricsBackdrop,
            ID3D11ShaderResourceView ordinaryBackdrop,
            float lyricsModeMix)
        {
            SetViewport(_outputSurface.Width, _outputSurface.Height);
            SetRenderTarget(_outputSurface.RenderTargetView);
            _context.ClearRenderTargetView(
                _outputSurface.RenderTargetView,
                new Color4(0f, 0f, 0f, 1f));

            if (lyricsModeMix <= 0f)
            {
                // isBehindLyrics=false keeps the treated blurred artwork but
                // does not submit the lyric pinch mesh.
                BindPixelShaderResources(ordinaryBackdrop);
                DrawFullscreenMaterial(_ordinaryMaterialPixelShader);
                return;
            }

            if (lyricsModeMix >= 1f)
            {
                BindPixelShaderResources(lyricsBackdrop);
                if (_isVerticalLayout)
                {
                    // Portrait control maps can pull their outer contour away
                    // from a corner, so retain the treated fallback used by
                    // the established lyric path.
                    DrawFullscreenMaterial(_materialTreatedPixelShader);
                }
                DrawPinchMesh(_pinchPixelShader);
                return;
            }

            BindPixelShaderResources(lyricsBackdrop, ordinaryBackdrop);
            // During the iOS mode animation this fullscreen layer supplies
            // both the ordinary backdrop and the treated lyric fallback.
            // The mesh then replaces only its covered pixels with the same
            // crossfade using warped lyric coordinates.
            DrawFullscreenMaterial(_materialCompositePixelShader);
            DrawPinchMesh(_pinchCompositePixelShader);
        }

        private void DrawFullscreenMaterial(ID3D11PixelShader pixelShader)
        {
            _context.IASetInputLayout(_quadInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_quadVertexBuffer, sizeof(QuadVertex));
            _context.IASetIndexBuffer(_quadIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_fullscreenVertexShader, null, 0);
            _context.PSSetShader(pixelShader, null, 0);
            _context.DrawIndexed(6, 0, 0);
        }


        private void DrawPinchMesh(ID3D11PixelShader pixelShader)
        {
            _context.IASetInputLayout(_pinchInputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            BindVertexBuffer(_pinchVertexBuffer, sizeof(LikeApplePinchVertex));
            _context.IASetIndexBuffer(_pinchIndexBuffer, Format.R16_UInt, 0);
            _context.VSSetShader(_pinchVertexShader, null, 0);
            _context.PSSetShader(pixelShader, null, 0);
            _context.DrawIndexed((uint)_meshIndices.Length, 0, 0);
        }

        private void InitializeDeviceResources()
        {
            _device = Win2DDirect3DBridge.GetDirect3DDevice(_canvasDevice);
            _context = _device.ImmediateContext;

            CreatePipelineResources();
        }

        private void CreatePipelineResources()
        {
            byte[] rotationVertex = ReadShaderBytecode("RotationVertex");
            byte[] artworkFillVertex = ReadShaderBytecode("ArtworkFillVertex");
            byte[] fullscreenVertex = ReadShaderBytecode("FullscreenVertex");
            byte[] pinchVertex = ReadShaderBytecode("PinchVertex");
            byte[] rotationPixel = ReadShaderBytecode("RotationPixel");
            byte[] horizontalBlurPixel = ReadShaderBytecode("BlurHorizontalPixel");
            byte[] verticalBlurPixel = ReadShaderBytecode("BlurVerticalPixel");
            byte[] ordinaryMaterialPixel = ReadShaderBytecode("OrdinaryMaterialPixel");
            byte[] materialTreatedPixel = ReadShaderBytecode("MaterialTreatedPixel");
            byte[] materialCompositePixel = ReadShaderBytecode("MaterialCompositePixel");
            byte[] pinchPixel = ReadShaderBytecode("PinchPixel");
            byte[] pinchCompositePixel = ReadShaderBytecode("PinchCompositePixel");

            _rotationVertexShader = CreateVertexShader(rotationVertex);
            _artworkFillVertexShader = CreateVertexShader(artworkFillVertex);
            _fullscreenVertexShader = CreateVertexShader(fullscreenVertex);
            _pinchVertexShader = CreateVertexShader(pinchVertex);
            _rotationPixelShader = CreatePixelShader(rotationPixel);
            _horizontalBlurPixelShader = CreatePixelShader(horizontalBlurPixel);
            _verticalBlurPixelShader = CreatePixelShader(verticalBlurPixel);
            _ordinaryMaterialPixelShader = CreatePixelShader(ordinaryMaterialPixel);
            _materialTreatedPixelShader = CreatePixelShader(materialTreatedPixel);
            _materialCompositePixelShader = CreatePixelShader(materialCompositePixel);
            _pinchPixelShader = CreatePixelShader(pinchPixel);
            _pinchCompositePixelShader = CreatePixelShader(pinchCompositePixel);

            _quadInputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
            ],
                rotationVertex);
            _pinchInputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("FROMPOS", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TOPOS", 0, Format.R32G32_Float, 8, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
            ],
                pinchVertex);

            QuadVertex[] quadVertices =
            [
                new(new Vector4(-1f, -1f, 0f, 1f), new Vector2(0f, 1f)),
                new(new Vector4(-1f, 1f, 0f, 1f), new Vector2(0f, 0f)),
                new(new Vector4(1f, 1f, 0f, 1f), new Vector2(1f, 0f)),
                new(new Vector4(1f, -1f, 0f, 1f), new Vector2(1f, 1f)),
            ];
            ushort[] quadIndices = [0, 1, 2, 2, 3, 0];
            _quadVertexBuffer = _device.CreateBuffer(quadVertices, BindFlags.VertexBuffer);
            _quadIndexBuffer = _device.CreateBuffer(quadIndices, BindFlags.IndexBuffer);
            _pinchVertexBuffer = _device.CreateBuffer(_meshVertices, BindFlags.VertexBuffer);
            _pinchIndexBuffer = _device.CreateBuffer(_meshIndices, BindFlags.IndexBuffer);
            _frameConstantBuffer = _device.CreateBuffer(
                (uint)sizeof(FrameConstants),
                BindFlags.ConstantBuffer,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.None,
                0);
            _frameCompletionQuery = _device.CreateQuery(
                new QueryDescription(
                    Vortice.Direct3D11.QueryType.Event,
                    Vortice.Direct3D11.QueryFlags.None));

            _linearClampSampler = _device.CreateSamplerState(new SamplerDescription(
                Filter.MinMagMipLinear,

                TextureAddressMode.Clamp,
                0f,
                1,
                ComparisonFunction.Never,
                0f,
                float.MaxValue));
            _rasterizerState = _device.CreateRasterizerState(
                new RasterizerDescription(
                    CullMode.None,
                    Vortice.Direct3D11.FillMode.Solid));
        }

        private void RecreatePinchMeshBuffers()
        {
            if (_device == null)
            {
                return;
            }

            ID3D11Buffer replacementVertices = _device.CreateBuffer(
                _meshVertices,
                BindFlags.VertexBuffer);
            ID3D11Buffer replacementIndices;
            try
            {
                replacementIndices = _device.CreateBuffer(
                    _meshIndices,
                    BindFlags.IndexBuffer);
            }
            catch
            {
                replacementVertices.Dispose();
                throw;
            }

            ID3D11Buffer previousVertices = _pinchVertexBuffer;
            ID3D11Buffer previousIndices = _pinchIndexBuffer;
            _pinchVertexBuffer = replacementVertices;
            _pinchIndexBuffer = replacementIndices;
            previousVertices?.Dispose();
            previousIndices?.Dispose();
        }

        private GpuArtwork CreateGpuArtwork(CanvasBitmap bitmap)
        {
            ID3D11Texture2D texture = Win2DDirect3DBridge.GetTexture2D(bitmap);
            ID3D11ShaderResourceView? view = null;
            try
            {
                view = _device.CreateShaderResourceView(texture);
                return new GpuArtwork(bitmap, texture, view);
            }
            catch
            {
                view?.Dispose();
                texture.Dispose();
                bitmap.Dispose();
                throw;
            }
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
            RenderSurface? newRotation = null;
            RenderSurface? newHorizontalBlur = null;
            RenderSurface? newVerticalBlur = null;
            RenderSurface? newOrdinaryBlur = null;
            RenderSurface? newOutput = null;
            CanvasRenderTarget? newOutputTarget = null;
            try
            {
                float backdropDownsample =
                    BlurSurfaceDownsample * Math.Max(1f, _blurScale);
                int backdropWidth = Math.Max(1, (int)Math.Floor(width / backdropDownsample));
                int backdropHeight = Math.Max(1, (int)Math.Floor(height / backdropDownsample));

                newRotation = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);
                newHorizontalBlur = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);
                newVerticalBlur = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);
                newOrdinaryBlur = CreateSurface(
                    backdropWidth,
                    backdropHeight,
                    Format.R16G16B16A16_Float,
                    true);

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
                ID3D11Texture2D? outputTexture =
                    Win2DDirect3DBridge.GetTexture2D(newOutputTarget);
                try
                {
                    ID3D11RenderTargetView outputView =
                        _device.CreateRenderTargetView(outputTexture);
                    newOutput = new RenderSurface(
                        outputTexture,
                        outputView,
                        null!,
                        width,
                        height);
                    outputTexture = null;
                }
                finally
                {
                    outputTexture?.Dispose();
                }

                RenderSurface oldRotation = _rotationSurface;
                RenderSurface oldHorizontalBlur = _horizontalBlurSurface;
                RenderSurface oldVerticalBlur = _verticalBlurSurface;

                RenderSurface oldOrdinaryBlur = _ordinaryBlurSurface;
                RenderSurface oldOutput = _outputSurface;
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

        private RenderSurface CreateSurface(
            int width,
            int height,
            Format format,
            bool createShaderResource)
        {
            var description = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget |
                    (createShaderResource ? BindFlags.ShaderResource : BindFlags.None),
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            };
            ID3D11Texture2D texture = _device.CreateTexture2D(description);
            ID3D11RenderTargetView? renderTarget = null;
            ID3D11ShaderResourceView? shaderResource = null;
            try
            {
                renderTarget = _device.CreateRenderTargetView(texture);
                if (createShaderResource)
                {
                    shaderResource = _device.CreateShaderResourceView(texture);
                }
                return new RenderSurface(
                    texture,
                    renderTarget,
                    shaderResource!,
                    width,
                    height);
            }
            catch
            {
                shaderResource?.Dispose();
                renderTarget?.Dispose();
                texture.Dispose();
                throw;
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
                _context?.ClearState();
                _context?.Flush();
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

            _rasterizerState?.Dispose();
            _linearClampSampler?.Dispose();
            _frameCompletionQuery?.Dispose();
            _frameConstantBuffer?.Dispose();
            _pinchIndexBuffer?.Dispose();
            _pinchVertexBuffer?.Dispose();
            _quadIndexBuffer?.Dispose();
            _quadVertexBuffer?.Dispose();
            _pinchInputLayout?.Dispose();
            _quadInputLayout?.Dispose();
            _pinchCompositePixelShader?.Dispose();
            _pinchPixelShader?.Dispose();
            _materialCompositePixelShader?.Dispose();
            _materialTreatedPixelShader?.Dispose();
            _ordinaryMaterialPixelShader?.Dispose();
            _verticalBlurPixelShader?.Dispose();
            _horizontalBlurPixelShader?.Dispose();
            _rotationPixelShader?.Dispose();
            _pinchVertexShader?.Dispose();
            _fullscreenVertexShader?.Dispose();
            _artworkFillVertexShader?.Dispose();
            _rotationVertexShader?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
        }

        private ID3D11VertexShader CreateVertexShader(byte[] bytecode)
        {
            return _device.CreateVertexShader(bytecode.AsSpan());
        }

        private ID3D11PixelShader CreatePixelShader(byte[] bytecode)
        {
            return _device.CreatePixelShader(bytecode.AsSpan());
        }

        private void SetRenderTarget(ID3D11RenderTargetView? renderTarget)
        {
            if (renderTarget == null)
            {
                _context.OMSetRenderTargets(
                    0,
                    Array.Empty<ID3D11RenderTargetView>(),
                    null);
                return;
            }
            _context.OMSetRenderTargets(1, [renderTarget], null);
        }

        private void WaitForFrameCompletion()
        {
            // Flush only submits the command list; it does not guarantee that
            // the CanvasRenderTarget is complete before Win2D samples it.
            // An event query prevents the draw pass from observing a partial frame.
            _context.End(_frameCompletionQuery);
            _context.Flush();
            while (true)
            {
                Result result = _context.GetData(
                    _frameCompletionQuery,
                    IntPtr.Zero,
                    0,
                    AsyncGetDataFlags.DoNotFlush);
                if (result == Result.Ok)
                {
                    return;
                }
                if (result.Failure)
                {
                    result.CheckError();
                }
                Thread.Yield();
            }
        }

        private void SetViewport(int width, int height)
        {

            var viewport = new Vortice.Mathematics.Viewport(width, height);
            _context.RSSetViewports([viewport]);
        }

        private void BindVertexBuffer(ID3D11Buffer buffer, int stride)
        {
            _context.IASetVertexBuffer(0, buffer, (uint)stride);
        }

        private void BindConstantBuffer()
        {
            _context.VSSetConstantBuffers(0, 1, [_frameConstantBuffer]);
            _context.PSSetConstantBuffers(0, 1, [_frameConstantBuffer]);
        }

        private void BindSampler()
        {
            _context.PSSetSamplers(0, 1, [_linearClampSampler]);
        }

        private void BindPixelShaderResources(params ID3D11ShaderResourceView[] resources)
        {
            for (uint index = 0; index < (uint)resources.Length; index++)
            {
                _context.PSSetShaderResource(index, resources[index]);
            }
        }

        private void UnbindPixelShaderResources(int count)
        {
            for (uint index = 0; index < (uint)count; index++)
            {
                _context.PSSetShaderResource(index, null!);
            }
        }

        private static byte[] ReadShaderBytecode(string shaderName)
        {
            string shaderDirectory = ShaderDirectoryRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar);
            string shaderPath = Path.Combine(
                AppContext.BaseDirectory,
                shaderDirectory,
                $"{shaderName}.bin");
            return File.ReadAllBytes(shaderPath);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FrameConstants
        {
            public float Time;
            public float TextureTransitionMix;
            public Vector2 ViewScale;
            public float BlackScrimAlpha;
            public float OutputDitherStrength;
            public Vector2 BlurScale;
            public Vector4 ImageScales;
            public Vector4 PinchTextureTransform;
            public float LyricsModeMix;
            public Vector3 Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct QuadVertex
        {
            public QuadVertex(Vector4 position, Vector2 textureCoordinate)
            {
                Position = position;
                TextureCoordinate = textureCoordinate;
            }

            public readonly Vector4 Position;
            public readonly Vector2 TextureCoordinate;
        }


        private sealed partial class GpuArtwork : IDisposable
        {
            public GpuArtwork(
                CanvasBitmap bitmap,
                ID3D11Texture2D texture,
                ID3D11ShaderResourceView shaderResourceView)
            {
                Bitmap = bitmap;
                Texture = texture;
                ShaderResourceView = shaderResourceView;
            }

            public CanvasBitmap Bitmap { get; }
            public ID3D11Texture2D Texture { get; }
            public ID3D11ShaderResourceView ShaderResourceView { get; }


            public void Dispose()
            {
                ShaderResourceView.Dispose();
                Texture.Dispose();
                Bitmap.Dispose();
            }
        }

        private sealed partial class RenderSurface : IDisposable
        {
            public RenderSurface(
                ID3D11Texture2D texture,
                ID3D11RenderTargetView renderTargetView,
                ID3D11ShaderResourceView shaderResourceView,
                int width,
                int height)
            {
                Texture = texture;
                RenderTargetView = renderTargetView;
                ShaderResourceView = shaderResourceView;
                Width = width;
                Height = height;
            }

            public ID3D11Texture2D Texture { get; }
            public ID3D11RenderTargetView RenderTargetView { get; }
            public ID3D11ShaderResourceView ShaderResourceView { get; }
            public int Width { get; }
            public int Height { get; }

            public void Dispose()
            {
                ShaderResourceView?.Dispose();
                RenderTargetView.Dispose();
                Texture.Dispose();
            }
        }
    }
}
