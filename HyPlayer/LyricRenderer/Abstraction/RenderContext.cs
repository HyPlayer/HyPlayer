using System.Collections.Generic;
using HyPlayer.LyricRenderer.Abstraction.Render;

namespace HyPlayer.LyricRenderer.Abstraction;

public class RenderContext
{
    /// <summary>
    /// 是否显示调试信息
    /// 请合理使用
    /// </summary>
    public bool Debug { get; set; } = false;
    
    /// <summary>
    /// 所有歌词
    /// </summary>
    public List<RenderingLyricLine> LyricLines { get; set; } = new();

    /// <summary>
    /// 视图宽度
    /// </summary>
    public double ViewWidth { get; set; }
    
    /// <summary>
    /// 视图高度
    /// </summary>
    public double ViewHeight { get; set; }

    public double ItemWidth { get; set; }
    
    /// <summary>
    /// 播放时间 单位毫秒
    /// </summary>
    public long CurrentLyricTime { get; set; }
    
    /// <summary>
    /// 渲染时间刻 单位微秒
    /// 精细动画时使用
    /// </summary>
    public long RenderTick { get; set; }

    /// <summary>
    /// 当前主渲染的歌词行
    /// </summary>
    public RenderingLyricLine CurrentLyricLine { get; set; }
    
    /// <summary>
    /// 当前主渲染的歌词行号
    /// </summary>
    public int CurrentLyricLineIndex { get; set; }

    /// <summary>
    /// 在视图范围内被渲染的歌词行
    /// </summary>
    public List<RenderingLyricLine> RenderingLyricLines { get; } = new();
    
    /// <summary>
    /// 歌词的偏移
    /// </summary>
    public Dictionary<int, LineRenderOffset> RenderOffsets { get; } = new();

    /// <summary>
    /// 上一关键帧的偏移快照
    /// </summary>
    public Dictionary<int, LineRenderOffset> SnapshotRenderOffsets { get; } = new();

    /// <summary>
    /// 歌词滚动的缓动计算器
    /// </summary>
    public LineRollingCalculator LineRollingEaseCalculator { get; set; }

    /// <summary>
    /// 缺省的排版设置
    /// </summary>
    public RenderTypography PreferTypography { get; set; } = new();
    
    /// <summary>
    /// 当前的关键帧
    /// </summary>
    public long CurrentKeyframe { get; set; }

    /// <summary>
    /// 是否正在滚动
    /// </summary>
    public bool IsScrolling { get; set; }

    /// <summary>
    /// 滚动的增量
    /// </summary>
    public long ScrollingDelta { get; set; }

    /// <summary>
    /// 指针设备的焦点行号
    /// </summary>
    public int PointerFocusingIndex { get; set; }

    /// <summary>
    /// 歌曲 BPM
    /// </summary>
    public double BeatPerMinute { get; set; }

    /// <summary>
    /// 歌词距离顶部的距离比例
    /// </summary>
    public double LyricPaddingTopRatio { get; set; }

    /// <summary>
    /// 歌词的宽度比例
    /// </summary>
    public double LyricWidthRatio { get; set; }
}