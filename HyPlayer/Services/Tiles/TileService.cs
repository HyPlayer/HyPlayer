using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using Microsoft.Toolkit.Uwp.Helpers;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;

namespace HyPlayer.Services.Tiles
{
    public class TileService : ITileService
    {
        private Setting _setting;
        private readonly TileUpdater _tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication();
        public async Task UpdateTile(HyPlayItem item, IRandomAccessStream coverStream)
        {
            if (!_setting.EnableTile) return;
            var cover = await GetTileBackgroundAsync(item, coverStream);
            var tileContent = new TileContent()
            {
                Visual = new TileVisual()
                {
                    DisplayName = "HyPlayer 正在播放",
                    TileSmall = new TileBinding()
                    {
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                        }
                    },
                    TileMedium = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = item.Name,
                                    HintStyle = AdaptiveTextStyle.Base
                                },
                                new AdaptiveText()
                                {
                                    Text = item.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 2
                                },
                                new AdaptiveText()
                                {
                                    Text = item.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 2
                                }
                            }
                        }
                    },
                    TileWide = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = item.Name,
                                    HintStyle = AdaptiveTextStyle.Base
                                },
                                new AdaptiveText()
                                {
                                    Text = item.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 3
                                },
                                new AdaptiveText()
                                {
                                    Text = item.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle
                                }
                            }
                        }
                    },
                    TileLarge = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = item.Name,
                                    HintStyle = AdaptiveTextStyle.Base
                                },
                                new AdaptiveText()
                                {
                                    Text = item.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 3
                                },
                                new AdaptiveText()
                                {
                                    Text = item.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle
                                }
                            }
                        }
                    }
                }
            };

            var notification = new TileNotification(tileContent.GetXml());

            _tileUpdater.Update(notification);
        }
        public async Task<TileBackgroundImage?> GetTileBackgroundAsync(HyPlayItem item, IRandomAccessStream stream)
        {
            if (item.ItemType != HyPlayItemType.Netease || !_setting.EnableTileBackground || stream == null) return null;
            using var coverStream = stream.CloneStream();
            StorageFolder storageFolder =
                await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("TileImages", CreationCollisionOption.OpenIfExists);
            var exists = await storageFolder.FileExistsAsync(item.Album.Id);
            if (!exists)
            {
                var file = await storageFolder.CreateFileAsync(item.Album.Id);
                var decoder = await BitmapDecoder.CreateAsync(coverStream);
                using var bitmap = await decoder.GetSoftwareBitmapAsync();
                using var encoderStream = await file.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, encoderStream);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
            }
            var cover = new TileBackgroundImage()
            {
                Source = $"ms-appdata:///temp/TileImages/{item.Album.Id}",
                HintOverlay = 50
            };
            return cover;
        }
        public async Task ClearAllTiles()
        {
            _tileUpdater.Clear();
            var item = await ApplicationData.Current.TemporaryFolder.TryGetItemAsync("TileImages");
            await item?.DeleteAsync();
        }
        public TileService(Setting setting)
        {
            _setting = setting;
        }
    }
}
