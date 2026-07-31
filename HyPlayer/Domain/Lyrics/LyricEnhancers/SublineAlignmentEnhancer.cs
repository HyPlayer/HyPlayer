using System;
using System.Linq;
using ALRC.Abstraction;
using ALRC.Converters;

namespace HyPlayer.Domain.Lyrics.LyricEnhancers;

public class SublineAlignmentEnhancer : ILyricEnhancer<bool>
{
    public bool Extract(ALRCFile input)
    {
        return true;
    }

    public ALRCFile Enhance(bool input, ALRCFile target)
    {
        var validLines = target.Lines.Where(t => !string.IsNullOrEmpty(t.Id)).ToDictionary(t => t.Id);
        foreach (var line in target.Lines)
        {
            if (line.ParentLineId is null)
                continue;
            if (!validLines.TryGetValue(line.ParentLineId, out var parentLine)) continue;
            var minStart = Math.Min(line.Start ?? 0, parentLine.Start ?? 0);
            var maxEnd = Math.Max(line.End ?? 0, parentLine.End ?? 0);

            if (line.Words is { Count: 0 })
                line.Words =
                [
                    new ALRCWord
                    {
                        Start = line.Start ?? 0,
                        End = line.End ?? 0,
                        Word = line.RawText ?? ""
                    }
                ];

            if (parentLine.Words is { Count: 0 })
                parentLine.Words =
                [
                    new ALRCWord
                    {
                        Start = parentLine.Start ?? 0,
                        End = parentLine.End ?? 0,
                        Word = parentLine.RawText ?? ""
                    }
                ];

            line.Start = minStart;
            line.End = maxEnd;
            parentLine.Start = minStart;
            parentLine.End = maxEnd;
        }

        return target;
    }
}