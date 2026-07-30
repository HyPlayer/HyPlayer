namespace HyPlayer.LyricRenderer.Abstraction.Render;

// 特技特技加特技
public class RenderEffects
{
    /// <summary>
    /// 音译扫词
    /// </summary>
    public bool TransliterationScanning { get; set; } = true;

    /// <summary>
    /// 非逐字平滑扫词
    /// </summary>
    public bool SimpleLineScanning { get; set; } = true;

    /// <summary>
    /// 预渲染合成
    /// </summary>
    public bool CacheRenderTarget { get; set; } = false;

    /// <summary>
    /// 扫词样式
    /// </summary>
    public HyPlayer.Domain.LyricScanStyle ScanStyle { get; set; } = HyPlayer.Domain.LyricScanStyle.RectReveal;
}
