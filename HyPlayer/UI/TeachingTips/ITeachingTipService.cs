using System;
using System.Collections.Generic;

namespace HyPlayer.UI.TeachingTips;

public sealed record TeachingTipItem(string Title, string? Subtitle, string? ActionText = null, Action? Action = null);

public interface ITeachingTipService
{
    Queue<TeachingTipItem> Items { get; }
    object? Tip { get; set; }
    void Roll(bool passiveRoll = true);
    void Clear();
    void InvokeAction();
}