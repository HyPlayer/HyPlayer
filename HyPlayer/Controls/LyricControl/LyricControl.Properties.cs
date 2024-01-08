using HyPlayer.Classes;
using LyricParser.Abstraction;
using Microsoft.Graphics.Canvas.Text;
using System;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Controls.LyricControl
{
    public partial class LyricControl
    {
        /// <summary>
        /// 当前播放的时间
        /// </summary>
        public int BlurAmount
        {
            get => (int)GetValue(CurrentTimeProperty);
            set => SetValue(CurrentTimeProperty, value);
        }

        public static readonly DependencyProperty BlurAmountProperty =
            DependencyProperty.Register(nameof(BlurAmount), typeof(int), typeof(LyricControl), new PropertyMetadata(16, OnBlurAmountChanged));

        private int _blurAmount = 16;

        private static void OnBlurAmountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._blurAmount = (int)e.NewValue;
        }

        /// <summary>
        /// 当前播放的时间
        /// </summary>
        public TimeSpan CurrentTime
        {
            get => (TimeSpan)GetValue(CurrentTimeProperty);
            set => SetValue(CurrentTimeProperty, value);
        }

        public static readonly DependencyProperty CurrentTimeProperty =
            DependencyProperty.Register(nameof(CurrentTime), typeof(TimeSpan), typeof(LyricControl), new PropertyMetadata(TimeSpan.Zero, OnCurrentTimeChanged));

        private TimeSpan _currentTime = TimeSpan.Zero;

        private static void OnCurrentTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._currentTime = (TimeSpan)e.NewValue;
        }


        /// <summary>
        /// 快速渲染模式
        /// </summary>
        public bool QuickRenderMode { get; set; }




        /// <summary>
        /// 文字样式(斜体等)
        /// </summary>
        public new FontStyle FontStyle
        {
            get => (FontStyle)GetValue(FontStyleProperty);
            set => SetValue(FontStyleProperty, value);
        }

        public new static readonly DependencyProperty FontStyleProperty =
            DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(LyricControl), new PropertyMetadata(FontStyle.Normal, OnFontStyleChanged));

        private FontStyle _fontStyle = FontStyle.Normal;

        private static void OnFontStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._fontStyle = (FontStyle)e.NewValue;
        }

        /// <summary>
        /// 文字水平对齐方式
        /// </summary>
        public CanvasHorizontalAlignment HorizontalTextAlignment
        {
            get => (CanvasHorizontalAlignment)GetValue(HorizontalTextAlignmentProperty);
            set => SetValue(HorizontalTextAlignmentProperty, value);
        }

        public static readonly DependencyProperty HorizontalTextAlignmentProperty =
            DependencyProperty.Register(nameof(CurrentTime), typeof(CanvasHorizontalAlignment), typeof(LyricControl), new PropertyMetadata(CanvasHorizontalAlignment.Center, OnHorizontalTextAlignmentChanged));

        private CanvasHorizontalAlignment _horizontalTextAlignment = CanvasHorizontalAlignment.Center;

        private static void OnHorizontalTextAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._horizontalTextAlignment = (CanvasHorizontalAlignment)e.NewValue;
        }

        /// <summary>
        /// 文字竖直对齐方式
        /// </summary>
        public CanvasVerticalAlignment VerticalTextAlignment
        {
            get => (CanvasVerticalAlignment)GetValue(VerticalTextAlignmentProperty);
            set => SetValue(VerticalTextAlignmentProperty, value);
        }

        public static readonly DependencyProperty VerticalTextAlignmentProperty =
            DependencyProperty.Register(nameof(VerticalTextAlignment), typeof(CanvasVerticalAlignment), typeof(LyricControl), new PropertyMetadata(CanvasVerticalAlignment.Center, OnVerticalTextAlignmentChanged));

        private CanvasVerticalAlignment _verticalTextAlignment = CanvasVerticalAlignment.Center;

        private static void OnVerticalTextAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._verticalTextAlignment = (CanvasVerticalAlignment)e.NewValue;
        }

        /// <summary>
        /// 文字换行
        /// </summary>
        public CanvasWordWrapping WordWrapping
        {
            get => (CanvasWordWrapping)GetValue(WordWrappingProperty);
            set => SetValue(WordWrappingProperty, value);
        }

        public static readonly DependencyProperty WordWrappingProperty =
            DependencyProperty.Register(nameof(WordWrapping), typeof(CanvasWordWrapping), typeof(LyricControl), new PropertyMetadata(CanvasWordWrapping.Wrap, OnVerticalWordWrappingChanged));

        private CanvasWordWrapping _wordWrapping = CanvasWordWrapping.Wrap;

        private static void OnVerticalWordWrappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._wordWrapping = (CanvasWordWrapping)e.NewValue;
        }

        /// <summary>
        /// 字体
        /// </summary>
        public string TextFontFamily
        {
            get => (string)GetValue(TextFontFamilyProperty);
            set => SetValue(TextFontFamilyProperty, value);
        }

        public static readonly DependencyProperty TextFontFamilyProperty =
            DependencyProperty.Register(nameof(TextFontFamily), typeof(string), typeof(LyricControl), new PropertyMetadata(FontFamily.XamlAutoFontFamily.Source, OnTextFontFamilyChanged));

        private string _textFontFamily = FontFamily.XamlAutoFontFamily.Source;

        private static void OnTextFontFamilyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._textFontFamily = (string)e.NewValue;
        }

        /// <summary>
        /// 歌词播放的缓动曲线
        /// </summary>
        public EaseFunctionBase EaseFunction
        {
            get => (EaseFunctionBase)GetValue(EaseFunctionProperty);
            set => SetValue(EaseFunctionProperty, value);
        }

        public static readonly DependencyProperty EaseFunctionProperty =
            DependencyProperty.Register(nameof(EaseFunction), typeof(EaseFunctionBase), typeof(LyricControl), new PropertyMetadata(new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3 }, OnEaseFunctionChanged));

        private EaseFunctionBase _easeFunction = new CustomExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3 };

        private static void OnEaseFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._easeFunction = (EaseFunctionBase)e.NewValue;
        }

        /// <summary>
        /// 字重(粗体等)
        /// </summary>
        public new FontWeight FontWeight
        {
            get => (FontWeight)GetValue(FontWeightProperty);
            set => SetValue(FontWeightProperty, value);
        }

        public new static readonly DependencyProperty FontWeightProperty =
            DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(LyricControl), new PropertyMetadata(FontWeights.SemiBold, OnFontWeightChanged));

        private FontWeight _fontWeight = FontWeights.SemiBold;

        private static void OnFontWeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._fontWeight = (FontWeight)e.NewValue;
        }

        /// <summary>
        /// 歌词颜色(未激活)
        /// </summary>
        public new int FontSize
        {
            get => (int)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public new static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(int), typeof(LyricControl), new PropertyMetadata(28, OnFontSizeChanged));

        private int _fontSize = 28;

        private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._fontSize = (int)e.NewValue;
        }

        /// <summary>
        /// 歌词颜色(未激活)
        /// </summary>
        public Color LyricColor
        {
            get => (Color)GetValue(LyricColorProperty);
            set => SetValue(LyricColorProperty, value);
        }

        public static readonly DependencyProperty LyricColorProperty =
            DependencyProperty.Register(nameof(LyricColor), typeof(Color), typeof(LyricControl), new PropertyMetadata(Color.FromArgb(50, 200, 200, 200), OnLyricColorChanged));

        private Color _lyricColor = Color.FromArgb(50, 200, 200, 200);

        private static void OnLyricColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._lyricColor = (Color)e.NewValue;
        }

        /// <summary>
        /// 歌词颜色(激活)
        /// </summary>

        public Color AccentLyricColor
        {
            get => (Color)GetValue(AccentLyricColorProperty);
            set => SetValue(AccentLyricColorProperty, value);
        }

        public static readonly DependencyProperty AccentLyricColorProperty =
            DependencyProperty.Register(nameof(AccentLyricColor), typeof(Color), typeof(LyricControl), new PropertyMetadata(Colors.White, OnAccentLyricColorChanged));

        private Color _accentLyricColor = Colors.White;

        private static void OnAccentLyricColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._accentLyricColor = (Color)e.NewValue;
        }

        /// <summary>
        /// 阴影颜色
        /// </summary>
        public Color ShadowColor
        {
            get => (Color)GetValue(ShadowColorProperty);
            set => SetValue(ShadowColorProperty, value);
        }

        public static readonly DependencyProperty ShadowColorProperty =
            DependencyProperty.Register(nameof(ShadowColor), typeof(Color), typeof(LyricControl), new PropertyMetadata(Color.FromArgb(200, 0, 0, 0), OnShadowColorChanged));

        private Color _shadowColor = Color.FromArgb(200, 0, 0, 0);

        private static void OnShadowColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._shadowColor = (Color)e.NewValue;
        }

        /// <summary>
        /// 歌词
        /// </summary>
        public SongLyric Lyric
        {
            get => (SongLyric)GetValue(LyricProperty);
            set => SetValue(LyricProperty, value);
        }

        public static readonly DependencyProperty LyricProperty =
            DependencyProperty.Register(nameof(Lyric), typeof(SongLyric), typeof(LyricControl), new PropertyMetadata(new SongLyric { LyricLine = new LrcLyricsLine("无歌词", string.Empty, TimeSpan.Zero) }, OnLyricChanged));

        private SongLyric _lyric = new SongLyric { LyricLine = new LrcLyricsLine("无歌词", string.Empty, TimeSpan.Zero) };

        private static void OnLyricChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LyricControl)d)._lyric = (SongLyric)e.NewValue;
        }
    }

}
