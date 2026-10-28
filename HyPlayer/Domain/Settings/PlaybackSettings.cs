using System;
using Windows.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.Storage.Cache;
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
            if (value is not ("dir" or "gap" or "xfd" or "fde" or "mix"))
                throw new ArgumentOutOfRangeException(nameof(value));

            if (SetSettings(nameof(TransitionId), value))
            {
                OnPropertyChanged(nameof(IsCrossFadeTransition));
                OnPropertyChanged(nameof(TransitionUsesDuration));
                OnPropertyChanged(nameof(IsAutoMixTransition));
            }
        }
    }

    public bool IsCrossFadeTransition => TransitionId == "xfd";

    public bool TransitionUsesDuration => TransitionId is "xfd" or "fde" or "mix";

    public bool IsAutoMixTransition => TransitionId == "mix";

    public bool PlaybackFade
    {
        get => GetSettings(nameof(PlaybackFade), true);
        set => SetSettings(nameof(PlaybackFade), value);
    }

    public bool SpeedChangesPitch
    {
        get => GetSettings(nameof(SpeedChangesPitch), false);
        set => SetSettings(nameof(SpeedChangesPitch), value);
    }

    public bool AutoPlayOnLaunch
    {
        get => GetSettings(nameof(AutoPlayOnLaunch), false);
        set => SetSettings(nameof(AutoPlayOnLaunch), value);
    }

    public bool RememberPlaybackPosition
    {
        get => GetSettings(nameof(RememberPlaybackPosition), true);
        set => SetSettings(nameof(RememberPlaybackPosition), value);
    }

    public PlaybackFailureAction PlaybackFailureAction
    {
        get => GetSettings(nameof(PlaybackFailureAction), PlaybackFailureAction.SkipToNext);
        set => SetSettings(nameof(PlaybackFailureAction), value);
    }

    public bool DynamicCoverEnabled
    {
        get => GetSettings(nameof(DynamicCoverEnabled), false);
        set => SetSettings(nameof(DynamicCoverEnabled), value);
    }

    public DynamicCoverSource DynamicCoverSource
    {
        get => GetSettings(nameof(DynamicCoverSource), DynamicCoverSource.Auto);
        set => SetSettings(nameof(DynamicCoverSource), value);
    }

    /// <summary>
    ///     Duration of crossfade in seconds.
    /// </summary>
    public double CrossFadeTime
    {
        get => GetSettings(nameof(CrossFadeTime), 3d);
        set => SetSettings(nameof(CrossFadeTime), Math.Clamp(value, 2d, 10d));
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
                return CustomCacheDirectory is { } custom && FolderAccess.IsUsable(custom)
                    ? custom
                    : ApplicationData.Current.LocalCacheFolder.Path;
            }
            catch
            {
                return ApplicationData.Current.LocalCacheFolder.Path;
            }
        }
        set
        {
            FolderAccess.Forget(CustomCacheDirectory);
            FolderAccess.Forget(value);
            if (!SetSettings(nameof(CacheDirectory), value))
                OnPropertyChanged();
            OnPropertyChanged(nameof(CacheDirectoryIsCustom));
            SimpleCacher.Reset();
            HyPlayer.Features.Settings.Services.FolderAccessGuard.RefreshPath(value);
        }
    }

    public bool CacheDirectoryIsCustom =>
        CustomCacheDirectory is { } custom && FolderAccess.IsUsable(custom) &&
        !FolderAccess.IsSamePath(custom, ApplicationData.Current.LocalCacheFolder.Path);

    public void ResetCacheDirectory()
    {
        FolderAccess.Forget(CustomCacheDirectory);
        if (!SetSettings<string?>(nameof(CacheDirectory), null, nameof(CacheDirectory)))
            OnPropertyChanged(nameof(CacheDirectory));
        OnPropertyChanged(nameof(CacheDirectoryIsCustom));
        SimpleCacher.Reset();
    }

    internal void NotifyDirectoryAccessChanged()
    {
        OnPropertyChanged(nameof(CacheDirectory));
        OnPropertyChanged(nameof(CacheDirectoryIsCustom));
        SimpleCacher.Reset();
    }

    public string? CustomCacheDirectory =>
        TryGetSettings<string>(nameof(CacheDirectory), out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public static readonly double[] CacheLimitPresets = [1, 5, 10, 15, 20];

    public double CacheLimitGB
    {
        get => GetSettings(nameof(CacheLimitGB), 5d);
        set
        {
            if (double.IsNaN(value))
                return;

            if (SetSettings(nameof(CacheLimitGB), Math.Clamp(value, 0.5, 4096)))
                StorageMaintenance.ScheduleTrim(TimeSpan.FromSeconds(2));
        }
    }

    public bool CacheLimitCustom
    {
        get => GetSettings(nameof(CacheLimitCustom), false);
        set => SetSettings(nameof(CacheLimitCustom), value);
    }

    public int CacheLimitIndex
    {
        get
        {
            if (CacheLimitCustom)
                return CacheLimitPresets.Length;

            var index = Array.IndexOf(CacheLimitPresets, CacheLimitGB);
            return index < 0 ? CacheLimitPresets.Length : index;
        }
        set
        {
            if (value < 0 || value > CacheLimitPresets.Length)
                return;

            CacheLimitCustom = value == CacheLimitPresets.Length;
            if (value < CacheLimitPresets.Length)
                CacheLimitGB = CacheLimitPresets[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(CacheLimitIsCustom));
        }
    }

    public bool CacheLimitIsCustom => CacheLimitIndex == CacheLimitPresets.Length;

    /// <summary>
    ///     Audio render device identifier.
    /// </summary>
    public string AudioRenderDevice
    {
        get => GetSettings("audio-render-device", "");
        set
        {
            if (SetSettings("audio-render-device", value, nameof(AudioRenderDevice)))
                _ = Ioc.Default.GetRequiredService<IPlaybackControlService>().ApplyAudioDeviceAsync();
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

    public bool PromptWeakNetwork
    {
        get => GetSettings(nameof(PromptWeakNetwork), true);
        set => SetSettings(nameof(PromptWeakNetwork), value);
    }

    public bool PromptPowerSaving
    {
        get => GetSettings(nameof(PromptPowerSaving), true);
        set => SetSettings(nameof(PromptPowerSaving), value);
    }

    public string WeakNetworkBackup
    {
        get => GetSettings(nameof(WeakNetworkBackup), string.Empty);
        set => SetSettings(nameof(WeakNetworkBackup), value);
    }

    public string PowerSavingBackup
    {
        get => GetSettings(nameof(PowerSavingBackup), string.Empty);
        set => SetSettings(nameof(PowerSavingBackup), value);
    }

    // TODO(settings-applier): PlaybackSettings still applies several playback side effects directly
    // (EnableAudioGain, AudioRenderDevice, EnableFFT/ShowSpectrum).
    // Keep the current behavior for compatibility; migrate these setters behind a dedicated
    // PlaybackSettingsApplier in a separate high-risk pass so import/reset settings can be made side-effect safe.
}
