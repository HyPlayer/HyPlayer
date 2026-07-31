#nullable enable

using System;

namespace HyPlayer.LyricRenderer.Text;

public sealed class DefaultTextProgressResolver : ITextProgressResolver
{
    public TextRenderFrame Resolve(long currentTime, long lineStartTime, long lineEndTime,
        LyricTextLayoutSnapshot layout)
    {
        var currentTokenIndex = FindCurrentTokenIndex(layout, currentTime);
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
            CurrentLyricSourcePosition =
                GetCurrentSourcePosition(layout, currentTokenIndex, currentProgress, t => t.Text),
            CurrentTransliterationSourcePosition = GetCurrentSourcePosition(layout, currentTokenIndex, currentProgress,
                t => t.Transliteration ?? string.Empty),
            CurrentToken = currentToken
        };
    }

    private static int FindCurrentTokenIndex(LyricTextLayoutSnapshot layout, long currentTime)
    {
        for (var i = layout.Tokens.Count - 1; i >= 0; i--)
            if (layout.Tokens[i].StartTime <= currentTime)
                return i;

        return -1;
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
        LyricTextLayoutSnapshot layout,
        int currentTokenIndex,
        float currentProgress,
        Func<LyricTextToken, string> textSelector)
    {
        if (layout.Tokens.Count <= 0) return layout.Text.Length * Math.Clamp(currentProgress, 0, 1);

        if (currentTokenIndex < 0) return 0;

        var position = 0;
        for (var i = 0; i < currentTokenIndex && i < layout.Tokens.Count; i++)
            position += textSelector(layout.Tokens[i]).Length;

        return position + textSelector(layout.Tokens[currentTokenIndex]).Length * Math.Clamp(currentProgress, 0, 1);
    }
}