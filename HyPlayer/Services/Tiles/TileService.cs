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
using Buffer = Windows.Storage.Streams.Buffer;

namespace HyPlayer.Services.Tiles
{
    public class TileService : ITileService
    {
        private Setting _setting;
        private readonly TileUpdater _tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication();
        public async Task UpdateTile(HyPlayItem item, IRandomAccessStream coverStream)
        {
            if (!_setting.EnableTile) return;
            var infoContent = new TileContent()
            {
                Visual = new TileVisual()
                {
                    DisplayName = "HyPlayer 正在播放",
                    TileMedium = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = item.Name,
                                    HintStyle = AdaptiveTextStyle.Base,
                                    HintMaxLines = 2,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = item.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 1,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = item.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 1,
                                    HintWrap = true
                                }
                            }
                        }
                    },
                    TileWide = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = item.Name,
                                    HintStyle = AdaptiveTextStyle.Base,                     
                                    HintMaxLines = 2,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = item.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 2,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = item.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 1,
                                    HintWrap = true
                                }
                            }
                        }
                    },
                    TileLarge = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = item.Name,
                                    HintStyle = AdaptiveTextStyle.Base,
                                    HintMaxLines = 3,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = item.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 1,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = item.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 1,
                                    HintWrap = true
                                }
                            }
                        }
                    }
                }
            };
            var notificationInfo = new TileNotification(infoContent.GetXml()) { Tag = "Info" };
            _tileUpdater.Update(notificationInfo);
            if (_setting.EnableTileBackground)
            {
                var cover = await GetTileBackgroundAsync(item, coverStream);
                var coverContent = new TileContent()
                {
                    Visual = new TileVisual()
                    {
                        DisplayName = "HyPlayer 正在播放",
                        TileMedium = new TileBinding()
                        {
                            Branding = TileBranding.NameAndLogo,
                            Content = new TileBindingContentAdaptive()
                            {
                                BackgroundImage = cover
                            }
                        },
                        TileWide = new TileBinding()
                        {
                            Branding = TileBranding.NameAndLogo,
                            Content = new TileBindingContentAdaptive()
                            {
                                BackgroundImage = cover
                            }
                        },
                        TileLarge = new TileBinding()
                        {
                            Branding = TileBranding.NameAndLogo,
                            Content = new TileBindingContentAdaptive()
                            {
                                BackgroundImage = cover
                            }
                        }
                    }
                };
                var notificationCover = new TileNotification(coverContent.GetXml()) { Tag = "Cover" };
                _tileUpdater.Update(notificationCover);
            }
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
                using var encoderStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, encoderStream);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                encoderStream.Seek(0);
                var buffer = new Buffer((uint)encoderStream.Size);
                await encoderStream.ReadAsync(buffer, (uint)encoderStream.Size, InputStreamOptions.None);
                await FileIO.WriteBufferAsync(file, buffer);
            }
            var cover = new TileBackgroundImage()
            {
                Source = $"ms-appdata:///temp/TileImages/{item.Album.Id}"
            };
            return cover;
        }
        public async Task ClearAllTiles()
        {
            _tileUpdater.EnableNotificationQueue(false);
            _tileUpdater.Clear();
            var item = await ApplicationData.Current.TemporaryFolder.TryGetItemAsync("TileImages");
            await item?.DeleteAsync();
        }
        public TileService(Setting setting)
        {
            _setting = setting;
            _tileUpdater.EnableNotificationQueue(true);
        }
    }
}
