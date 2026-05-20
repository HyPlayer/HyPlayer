#nullable enable
using HyPlayer.Services.Abstractions;
using Kawazu;

namespace HyPlayer.Services.Lyrics;

public sealed class KawazuStateService : IKawazuStateService
{
    public KawazuConverter? Converter { get; set; }
}
