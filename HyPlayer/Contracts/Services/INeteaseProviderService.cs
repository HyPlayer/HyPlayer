using HyPlayer.Classes;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Contracts.Services
{
    public interface INeteaseProviderService
    {
        bool IsLoggedIn { get; }

        /// <summary> Get Recommended Resource. </summary>
        Task<List<ProvidableItemBase>> GetRecommendedResourceAsync(string? typeId = null, CancellationToken token = new());
    }
}
