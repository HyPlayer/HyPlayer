using System;
using Windows.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Playback.Services;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Domain.Settings;

/// <summary>
///     Settings related to audio playback, caching, and audio device configuration.
/// </summary>
public partial class PlaybackSettings : SettingsBase
{
    protected override string SectionName => "playback";

    /// <summary>
    ///     Playback volume (0-100).
    /// </summary>
    public int Volume
    {
        get
        {
            try
            {
                return GetSettings(nameof(Volume), 50);
            }
            catch
            {
                return 50;
            }
        }
        set => SetSettings(nameof(Volume), value);
    }

    /// <summary>
    ///     Audio quality rate (e.g. "exhigh", "hires").
    /// </summary>
    public string AudioRate
    {
        get => GetSettings(nameof(AudioRate), "exhigh");
        set => SetSettings(nameof(AudioRate), value);
    }

    /// <summary>Current track transition identifier (dir/gap/xfd).</summary>
    public string TransitionId
    {
        get => GetSettings(nameof(TransitionId), "dir");
        set
        {
            if (value is not ("dir" or "gap" or "xfd"))
                throw new ArgumentOutOfRangeException(nameof(value));

            if (SetSettings(nameof(TransitionId), value))
                OnPropertyChanged(nameof(IsCrossFadeTransition));
        }
    }

    public bool IsCrossFadeTransition => TransitionId == "xfd";

    /// <summary>
    ///     Duration of crossfade in seconds.
    /// </summary>
    public double CrossFadeTime
    {
        get => GetSettings(nameof(CrossFadeTime), 3d);
        set => SetSettings(nameof(CrossFadeTime), Math.Clamp(value, 3d, 10d));
    }

    /// <summary>
    ///     Whether per-track audio gain normalization is enabled.
    /// </summary>
    public bool EnableAudioGain
    {
        get => GetSettings(nameof(EnableAudioGain), false);
        set
        {
            if (!SetSettings(nameof(EnableAudioGain), value))
                return;

            Ioc.Default.GetService<IPlaybackControlService>()?.SetAudioGainEnabled(value);
        }
    }

    /// <summary>
    ///     Whether audio caching is enabled.
    /// </summary>
    public bool EnableCache
    {
        get => GetSettings(nameof(EnableCache), true);
        set => SetSettings(nameof(EnableCache), value);
    }

    /// <summary>
    ///     Cache directory path.
    /// </summary>
    public string CacheDirectory
    {
        get
        {
            try
            {
                return GetSettings(nameof(CacheDirectory), ApplicationData.Current.LocalCacheFolder.Path);
            }
            catch
            {
                return ApplicationData.Current.LocalCacheFolder.Path;
            }
        }
        set => SetSettings(nameof(CacheDirectory), value);
    }

    /// <summary>
    ///     Audio render device identifier.
    /// </summary>
    public string AudioRenderDevice
    {
        get => GetSettings("audio-render-device", "");
        set
        {
            if (SetSettings("audio-render-device", value, nameof(AudioRenderDevice)))
                _ = Ioc.Default.GetRequiredService<IPlaybackControlService>().InitializeAsync();
        }
    }

    /// <summary>
    ///     Whether FFT audio information is produced for consumers.
    /// </summary>
    public bool EnableFFT
    {
        get => GetSettings(nameof(EnableFFT), false);
        set
        {
            if (SetSettings(nameof(EnableFFT), value))
            {
                UpdateFftProcessing();
            }
        }
    }

    /// <summary>
    ///     Whether the spectrum visualization is shown in the expanded player.
    /// </summary>
    public bool ShowSpectrum
    {
        // Preserve the old combined setting as the initial display preference.
        get => GetSettings(nameof(ShowSpectrum), GetSettings(nameof(EnableFFT), false));
        set
        {
            if (SetSettings(nameof(ShowSpectrum), value))
                UpdateFftProcessing();
        }
    }

    private void UpdateFftProcessing()
    {
        var player = Ioc.Default.GetService<AudioGraphPlayer>();
        if (player is not null)
            player.EnableFFTProcessing = EnableFFT || ShowSpectrum;
    }

    /// <summary>
    ///     Current playback strategy identifier (seq/sgl/shn/pfm/ltg).
    /// </summary>
    public string ActiveStrategyId
    {
        get => GetSettings(nameof(ActiveStrategyId), "seq");
        set => SetSettings(nameof(ActiveStrategyId), value);
    }

    // TODO(settings-applier): PlaybackSettings still applies several playback side effects directly
    // (EnableAudioGain, AudioRenderDevice, EnableFFT/ShowSpectrum).
    // Keep the current behavior for compatibility; migrate these setters behind a dedicated
    // PlaybackSettingsApplier in a separate high-risk pass so import/reset settings can be made side-effect safe.
}
