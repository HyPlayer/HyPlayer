using HyPlayer.Domain.Settings;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using Microsoft.Toolkit.Uwp.Helpers;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Buffer = Windows.Storage.Streams.Buffer;

namespace HyPlayer.Platform.Tiles
{
    public class TileService : ITileService
    {
        private Setting _setting;
        private readonly TileUpdater _tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication();

        public async Task UpdateTile(SingleSongBase item, IRandomAccessStream coverStream)
        {
            if (!_setting.EnableTile) return;

            var artistText = item.CreatorList is { Count: > 0 } creators
                ? string.Join(" / ", creators)
                : string.Empty;
            var albumName = item.Album?.Name ?? string.Empty;

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
                                    Text = artistText,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 1,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = albumName,
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
                                    Text = artistText,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 2,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = albumName,
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
                                    Text = artistText,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintMaxLines = 1,
                                    HintWrap = true
                                },
                                new AdaptiveText()
                                {
                                    Text = albumName,
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

        public async Task<TileBackgroundImage?> GetTileBackgroundAsync(SingleSongBase item, IRandomAccessStream stream)
        {
            var singleSongTypeId = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>().SingleSongTypeId;
            if (item.TypeId != singleSongTypeId || !_setting.EnableTileBackground || stream == null) return null;

            using var coverStream = stream.CloneStream();
            StorageFolder storageFolder =
                await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("TileImages", CreationCollisionOption.OpenIfExists);
            var albumId = item.Album?.ActualId ?? item.ActualId;
            var exists = await storageFolder.FileExistsAsync(albumId);
            if (!exists)
            {
                var file = await storageFolder.CreateFileAsync(albumId);
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
                Source = $"ms-appdata:///temp/TileImages/{albumId}"
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
