using HyPlayer.Classes;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace HyPlayer.Services.Abstractions
{
    public interface ITileService
    {
        Task UpdateTile(HyPlayItem item, IRandomAccessStream coverStream);
        Task<TileBackgroundImage?> GetTileBackgroundAsync(HyPlayItem item, IRandomAccessStream stream);
        Task ClearAllTiles();
    }
}
