using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Platform.Playback.LocalProvider;

public sealed class LocalArtist : PersonBase
{
    public override string ProviderId => LocalProvider.ProviderIdValue;
    public override string TypeId => "ar";

    public override Task<List<ContainerBase>> GetSubContainerAsync(CancellationToken ctk = default)
    {
        return Task.FromResult(new List<ContainerBase>());
    }
}
