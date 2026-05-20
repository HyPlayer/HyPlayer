using ALRC.Abstraction;
using ALRC.Converters;
using System;

namespace HyPlayer.Domain.Lyrics.LyricEnhancers;

public class NearbyLineAlignmentEnhancer : ILyricEnhancer<bool>
{
    public bool Extract(ALRCFile input)
    {
        return true;
    }

    public ALRCFile Enhance(bool input, ALRCFile target)
    {
        for (var index = 0; index < target.Lines.Count - 1; index++)
        {
            var targetLine = target.Lines[index];
            if (index >= target.Lines.Count - 1) break;
            var nextLine = target.Lines[index + 1];
            if (Math.Abs(nextLine.End - targetLine.End ?? long.MaxValue) < 1000)
            {
                // get the max
                targetLine.End = Math.Max(targetLine.End ?? 0, nextLine.End ?? 0);
                nextLine.End = targetLine.End;
            }

            if (Math.Abs(nextLine.Start - targetLine.Start ?? long.MaxValue) < 1000)
            {
                // get the min
                targetLine.Start = Math.Min(targetLine.Start ?? 0, nextLine.Start ?? 0);
                nextLine.Start = targetLine.Start;
            }

            if (Math.Abs(nextLine.Start - targetLine.End ?? long.MaxValue) < 1000)
            {
                // get the min
                targetLine.End = nextLine.Start;
            }
        }
        return target;
    }
}