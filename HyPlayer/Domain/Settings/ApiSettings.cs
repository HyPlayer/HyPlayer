using Windows.Storage;

namespace HyPlayer.Domain.Settings;

/// <summary>
///     Settings related to API configuration, proxy, and network options.
/// </summary>
public partial class ApiSettings : SettingsBase
{
    protected override string SectionName => "api";

    public string? RealIp
    {
        get => GetSettings<string?>(nameof(RealIp), null);
        set => SetSettings(nameof(RealIp), value);
    }

    /// <summary>
    ///     Whether proxy is enabled.
    /// </summary>
    public bool EnableProxy
    {
        get => GetSettings(nameof(EnableProxy), false);
        set => SetSettings(nameof(EnableProxy), value);
    }

    /// <summary>
    ///     Additional API parameters.
    /// </summary>
    public string ApiAdditionalParametersJson
    {
        get => GetSettings("ApiAdditionalParameters", "{}");
        set => SetSettings("ApiAdditionalParameters", string.IsNullOrWhiteSpace(value) ? "{}" : value);
    }

    /// <summary>
    ///     Whether to use HTTP instead of HTTPS.
    /// </summary>
    public bool UseHttp
    {
        get => GetSettings(nameof(UseHttp), false);
        set => SetSettings(nameof(UseHttp), value);
    }

    /// <summary>
    ///     Whether to use HTTP when getting songs.
    /// </summary>
    public bool UseHttpWhenGettingSongs
    {
        get => GetSettings(nameof(UseHttpWhenGettingSongs), false);
        set => SetSettings(nameof(UseHttpWhenGettingSongs), value);
    }

    /// <summary>
    ///     Whether to enable fake check token API.
    /// </summary>
    public bool EnableCheckTokenApi
    {
        get => GetSettings(nameof(EnableCheckTokenApi), false);
        set => SetSettings(nameof(EnableCheckTokenApi), value);
    }

    /// <summary>
    ///     Whether API caching is enabled.
    /// </summary>
    public bool EnableApiCache
    {
        get => GetSettings(nameof(EnableApiCache), false);
        set => SetSettings(nameof(EnableApiCache), value);
    }

    /// <summary>
    ///     Whether to lazily get song URLs.
    /// </summary>
    public bool SongUrlLazyGet
    {
        get => GetSettings(nameof(SongUrlLazyGet), true);
        set => SetSettings(nameof(SongUrlLazyGet), value);
    }

    /// <summary>
    ///     Whether to greedily load play container items.
    /// </summary>
    public bool GreedilyLoadPlayContainerItems
    {
        get => GetSettings(nameof(GreedilyLoadPlayContainerItems), false);
        set => SetSettings(nameof(GreedilyLoadPlayContainerItems), value);
    }

    /// <summary>
    ///     Whether to auto-add greedily loaded songs to playlist.
    /// </summary>
    public bool AutoAddGreedilyLoadedSongsToPlayList
    {
        get => GetSettings(nameof(AutoAddGreedilyLoadedSongsToPlayList), false);
        set => SetSettings(nameof(AutoAddGreedilyLoadedSongsToPlayList), value);
    }

    /// <summary>
    ///     Whether to skip VIP songs during playback.
    /// </summary>
    public bool JumpVipSongPlaying
    {
        get => GetSettings(nameof(JumpVipSongPlaying), false);
        set => SetSettings(nameof(JumpVipSongPlaying), value);
    }

    /// <summary>
    ///     Whether to skip VIP songs during download.
    /// </summary>
    public bool JumpVipSongDownloading
    {
        get => GetSettings(nameof(JumpVipSongDownloading), false);
        set => SetSettings(nameof(JumpVipSongDownloading), value);
    }
}
