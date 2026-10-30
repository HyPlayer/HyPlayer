using System.Globalization;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Playback.MediaSources;
using HyPlayer.PlayCore.Abstraction.Models.Resources;

namespace HyPlayer.Features.Playback.Services;

internal static class PlaybackQualityLevel
{
    internal static string Of(MusicResourceBase? resource)
    {
        if (resource is not NeteaseMusicResource netease)
            return string.Empty;

        return Resolve(netease.Level, netease.BitRate,
            resource is NeteaseMediaResource { Song: NeteaseRadioProgram });
    }

    internal static string Resolve(string? level, string? bitRate, bool isProgramme)
    {
        var measured = FromBitRate(bitRate);
        if (isProgramme && measured.Length > 0)
            return measured;

        return string.IsNullOrWhiteSpace(level) ? measured : level;
    }

    internal static string FromBitRate(string? bitRate)
    {
        if (!double.TryParse(bitRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return string.Empty;

        return value switch
        {
            < 160_000 => "standard",
            < 256_000 => "higher",
            < 400_000 => "exhigh",
            <= 1_411_200 => "lossless",
            _ => "hires"
        };
    }
}
