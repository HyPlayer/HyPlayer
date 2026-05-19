#region

using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
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
            var filepicker = new FileSavePicker
            {
                SuggestedFileName = _state.NowPlayingItem.Name + "-Cover.jpg"
            };
            filepicker.FileTypeChoices.Add("图片文件", [".png", ".jpg"]);
            var file = await filepicker.PickSaveFileAsync();
            if (file == null) return;

            // Remote / streaming item – download cover from URL
            if (_state.NowPlayingItem.ItemType is not (HyPlayItemType.Local or HyPlayItemType.LocalProgressive))
            {
                using var coverResult =
                    await _httpClient.GetAsync(new Uri(_state.NowPlayingItem.Album.Cover));
                if (coverResult.IsSuccessStatusCode)
                {
                    var cover = (await coverResult.Content.ReadAsByteArrayAsync()).AsBuffer();
                    await FileIO.WriteBufferAsync(file, cover);
                }
                else
                {
                    _notification.ShowMessage("专辑封面保存失败", "专辑封面下载失败");
                }
            }
            // Local file – extract thumbnail from the storage file
            else
            {
                using var thumbnail =
                    await _playlist.NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999);
                var buffer = new Buffer((uint)thumbnail.Size);
                await thumbnail.ReadAsync(buffer, (uint)thumbnail.Size, InputStreamOptions.None);
                await FileIO.WriteBufferAsync(file, buffer);
                buffer.Length = 0;
            }
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("专辑封面保存失败", ex.Message);
        }
    }

    /// <summary>Open the lyric-share dialog with the current lyric lines.</summary>
    public void ShowLyricShareDialog()
    {
        _ = new HyPlayer.Controls.LyricShareDialog
        {
            Lyrics = _state.LyricInfo.Lyrics
        }.ShowAsync();
    }
}
