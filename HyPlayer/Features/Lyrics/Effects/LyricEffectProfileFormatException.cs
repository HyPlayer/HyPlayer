#nullable enable

using System;

namespace HyPlayer.Features.Lyrics.Effects;

public sealed class LyricEffectProfileFormatException : Exception
{
    public LyricEffectProfileFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
