#nullable enable

using System;
using System.Collections.Generic;

namespace HyPlayer.LyricRenderer.Text;

public sealed class DefaultTextProgressResolver : ITextProgressResolver
{
    public TextRenderFrame Resolve(long currentTime, long lineStartTime, long lineEndTime,
        LyricTextLayoutSnapshot layout)
    {
        var currentTokenIndex = FindCurrentTokenIndex(layout.TokenStartTimes, currentTime,
            layout.TokenStartTimesAreSorted);
        var currentProgress =
            GetCurrentTokenProgress(currentTime, lineStartTime, lineEndTime, layout, currentTokenIndex);
        var lineProgress = GetLineProgress(currentTime, lineStartTime, lineEndTime);
        var currentToken = (uint)currentTokenIndex < (uint)layout.Tokens.Count
            ? layout.Tokens[currentTokenIndex]
            : null;
        var currentTokenDuration = currentToken is null
            ? lineEndTime - lineStartTime
            : currentToken.EndTime - currentToken.StartTime;

        return new TextRenderFrame
        {
            CurrentTokenIndex = currentTokenIndex,
            CurrentTokenProgress = currentProgress,
            CurrentTokenDuration = currentTokenDuration,
            LineProgress = lineProgress,
            CurrentLyricSourcePosition = GetCurrentSourcePosition(layout.Text, layout.TokenSourceStarts,
                layout.Tokens, currentTokenIndex, currentProgress, static token => token.Text),
            CurrentTransliterationSourcePosition = GetCurrentSourcePosition(layout.Text,
                layout.TransliterationSourceStarts, layout.Tokens, currentTokenIndex, currentProgress,
                static token => token.Transliteration ?? string.Empty),
            CurrentToken = currentToken
        };
    }

    internal static int FindCurrentTokenIndex(IReadOnlyList<long> tokenStartTimes, long currentTime,
        bool areSorted = true)
    {
        // Providers may supply overlapping words out of time order. Preserve the
        // original last-matching-index semantics without reordering lyric text.
        if (!areSorted)
        {
            for (var index = tokenStartTimes.Count - 1; index >= 0; index--)
                if (tokenStartTimes[index] <= currentTime) return index;
            return -1;
        }
        var low = 0;
        var high = tokenStartTimes.Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (tokenStartTimes[middle] <= currentTime) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    private static float GetCurrentTokenProgress(
        long currentTime,
        long lineStartTime,
        long lineEndTime,
        LyricTextLayoutSnapshot layout,
        int currentTokenIndex)
    {
        if (layout.Tokens.Count <= 0)
        {
            var duration = lineEndTime - lineStartTime;
            if (duration <= 0) return 1;
            return Math.Clamp((currentTime - lineStartTime) * 1f / duration, 0, 1);
        }

        if (currentTokenIndex == -1) return 0;
        var currentToken = layout.Tokens[currentTokenIndex];
        var tokenDuration = currentToken.EndTime - currentToken.StartTime;
        if (tokenDuration <= 0) return 1;
        return Math.Clamp((currentTime - currentToken.StartTime) * 1.0f / tokenDuration, 0, 1);
    }

    private static float GetLineProgress(long currentTime, long lineStartTime, long lineEndTime)
    {
        var duration = lineEndTime - lineStartTime;
        if (duration <= 0) return 1;
        return Math.Clamp((currentTime - lineStartTime) * 1f / duration, 0, 1);
    }

    private static float GetCurrentSourcePosition(
        string text,
        IReadOnlyList<int> sourceStarts,
        IReadOnlyList<LyricTextToken> tokens,
        int currentTokenIndex,
        float currentProgress,
        Func<LyricTextToken, string> textSelector)
    {
        if (tokens.Count <= 0) return text.Length * Math.Clamp(currentProgress, 0, 1);

        if (currentTokenIndex < 0) return 0;

        var token = tokens[currentTokenIndex];
        return sourceStarts[currentTokenIndex] + textSelector(token).Length * Math.Clamp(currentProgress, 0, 1);
    }
}
