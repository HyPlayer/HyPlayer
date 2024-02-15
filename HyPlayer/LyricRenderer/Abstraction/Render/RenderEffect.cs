namespace HyPlayer.LyricRenderer.Abstraction.Render;

// 特技特技加特技
public class RenderEffects
{
    /// <summary>
    /// 歌词发光效果
    /// </summary>
    public bool FocusHighlighting { get; set; } = false;

    /// <summary>
    /// 音译扫词
    /// </summary>
    public bool TransliterationScanning { get; set; } = false;

    /// <summary>
    /// 非逐字平滑扫词
    /// </summary>
    public bool SimpleLineScanning { get; set; } = false;

    /// <summary>
    /// 焦点时放大
    /// </summary>
    public bool ScaleWhenFocusing { get; set; } = false;

    /// <summary>
    /// 歌词模糊
    /// </summary>
    public bool Blur { get; set; } = false;
}