using System.Collections.Generic;
using Windows.Storage;

namespace HyPlayer.Domain.Settings;

public partial class LocalLibrarySettings(DownloadSettings downloadSettings) : SettingsBase
{
    protected override string SectionName => "library";

    public string SearchDirectory
    {
        get => GetSettings(nameof(SearchDirectory), downloadSettings.DownloadDirectory);
        set => SetSettings(nameof(SearchDirectory), value);
    }

    public List<string> ScanLocalFolders
    {
        get
        {
            var folders = GetSettings(nameof(ScanLocalFolders), KnownFolders.MusicLibrary.Path);
            return [.. folders.Split("\r\n")];
        }
        set => SetSettings(nameof(ScanLocalFolders), string.Join("\r\n", value));
    }

    public bool AdvancedMusicHistoryStorage
    {
        get => GetSettings(nameof(AdvancedMusicHistoryStorage), true);
        set => SetSettings(nameof(AdvancedMusicHistoryStorage), value);
    }

    public bool LocalProgressiveLoad
    {
        get => GetSettings(nameof(LocalProgressiveLoad), false);
        set => SetSettings(nameof(LocalProgressiveLoad), value);
    }
}
