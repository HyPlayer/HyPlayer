using System.Linq;
using HyPlayer.Domain.Lyrics;

namespace HyPlayer.Features.Lyrics.Services.ThirdParty;

internal static class LyricWordTiming
{
    public static bool IsWordLevel(PureLyricInfo? info)
    {
        switch (info)
        {
            case KaraokLyricInfo karaoke:
                return !string.IsNullOrWhiteSpace(karaoke.KaraokLyric);
            case HyALRCLyricInfo { ALRC.Lines: { } lines }:
                var textLines = lines
                    .Where(line => string.IsNullOrEmpty(line.ParentLineId))
                    .Where(line => !string.IsNullOrWhiteSpace(line.RawText) ||
                                   line.Words?.Any(word => !string.IsNullOrWhiteSpace(word.Word)) == true)
                    .ToList();
                return textLines.Count > 0 && textLines.Count(line => line.Words is { Count: > 1 }) * 2 >= textLines.Count;
            default:
                return false;
        }
    }
}
