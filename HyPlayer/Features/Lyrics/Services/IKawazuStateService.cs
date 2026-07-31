#nullable enable
using Kawazu;

namespace HyPlayer.Features.Lyrics.Services;

public interface IKawazuStateService
{
    KawazuConverter? Converter { get; set; }
}