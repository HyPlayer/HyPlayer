namespace HyPlayer.LyricRenderer.Abstraction.Render;

// 特技特技加特技
public class RenderEffects
{
    /// <summary>
    /// 歌词发光效果
    /// </summary>
    public bool FocusHighlighting { get; set; } = true;

    /// <summary>
    /// 音译扫词
    /// </summary>
    public bool TransliterationScanning { get; set; } = true;

    /// <summary>
    /// 非逐字平滑扫词
    /// </summary>
    public bool SimpleLineScanning { get; set; } = true;

    /// <summary>
    /// 焦点时放大
    /// </summary>
    public bool ScaleWhenFocusing { get; set; } = true;

    /// <summary>
    /// 歌词模糊
    /// </summary>
    public bool Blur { get; set; } = true;
}