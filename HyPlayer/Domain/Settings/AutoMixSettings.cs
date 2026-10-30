using System;

namespace HyPlayer.Domain.Settings;

public partial class AutoMixSettings : SettingsBase
{
    public const string DefaultModelMirror = "https://hf-mirror.com";

    protected override string SectionName => "automix";

    public double MaxOverlap
    {
        get => GetSettings(nameof(MaxOverlap), 32d);
        set => SetSettings(nameof(MaxOverlap), Math.Clamp(value, 8d, 60d));
    }

    public bool TimeStretch
    {
        get => GetSettings(nameof(TimeStretch), true);
        set => SetSettings(nameof(TimeStretch), value);
    }

    public bool TempoGlide
    {
        get => GetSettings(nameof(TempoGlide), true);
        set => SetSettings(nameof(TempoGlide), value);
    }

    public bool KeepAlbumGapless
    {
        get => GetSettings(nameof(KeepAlbumGapless), true);
        set => SetSettings(nameof(KeepAlbumGapless), value);
    }

    public bool TailEffects
    {
        get => GetSettings(nameof(TailEffects), true);
        set => SetSettings(nameof(TailEffects), value);
    }

    public string ModelFolder
    {
        get => GetSettings(nameof(ModelFolder), string.Empty);
        set => SetSettings(nameof(ModelFolder), value ?? string.Empty);
    }

    public string ModelMirror
    {
        get => GetSettings(nameof(ModelMirror), DefaultModelMirror);
        set => SetSettings(nameof(ModelMirror), string.IsNullOrWhiteSpace(value) ? DefaultModelMirror : value.Trim());
    }

    public bool UseGpu
    {
        get => GetSettings(nameof(UseGpu), true);
        set => SetSettings(nameof(UseGpu), value);
    }

    public string GpuAdapter
    {
        get => GetSettings(nameof(GpuAdapter), string.Empty);
        set => SetSettings(nameof(GpuAdapter), value ?? string.Empty);
    }

    public bool ModelsSuspended
    {
        get => GetSettings(nameof(ModelsSuspended), false);
        set => SetSettings(nameof(ModelsSuspended), value);
    }

    public bool IsModelEnabled(string modelId)
    {
        return GetSettings($"ModelEnabled.{modelId}", true);
    }

    public void SetModelEnabled(string modelId, bool value)
    {
        SetSettings($"ModelEnabled.{modelId}", value, nameof(IsModelEnabled));
    }

    public long GetImportedModelSize(string modelId)
    {
        return GetSettings($"ModelImported.{modelId}", 0L);
    }

    public void SetImportedModelSize(string modelId, long value)
    {
        SetSettings($"ModelImported.{modelId}", value, nameof(GetImportedModelSize));
    }
}
