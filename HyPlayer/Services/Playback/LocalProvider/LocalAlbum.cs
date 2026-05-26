using HyPlayer.PlayCore.Abstraction.Models.Containers;

namespace HyPlayer.Services.Playback.LocalProvider;

public sealed class LocalAlbum : AlbumBase
{
    public override string ProviderId => LocalProvider.ProviderIdValue;
    public override string TypeId => "al";
}
