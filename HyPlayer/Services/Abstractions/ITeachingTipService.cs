using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace HyPlayer.Services.Abstractions;

public interface ITeachingTipService
{
    Queue<KeyValuePair<string, string?>> Items { get; }
    TeachingTip? Tip { get; set; }
    void Enqueue(KeyValuePair<string, string?> item);
    void Roll(bool passiveRoll = true);
    void Clear();
}
