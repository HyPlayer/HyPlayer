using System;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.Platform.Storage;

namespace HyPlayer.Domain.Settings;

public partial class DownloadSettings : SettingsBase
{
    protected override string SectionName => "download";

    public bool WriteDownloadFileInfo
    {
        get => GetSettings(nameof(WriteDownloadFileInfo), true);
        set => SetSettings(nameof(WriteDownloadFileInfo), value);
    }

    public bool Write163Info
    {
        get => GetSettings(nameof(Write163Info), true);
        set => SetSettings(nameof(Write163Info), value);
    }

    public OccupySolution DownloadNameOccupySolution
    {
        get => GetSettings(nameof(DownloadNameOccupySolution), OccupySolution.Skip);
        set => SetSettings(nameof(DownloadNameOccupySolution), value);
    }

    public string? CustomDownloadDirectory =>
        TryGetSettings<string>(nameof(DownloadDirectory), out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public string DownloadDirectory
    {
        get => CustomDownloadDirectory is { } custom && FolderAccess.IsUsable(custom)
            ? custom
            : ApplicationData.Current.LocalCacheFolder.Path;
        set
        {
            FolderAccess.Forget(value);
            if (!SetSettings(nameof(DownloadDirectory), value))
                OnPropertyChanged();
        }
    }

    public async Task<string> GetDownloadDirectoryAsync()
    {
        if (CustomDownloadDirectory is { } custom && await FolderAccess.ProbeAsync(custom))
            return custom;

        try
        {
            return (await KnownFolders.MusicLibrary.CreateFolderAsync(
                nameof(HyPlayer), CreationCollisionOption.OpenIfExists)).Path;
        }
        catch
        {
            return ApplicationData.Current.LocalCacheFolder.Path;
        }
    }

    public string DownloadFileName
    {
        get => GetSettings(nameof(DownloadFileName), "{$SINGER} - {$SONGNAME}");
        set => SetSettings(nameof(DownloadFileName), value);
    }

    public string DownloadAudioRate
    {
        get => GetSettings(nameof(DownloadAudioRate), "hires");
        set => SetSettings(nameof(DownloadAudioRate), value);
    }

    public int MaxDownloadCount
    {
        get => GetSettings(nameof(MaxDownloadCount), 1);
        set => SetSettings(nameof(MaxDownloadCount), value);
    }
}
