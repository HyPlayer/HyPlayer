using System;
using ALRC.Abstraction;
using ALRC.Converters;

namespace HyPlayer.Domain.Lyrics.LyricEnhancers;

public class NearbyLineAlignmentEnhancer : ILyricEnhancer<bool>
{
    internal const long SimultaneousToleranceMs = 100;
    internal const long GapClosingMs = 1000;

    public bool Extract(ALRCFile input)
    {
        return true;
    }

    public ALRCFile Enhance(bool input, ALRCFile target)
    {
        for (var index = 0; index < target.Lines.Count - 1; index++)
        {
            var targetLine = target.Lines[index];
            var nextLine = target.Lines[index + 1];
            if (targetLine.Start is { } start && nextLine.Start is { } nextStart &&
                Math.Abs(nextStart - start) <= SimultaneousToleranceMs)
            {
                targetLine.Start = Math.Min(start, nextStart);
                nextLine.Start = targetLine.Start;
                if (targetLine.End is { } end && nextLine.End is { } nextEnd &&
                    Math.Abs(nextEnd - end) <= SimultaneousToleranceMs)
                {
                    targetLine.End = Math.Max(end, nextEnd);
                    nextLine.End = targetLine.End;
                }

                continue;
            }

            if (targetLine.End is { } lineEnd && nextLine.Start is { } followingStart &&
                followingStart >= lineEnd && followingStart - lineEnd < GapClosingMs)
                targetLine.End = followingStart;
        }

        return target;
    }
}
