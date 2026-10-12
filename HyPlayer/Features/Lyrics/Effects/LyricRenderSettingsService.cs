#nullable enable
using HyPlayer.Classes;
using HyPlayer.Domain.Settings;
using HyPlayer.LyricRenderer.Abstraction;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Lyrics.Effects;

public sealed class LyricRenderSettingsChangedEventArgs(LyricRenderSettings settings) : EventArgs
{
    public LyricRenderSettings Settings { get; } = settings;
}

/// <summary>Persists numeric lyric settings independently of legacy hylfx files.</summary>
public interface ILyricRenderSettingsService
{
    event EventHandler<LyricRenderSettingsChangedEventArgs>? SettingsChanged;
    LyricRenderSettings Current { get; }
    Task InitializeAsync();
    void Commit(LyricRenderSettings settings);
}

/// <summary>Loads source-generated JSON once; render frames never access local storage.</summary>
public sealed class LyricRenderSettingsService(LyricSettings storage) : ILyricRenderSettingsService
{
    private readonly LyricSettings _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private LyricRenderSettings _current = LyricRenderSettings.Default;

    public event EventHandler<LyricRenderSettingsChangedEventArgs>? SettingsChanged;
    public LyricRenderSettings Current => Volatile.Read(ref _current);

    public Task InitializeAsync()
    {
        var settings = LyricRenderSettings.Default;
        var json = _storage.DirectRenderSettingsJson;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                settings = JsonSerializer.Deserialize(json, JsonDefaultContext.Default.LyricRenderSettings) ?? settings;
            }
            catch (JsonException) { /* Invalid settings use the numeric defaults; legacy files are untouched. */ }
        }
        Publish(Normalize(settings));
        return Task.CompletedTask;
    }

    public void Commit(LyricRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = Normalize(settings);
        _storage.DirectRenderSettingsJson = JsonSerializer.Serialize(settings, JsonDefaultContext.Default.LyricRenderSettings);
        Publish(settings);
    }

    private static LyricRenderSettings Normalize(LyricRenderSettings settings) =>
        new((settings.Line ?? new()).Normalize(), (settings.Focused ?? new()).Normalize());

    private void Publish(LyricRenderSettings settings)
    {
        if (settings == Current) return;
        Volatile.Write(ref _current, settings);
        SettingsChanged?.Invoke(this, new(settings));
    }
}
