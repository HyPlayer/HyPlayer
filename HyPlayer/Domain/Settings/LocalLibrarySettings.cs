using System.Collections.Generic;
using Windows.Storage;
using HyPlayer.Platform.Storage;

namespace HyPlayer.Domain.Settings;

public partial class LocalLibrarySettings(DownloadSettings downloadSettings) : SettingsBase
{
    protected override string SectionName => "library";

    public string? CustomSearchDirectory =>
        TryGetSettings<string>(nameof(SearchDirectory), out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public string SearchDirectory
    {
        get => CustomSearchDirectory is { } custom && FolderAccess.IsUsable(custom)
            ? custom
            : downloadSettings.DownloadDirectory;
        set
        {
            FolderAccess.Forget(value);
            if (!SetSettings(nameof(SearchDirectory), value))
                OnPropertyChanged();
        }
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
