#nullable enable

using System;
using System.Collections.Generic;

namespace HyPlayer.LyricRenderer.Text;

internal readonly record struct LyricEmphasisGroup(long Start, float Duration, float Amount, float Blur, int Count)
{
    public long ActiveFrom => Start - LyricEmphasisPlan.FloatLead;
    public long ActiveUntil => Start + (long)(Duration / 2.5f + Duration * LyricEmphasisPlan.FloatDurationScale);
}

internal readonly record struct LyricEmphasisSample(
    float Scale,
    float OffsetX,
    float OffsetY,
    float GlowOpacity,
    float GlowRadius);

internal sealed class LyricEmphasisPlan
{
    internal const long FloatLead = 400;
    internal const float FloatDurationScale = 1.4f;
    private const long MinimumDuration = 1000;

    private readonly LyricEmphasisGroup[] _groups;
    private readonly int[] _clusterGroups;
    private readonly int[] _clusterIndexes;
    private readonly long _activeFrom;
    private readonly long _activeUntil;

    private LyricEmphasisPlan(LyricEmphasisGroup[] groups, int[] clusterGroups, int[] clusterIndexes)
    {
        _groups = groups;
        _clusterGroups = clusterGroups;
        _clusterIndexes = clusterIndexes;
        _activeFrom = long.MaxValue;
        _activeUntil = long.MinValue;
        foreach (var group in groups)
        {
            _activeFrom = Math.Min(_activeFrom, group.ActiveFrom);
            _activeUntil = Math.Max(_activeUntil, group.ActiveUntil);
        }
    }

    public static LyricEmphasisPlan? Create(
        IReadOnlyList<LyricTextToken> tokens,
        IReadOnlyList<LyricGlyphCluster> clusters)
    {
        if (tokens.Count == 0 || clusters.Count == 0) return null;

        var tokenGroups = new int[tokens.Count];
        Array.Fill(tokenGroups, -1);
        var pending = new List<(long Start, long End, bool IsLastWord)>();

        var lastWordIndex = tokens.Count - 1;
        while (lastWordIndex > 0 && string.IsNullOrWhiteSpace(tokens[lastWordIndex].Text)) lastWordIndex--;

        var index = 0;
        while (index < tokens.Count)
        {
            var first = index;
            var cjk = IsCjk(tokens[index].Text);
            if (!cjk)
            {
                while (index + 1 < tokens.Count && !IsCjk(tokens[index + 1].Text) &&
                       !EndsWithSpace(tokens[index].Text) && !StartsWithSpace(tokens[index + 1].Text))
                    index++;
            }

            var last = index;
            index++;

            var emphasize = false;
            var text = string.Empty;
            for (var i = first; i <= last; i++)
            {
                emphasize |= ShouldEmphasize(tokens[i].Text, tokens[i].EndTime - tokens[i].StartTime);
                text += tokens[i].Text;
            }

            var start = tokens[first].StartTime;
            var end = tokens[last].EndTime;
            if (!cjk) emphasize |= ShouldEmphasize(text, end - start);
            if (!emphasize) continue;

            for (var i = first; i <= last; i++) tokenGroups[i] = pending.Count;
            pending.Add((start, end, lastWordIndex >= first && lastWordIndex <= last));
        }

        if (pending.Count == 0) return null;

        var counts = new int[pending.Count];
        var clusterGroups = new int[clusters.Count];
        var clusterIndexes = new int[clusters.Count];
        for (var i = 0; i < clusters.Count; i++)
        {
            clusterGroups[i] = -1;
            var token = clusters[i].TokenStartIndex;
            if ((uint)token >= (uint)tokenGroups.Length || tokenGroups[token] < 0) continue;
            if (string.IsNullOrWhiteSpace(clusters[i].BaseState.TextString)) continue;
            clusterGroups[i] = tokenGroups[token];
            clusterIndexes[i] = counts[tokenGroups[token]]++;
        }

        var groups = new LyricEmphasisGroup[pending.Count];
        var any = false;
        for (var i = 0; i < groups.Length; i++)
        {
            var (start, end, isLastWord) = pending[i];
            var duration = (float)Math.Max(MinimumDuration, end - start);
            var amount = duration / 2000f;
            amount = amount > 1 ? MathF.Sqrt(amount) : amount * amount * amount;
            var blur = duration / 3000f;
            blur = blur > 1 ? MathF.Sqrt(blur) : blur * blur * blur;
            amount *= 0.6f;
            blur *= 0.5f;
            if (isLastWord)
            {
                amount *= 1.6f;
                blur *= 1.5f;
                duration *= 1.2f;
            }

            any |= counts[i] > 0;
            groups[i] = new LyricEmphasisGroup(start, duration, Math.Min(1.2f, amount), Math.Min(0.8f, blur), counts[i]);
        }

        return any ? new LyricEmphasisPlan(groups, clusterGroups, clusterIndexes) : null;
    }

