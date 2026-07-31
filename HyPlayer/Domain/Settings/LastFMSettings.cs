using System.Text.Json;
using Windows.Storage;
using HyPlayer.Platform.Serialization;
using LiteFM.Abstractions;

namespace HyPlayer.Domain.Settings;

/// <summary>
///     Settings related to Last.FM integration.
/// </summary>
public partial class LastFMSettings : SettingsBase
{
    protected override string SectionName => "lastfm";

    /// <summary>
    ///     Last.FM session data.
    /// </summary>
    public LastFMSession LastFMSession
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<LastFMSession>(GetSettings(nameof(LastFMSession), "{}"),
                    JsonDefaults.Options) ?? new LastFMSession();
            }
            catch
            {
                return new LastFMSession();
            }
        }
        set
        {
            var serialized = value == null ? null : JsonSerializer.Serialize(value, JsonDefaults.Options);
            SetSettings(nameof(LastFMSession), serialized, nameof(LastFMSession));
        }
    }

    /// <summary>
    ///     Whether to update Last.FM now playing status.
    /// </summary>
    public bool UpdateLastFMNowPlaying
    {
        get => GetSettings(nameof(UpdateLastFMNowPlaying), true);
        set => SetSettings(nameof(UpdateLastFMNowPlaying), value);
    }

    /// <summary>
    ///     Whether Last.FM scrobbling is enabled.
    /// </summary>
    public bool LastFMScrobble
    {
        get => GetSettings(nameof(LastFMScrobble), true);
        set => SetSettings(nameof(LastFMScrobble), value);
    }

    /// <summary>
    ///     Whether AI DJ mode is enabled.
    /// </summary>
    public bool UseAiDj
    {
        get => GetSettings(nameof(UseAiDj), false);
        set => SetSettings(nameof(UseAiDj), value);
    }
}
