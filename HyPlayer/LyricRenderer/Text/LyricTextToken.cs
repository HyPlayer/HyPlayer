#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public class LyricTextToken(string text, long startTime, long endTime, string? transliteration)
{
    public string Text { get; set; } = text;
    public long StartTime { get; set; } = startTime;
    public long EndTime { get; set; } = endTime;
    public long Duration { get; set; } = endTime - startTime;
    public string? Transliteration { get; set; } = transliteration;
    public int CharacterCount => Text.Length;
}