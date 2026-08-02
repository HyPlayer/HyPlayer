using ALRC.Abstraction;
using ALRC.Converters;

namespace HyPlayer.Domain.Lyrics.LyricEnhancers;

public class BreathLineEnhancer : ILyricEnhancer<bool>
{
    public bool Extract(ALRCFile input)
    {
        return true;
    }

    public ALRCFile Enhance(bool input, ALRCFile target)
    {
        long lastTime = 0;
        var total = target.Lines.Count;
        for (var index = 0; index < total; index++)
        {
            var targetLine = target.Lines[index];
            if (targetLine.Start - lastTime > 7000)
            {
                // append a breath line
                var breathLine = new ALRCLine
                {
                    Start = lastTime,
                    End = targetLine.Start
                };
                target.Lines.Insert(index, breathLine);
                index++;
                total++;
            }

            if (string.IsNullOrWhiteSpace(targetLine.RawText) && targetLine.Words?.Count is < 0)
                if (targetLine.End - targetLine.Start < 1000)
                {
                    target.Lines.RemoveAt(index);
                    index--;
                    total--;
                }

            lastTime = targetLine.End is 0 or null ? int.MaxValue : targetLine.End.Value;
        }

        return target;
    }
}