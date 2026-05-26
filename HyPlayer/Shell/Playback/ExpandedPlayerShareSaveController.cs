#region

using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.UI.Dialogs;
using System;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Buffer = Windows.Storage.Streams.Buffer;

#endregion

namespace HyPlayer.Shell.Playback;

/// <summary>
/// Extracted controller for share/save clipboard and file-save operations
/// originally housed in ExpandedPlayer code-behind.
/// Delegates are used for UI-framework concerns (e.g., obtaining the current
/// song-title string from a named XAML element).
/// </summary>
public sealed class ExpandedPlayerShareSaveController
{
    private readonly PlaybackStateService _state;
    private readonly HttpClient _httpClient;
    private readonly IPlaylistService _playlist;
    private readonly INotificationService _notification;
    private readonly Func<string> _getSongTitle;

    public ExpandedPlayerShareSaveController(
        PlaybackStateService state,
        HttpClient httpClient,
        IPlaylistService playlist,
        INotificationService notification,
        Func<string> getSongTitle)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
        _notification = notification ?? throw new ArgumentNullException(nameof(notification));
        _getSongTitle = getSongTitle ?? throw new ArgumentNullException(nameof(getSongTitle));
    }

    /// <summary>Copy the current song name to the system clipboard.</summary>
    public void CopySongName()
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(_getSongTitle());
        Clipboard.SetContent(dataPackage);
    }

    /// <summary>Save the current album cover image to a user-chosen file.</summary>
    public async Task SaveAlbumImageAsync()
    {
        try
        {
            using var coverStream = _state.CoverStream.CloneStream();
            var filepicker = new FileSavePicker
            {
                SuggestedFileName = (_playlist.NowPlayingProviderItem?.Name ?? _state.NowPlayingSnapshot?.Name ?? "Cover") + "-Cover.jpg"
            };
            filepicker.FileTypeChoices.Add("图片文件", [".png", ".jpg"]);
            var file = await filepicker.PickSaveFileAsync();
            var buffer = new Buffer((uint)coverStream.Size);
            await coverStream.ReadAsync(buffer, (uint)coverStream.Size, InputStreamOptions.None);
            await FileIO.WriteBufferAsync(file, buffer);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("专辑封面保存失败", ex.Message);
        }
    }

    /// <summary>Open the lyric-share dialog with the current lyric lines.</summary>
    public void ShowLyricShareDialog()
    {
        _ = new LyricShareDialog
        {
            Lyrics = _state.LyricInfo.Lyrics
        }.ShowAsync();
    }
}
