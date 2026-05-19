#nullable enable
using Kawazu;

namespace HyPlayer.Services.Abstractions;

public interface IKawazuStateService
{
    KawazuConverter? Converter { get; set; }
}