    public bool IsActiveAt(long time)
    {
        if (time < _activeFrom || time > _activeUntil) return false;
        foreach (var group in _groups)
            if (group.Count > 0 && time >= group.ActiveFrom && time <= group.ActiveUntil)
                return true;
        return false;
    }

    public bool TrySample(int clusterIndex, long time, float fontSize, out LyricEmphasisSample sample)
    {
        sample = default;
        if (time < _activeFrom || time > _activeUntil || (uint)clusterIndex >= (uint)_clusterGroups.Length)
            return false;
        var groupIndex = _clusterGroups[clusterIndex];
        if (groupIndex < 0) return false;
        var group = _groups[groupIndex];
        if (time < group.ActiveFrom || time > group.ActiveUntil) return false;

        var em = Math.Max(1f, fontSize);
        var charIndex = _clusterIndexes[clusterIndex];
        var charStart = group.Start + group.Duration / 2.5f / group.Count * charIndex;
        var progress = Math.Clamp((time - charStart) / group.Duration, 0f, 1f);
        var strength = Ease(progress);
        var floatProgress = Math.Clamp((time - (charStart - FloatLead)) / (group.Duration * FloatDurationScale), 0f, 1f);
        var floatY = -MathF.Sin(floatProgress * MathF.PI) * 0.05f * em;
        sample = new LyricEmphasisSample(
            1 + strength * 0.1f * group.Amount,
            -strength * 0.03f * group.Amount * (group.Count / 2f - charIndex) * em,
            -strength * 0.025f * group.Amount * em + floatY,
            strength * group.Blur,
            Math.Min(0.3f, group.Blur * 0.3f) * em);
        return true;
    }

    private static bool ShouldEmphasize(string word, long duration)
    {
        if (duration < MinimumDuration) return false;
        if (IsCjk(word)) return true;
        var length = word.AsSpan().Trim().Length;
        return length is > 1 and <= 7;
    }

    private static bool IsCjk(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or
                >= '가' and <= '힯' or >= '豈' and <= '﫿' or >= 'ᄀ' and <= 'ᇿ')
                return true;
        }

        return false;
    }

    private static bool EndsWithSpace(string text) => text.Length > 0 && char.IsWhiteSpace(text[^1]);

    private static bool StartsWithSpace(string text) => text.Length > 0 && char.IsWhiteSpace(text[0]);

    private static float Ease(float x)
    {
        const float middle = 0.5f;
        return x < middle
            ? CubicBezier(0.2f, 0.4f, 0.58f, 1f, x / middle)
            : 1 - CubicBezier(0.3f, 0f, 0.58f, 1f, (x - middle) / (1 - middle));
    }

    private static float CubicBezier(float x1, float y1, float x2, float y2, float x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var current = Sample(x1, x2, t) - x;
            var slope = SampleDerivative(x1, x2, t);
            if (MathF.Abs(current) < 1e-5f) break;
            if (MathF.Abs(slope) < 1e-6f) break;
            t = Math.Clamp(t - current / slope, 0, 1);
        }

        return Sample(y1, y2, t);
    }

    private static float Sample(float p1, float p2, float t)
    {
        var u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }

    private static float SampleDerivative(float p1, float p2, float t)
    {
        var u = 1 - t;
        return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }
}
