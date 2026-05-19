#nullable enable
using HyPlayer.Services.Abstractions;
using Kawazu;

namespace HyPlayer.Services;

public sealed class KawazuStateService : IKawazuStateService
{
    public KawazuConverter? Converter { get; set; }
}
