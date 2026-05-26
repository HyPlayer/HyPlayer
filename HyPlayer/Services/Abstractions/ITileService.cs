using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace HyPlayer.Services.Abstractions
{
    public interface ITileService
    {
        Task UpdateTile(SingleSongBase item, IRandomAccessStream coverStream);
        Task<TileBackgroundImage?> GetTileBackgroundAsync(SingleSongBase item, IRandomAccessStream stream);
        Task ClearAllTiles();
    }
}
