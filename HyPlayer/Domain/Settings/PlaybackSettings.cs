using System;
using Windows.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Playback.Services;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Domain.Settings;

/// <summary>
///     Settings related to audio playback, caching, and audio device configuration.
/// </summary>
public class PlaybackSettings : SettingsBase
{
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
        set => ApplicationData.Current.LocalSettings.Values[nameof(Volume)] = value;
    }

    /// <summary>
    ///     Audio quality rate (e.g. "exhigh", "hires").
    /// </summary>
    public string audioRate
    {
        get => GetSettings(nameof(audioRate), "exhigh");
        set => ApplicationData.Current.LocalSettings.Values[nameof(audioRate)] = value;
    }

    /// <summary>Current track transition identifier (dir/gap/xfd).</summary>
    public string TransitionId
    {
        get
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue(nameof(TransitionId), out var stored)
                && stored is string id
                && id is "dir" or "gap" or "xfd")
            {
                values.Remove("CrossFade");
                return id;
            }

            var migrated = values.TryGetValue("CrossFade", out var legacy)
                           && legacy is true
                ? "xfd"
                : "dir";
            values[nameof(TransitionId)] = migrated;
            values.Remove("CrossFade");
            return migrated;
        }
        set
        {
            if (value is not ("dir" or "gap" or "xfd"))
                throw new ArgumentOutOfRangeException(nameof(value));

            var values = ApplicationData.Current.LocalSettings.Values;
            values[nameof(TransitionId)] = value;
            values.Remove("CrossFade");
        }
    }

    /// <summary>
    ///     Duration of crossfade in seconds.
    /// </summary>
    public double CrossFadeTime
    {
        get => GetSettings(nameof(CrossFadeTime), 3d);
        set => ApplicationData.Current.LocalSettings.Values[nameof(CrossFadeTime)] = Math.Clamp(value, 3d, 10d);
    }

    /// <summary>
    ///     Whether per-track audio gain normalization is enabled.
    /// </summary>
    public bool EnableAudioGain
    {
        get => GetSettings(nameof(EnableAudioGain), false);
        set
        {
            ApplicationData.Current.LocalSettings.Values[nameof(EnableAudioGain)] = value;
            var player = Ioc.Default.GetService<AudioGraphPlayer>();
            var state = Ioc.Default.GetService<PlaybackStateService>();
            if (player?.PrimaryPlaybackSource != null)
            {
                if (value)
                    player.SetPlaybackSourceOutputVolume(1, player.PrimaryPlaybackSource);
                else player.SetPlaybackSourceOutputVolume(1, player.PrimaryPlaybackSource);
            }
        }
    }

    /// <summary>
    ///     Whether audio caching is enabled.
    /// </summary>
    public bool enableCache
    {
        get => GetSettings(nameof(enableCache), true);
        set => ApplicationData.Current.LocalSettings.Values[nameof(enableCache)] = value;
    }

    /// <summary>
    ///     Cache directory path.
    /// </summary>
    public string cacheDir
    {
        get
        {
            try
            {
                return GetSettings(nameof(cacheDir), ApplicationData.Current.LocalCacheFolder.Path);
            }
            catch
            {
                return ApplicationData.Current.LocalCacheFolder.Path;
            }
        }
        set => ApplicationData.Current.LocalSettings.Values[nameof(cacheDir)] = value;
    }

    /// <summary>
    ///     Audio render device identifier.
    /// </summary>
    public string AudioRenderDevice
    {
        get => GetSettings("AudioRenderDeviceID", "");
        set
        {
            ApplicationData.Current.LocalSettings.Values["AudioRenderDeviceID"] = value;
            _ = Ioc.Default.GetRequiredService<IPlaybackControlService>().InitializeAsync();
        }
    }

    /// <summary>
    ///     Whether FFT audio processing is enabled.
    /// </summary>
    public bool EnableFFT
    {
        get => GetSettings(nameof(EnableFFT), false);
        set
        {
            ApplicationData.Current.LocalSettings.Values[nameof(EnableFFT)] = value;
            var player = Ioc.Default.GetService<AudioGraphPlayer>();
            player?.EnableFFTProcessing = value;
        }
    }

    /// <summary>
    ///     Current playback strategy identifier (seq/sgl/shn/pfm/ltg).
    /// </summary>
    public string ActiveStrategyId
    {
        get => GetSettings(nameof(ActiveStrategyId), "seq");
        set => ApplicationData.Current.LocalSettings.Values[nameof(ActiveStrategyId)] = value;
    }

    // TODO(settings-applier): PlaybackSettings still applies several playback side effects directly
    // (EnableAudioGain, AudioRenderDevice, EnableFFT).
    // Keep the current behavior for compatibility; migrate these setters behind a dedicated
    // PlaybackSettingsApplier in a separate high-risk pass so import/reset settings can be made side-effect safe.
}