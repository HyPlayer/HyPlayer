#nullable enable

using System;
using System.Linq;
using Windows.Foundation;

namespace HyPlayer.LyricRenderer.Text;

public sealed class DefaultTextProgressResolver : ITextProgressResolver
{
    public TextRenderFrame Resolve(long currentTime, long lineStartTime, long lineEndTime, LyricTextLayoutSnapshot layout)
    {
        var currentTokenIndex = layout.Tokens.Count > 0
            ? layout.Tokens.ToList().FindLastIndex(t => t.StartTime <= currentTime)
            : -1;

        var currentProgress = GetCurrentTokenProgress(currentTime, lineStartTime, lineEndTime, layout, currentTokenIndex);
        var beforeBounds = layout.TokenBounds.Take(Math.Max(currentTokenIndex, 0)).SelectMany(t => t).ToArray();
        var afterBounds = currentTokenIndex >= 0
            ? layout.TokenBounds.Skip(currentTokenIndex + 1).SelectMany(t => t).ToArray()
            : layout.TokenBounds.SelectMany(t => t).ToArray();
        var currentBounds = currentTokenIndex >= 0
            ? layout.TokenBounds.ElementAtOrDefault(currentTokenIndex) ?? []
            : [];
        var highlightBounds = currentTokenIndex >= 0 ? currentBounds : layout.ExpandedBounds;
        var currentToken = currentTokenIndex >= 0 ? layout.Tokens[currentTokenIndex] : null;

        return new TextRenderFrame
        {
            CurrentTokenIndex = currentTokenIndex,
            CurrentTokenProgress = currentProgress,
            BeforeTokenBounds = beforeBounds,
            CurrentTokenBounds = currentBounds,
            AfterTokenBounds = afterBounds,
            HighlightBounds = highlightBounds,
            FullLineBounds = layout.ExpandedBounds,
            CharacterBounds = layout.CharacterBounds.SelectMany(t => t).ToArray(),
            CurrentCharacterProgress = currentProgress,
            CurrentToken = currentToken
        };
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
}
