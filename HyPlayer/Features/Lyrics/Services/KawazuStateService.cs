#nullable enable
using Kawazu;

namespace HyPlayer.Features.Lyrics.Services;

public sealed class KawazuStateService : IKawazuStateService
{
    public KawazuConverter? Converter { get; set; }
}