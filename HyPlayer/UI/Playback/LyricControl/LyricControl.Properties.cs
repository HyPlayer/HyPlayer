using System;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using Microsoft.Graphics.Canvas.Text;

namespace HyPlayer.UI.Playback.LyricControl;

public partial class LyricControl
{
    public static readonly DependencyProperty BlurAmountProperty =
        DependencyProperty.Register(nameof(BlurAmount), typeof(int), typeof(LyricControl),
            new PropertyMetadata(16, OnBlurAmountChanged));

    public static readonly DependencyProperty CurrentTimeProperty =
        DependencyProperty.Register(nameof(CurrentTime), typeof(TimeSpan), typeof(LyricControl),
            new PropertyMetadata(TimeSpan.Zero, OnCurrentTimeChanged));

    public new static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(LyricControl),
            new PropertyMetadata(FontStyle.Normal, OnFontStyleChanged));

    public static readonly DependencyProperty HorizontalTextAlignmentProperty =
        DependencyProperty.Register(nameof(HorizontalTextAlignment), typeof(CanvasHorizontalAlignment),
            typeof(LyricControl),
            new PropertyMetadata(CanvasHorizontalAlignment.Center, OnHorizontalTextAlignmentChanged));

    public static readonly DependencyProperty VerticalTextAlignmentProperty =
        DependencyProperty.Register(nameof(VerticalTextAlignment), typeof(CanvasVerticalAlignment),
            typeof(LyricControl), new PropertyMetadata(CanvasVerticalAlignment.Center, OnVerticalTextAlignmentChanged));

    public static readonly DependencyProperty WordWrappingProperty =
        DependencyProperty.Register(nameof(WordWrapping), typeof(CanvasWordWrapping), typeof(LyricControl),
            new PropertyMetadata(CanvasWordWrapping.Wrap, OnVerticalWordWrappingChanged));

    public static readonly DependencyProperty TextFontFamilyProperty =
        DependencyProperty.Register(nameof(TextFontFamily), typeof(string), typeof(LyricControl),
            new PropertyMetadata(FontFamily.XamlAutoFontFamily.Source, OnTextFontFamilyChanged));

    public static readonly DependencyProperty EaseFunctionProperty =
        DependencyProperty.Register(nameof(EaseFunction), typeof(EaseFunctionBase), typeof(LyricControl),
            new PropertyMetadata(new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3 },
                OnEaseFunctionChanged));

    public new static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(LyricControl),
            new PropertyMetadata(FontWeights.SemiBold, OnFontWeightChanged));

    public new static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(int), typeof(LyricControl),
            new PropertyMetadata(28, OnFontSizeChanged));

    public static readonly DependencyProperty LyricColorProperty =
        DependencyProperty.Register(nameof(LyricColor), typeof(Color), typeof(LyricControl),
            new PropertyMetadata(Color.FromArgb(50, 200, 200, 200), OnLyricColorChanged));

    public static readonly DependencyProperty AccentLyricColorProperty =
        DependencyProperty.Register(nameof(AccentLyricColor), typeof(Color), typeof(LyricControl),
            new PropertyMetadata(Colors.White, OnAccentLyricColorChanged));

    public static readonly DependencyProperty ShadowColorProperty =
        DependencyProperty.Register(nameof(ShadowColor), typeof(Color), typeof(LyricControl),
            new PropertyMetadata(Color.FromArgb(200, 0, 0, 0), OnShadowColorChanged));

    public static readonly DependencyProperty LyricProperty =
        DependencyProperty.Register(nameof(Lyric), typeof(SongLyric), typeof(LyricControl),
            new PropertyMetadata(new SongLyric { LyricLine = new LrcLyricsLine("无歌词", TimeSpan.Zero) },
                OnLyricChanged));

    private Color _accentLyricColor = Colors.White;

    private int _blurAmount = 16;

    private TimeSpan _currentTime = TimeSpan.Zero;

    private EaseFunctionBase _easeFunction = new CustomExponentialEase
        { EasingMode = EasingMode.EaseOut, Exponent = 3 };

    private int _fontSize = 28;

    private FontStyle _fontStyle = FontStyle.Normal;

    private FontWeight _fontWeight = FontWeights.SemiBold;

    private CanvasHorizontalAlignment _horizontalTextAlignment = CanvasHorizontalAlignment.Center;

    private SongLyric _lyric = new() { LyricLine = new LrcLyricsLine("无歌词", TimeSpan.Zero) };

    private Color _lyricColor = Color.FromArgb(50, 200, 200, 200);

    private Color _shadowColor = Color.FromArgb(200, 0, 0, 0);

    private string _textFontFamily = FontFamily.XamlAutoFontFamily.Source;

    private CanvasVerticalAlignment _verticalTextAlignment = CanvasVerticalAlignment.Center;

    private CanvasWordWrapping _wordWrapping = CanvasWordWrapping.Wrap;

    /// <summary>
    ///     当前播放的时间
    /// </summary>
    public int BlurAmount
    {
        get => (int)GetValue(BlurAmountProperty);
        set => SetValue(BlurAmountProperty, value);
    }

    /// <summary>
    ///     当前播放的时间
    /// </summary>
    public TimeSpan CurrentTime
    {
        get => (TimeSpan)GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }


    /// <summary>
    ///     快速渲染模式
    /// </summary>
    public bool QuickRenderMode { get; set; }


    /// <summary>
    ///     文字样式(斜体等)
    /// </summary>
    public new FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    ///     文字水平对齐方式
    /// </summary>
    public CanvasHorizontalAlignment HorizontalTextAlignment
    {
        get => (CanvasHorizontalAlignment)GetValue(HorizontalTextAlignmentProperty);
        set => SetValue(HorizontalTextAlignmentProperty, value);
    }

    /// <summary>
    ///     文字竖直对齐方式
    /// </summary>
    public CanvasVerticalAlignment VerticalTextAlignment
    {
        get => (CanvasVerticalAlignment)GetValue(VerticalTextAlignmentProperty);
        set => SetValue(VerticalTextAlignmentProperty, value);
    }

    /// <summary>
    ///     文字换行
    /// </summary>
    public CanvasWordWrapping WordWrapping
    {
        get => (CanvasWordWrapping)GetValue(WordWrappingProperty);
        set => SetValue(WordWrappingProperty, value);
    }

    /// <summary>
    ///     字体
    /// </summary>
    public string TextFontFamily
    {
        get => (string)GetValue(TextFontFamilyProperty);
        set => SetValue(TextFontFamilyProperty, value);
    }

    /// <summary>
    ///     歌词播放的缓动曲线
    /// </summary>
    public EaseFunctionBase EaseFunction
    {
        get => (EaseFunctionBase)GetValue(EaseFunctionProperty);
        set => SetValue(EaseFunctionProperty, value);
    }

    /// <summary>
    ///     字重(粗体等)
    /// </summary>
    public new FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    ///     歌词颜色(未激活)
    /// </summary>
    public new int FontSize
    {
        get => (int)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    ///     歌词颜色(未激活)
    /// </summary>
    public Color LyricColor
    {
        get => (Color)GetValue(LyricColorProperty);
        set => SetValue(LyricColorProperty, value);
    }

    /// <summary>
    ///     歌词颜色(激活)
    /// </summary>

    public Color AccentLyricColor
    {
        get => (Color)GetValue(AccentLyricColorProperty);
        set => SetValue(AccentLyricColorProperty, value);
    }

    /// <summary>
    ///     阴影颜色
    /// </summary>
    public Color ShadowColor
    {
        get => (Color)GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    /// <summary>
    ///     歌词
    /// </summary>
    public SongLyric Lyric
    {
        get => (SongLyric)GetValue(LyricProperty);
        set => SetValue(LyricProperty, value);
    }

    private static void OnBlurAmountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._blurAmount = (int)e.NewValue;
    }

    private static void OnCurrentTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._currentTime = (TimeSpan)e.NewValue;
    }

    private static void OnFontStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._fontStyle = (FontStyle)e.NewValue;
    }

    private static void OnHorizontalTextAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._horizontalTextAlignment = (CanvasHorizontalAlignment)e.NewValue;
    }

    private static void OnVerticalTextAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._verticalTextAlignment = (CanvasVerticalAlignment)e.NewValue;
    }

    private static void OnVerticalWordWrappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._wordWrapping = (CanvasWordWrapping)e.NewValue;
    }

    private static void OnTextFontFamilyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._textFontFamily = (string)e.NewValue;
    }

    private static void OnEaseFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._easeFunction = (EaseFunctionBase)e.NewValue;
    }

    private static void OnFontWeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._fontWeight = (FontWeight)e.NewValue;
    }

    private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._fontSize = (int)e.NewValue;
    }

    private static void OnLyricColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._lyricColor = (Color)e.NewValue;
    }

    private static void OnAccentLyricColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._accentLyricColor = (Color)e.NewValue;
    }

    private static void OnShadowColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._shadowColor = (Color)e.NewValue;
    }

    private static void OnLyricChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricControl)d)._lyric = (SongLyric)e.NewValue;
    }
}