using System.Collections.Generic;

namespace HyPlayer.Services.Abstractions;

public interface ITeachingTipService
{
    Queue<KeyValuePair<string, string?>> Items { get; }
    object? Tip { get; set; }
    void Roll(bool passiveRoll = true);
    void Clear();
}
