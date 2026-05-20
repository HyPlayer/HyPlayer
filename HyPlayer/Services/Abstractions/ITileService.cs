using HyPlayer.Domain.Music;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Threading;
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
