using HyPlayer.Platform.Serialization;
using LiteFM.Abstractions;
using System.Text.Json;
using Windows.Storage;

namespace HyPlayer.Domain.Settings
{
    /// <summary>
    /// Settings related to Last.FM integration.
    /// </summary>
    public partial class LastFMSettings : SettingsBase
    {
        /// <summary>
        /// Last.FM session data.
        /// </summary>
        public LastFMSession LastFMSession
        {
            get
            {
                try
                {
                    return JsonSerializer.Deserialize<LastFMSession>(GetSettings(nameof(LastFMSession), "{}"), JsonDefaults.Options) ?? new LastFMSession();
                }
                catch
                {
                    return new LastFMSession();
                }
            }
            set
            {
                if (value == null)
                {
                    ApplicationData.Current.LocalSettings.Values[nameof(LastFMSession)] = null;
                }
                else
                {
                    ApplicationData.Current.LocalSettings.Values[nameof(LastFMSession)] = JsonSerializer.Serialize(value, JsonDefaults.Options);
                }
            }
        }

        /// <summary>
        /// Whether to update Last.FM now playing status.
        /// </summary>
        public bool UpdateLastFMNowPlaying
        {
            get => GetSettings(nameof(UpdateLastFMNowPlaying), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(UpdateLastFMNowPlaying)] = value;
            }
        }

        /// <summary>
        /// Whether Last.FM scrobbling is enabled.
        /// </summary>
        public bool LastFMScrobble
        {
            get => GetSettings(nameof(LastFMScrobble), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LastFMScrobble)] = value;
            }
        }

        /// <summary>
        /// Whether AI DJ mode is enabled.
        /// </summary>
        public bool useAiDj
        {
            get => GetSettings(nameof(useAiDj), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(useAiDj)] = value;
            }
        }
    }
}
