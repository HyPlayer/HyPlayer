using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI;
using HyPlayer.Classes;
using LyricParser.Abstraction;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.HyPlayControl.LyricControl
{
    public sealed partial class LyricControl : UserControl
    {
        public LyricControl()
        {
            this.InitializeComponent();
            this.Unloaded += LyricControl_Unloaded;
            this.Loaded += LyricControl_Loaded;
        }

        private void LyricControl_Loaded(object sender, RoutedEventArgs e)
        {
            CanvasControl.Update += CanvasControl_Update;
            CanvasControl.Draw += CanvasControl_Draw;
            CanvasControl.Paused = false;
        }

        private void LyricControl_Unloaded(object sender, RoutedEventArgs e)
        {
            CanvasControl.Update -= CanvasControl_Update;
            CanvasControl.Draw -= CanvasControl_Draw;
            CanvasControl.Paused = true;
        }

        private void CanvasControl_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
        {
            using (var textFormat = new CanvasTextFormat
            {
                FontSize = _fontSize,
                HorizontalAlignment = _horizontalTextAlignment,
                VerticalAlignment = _verticalTextAlignment,
                Options = CanvasDrawTextOptions.EnableColorFont,
                WordWrapping = _wordWrapping,
                Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                FontStyle = _fontStyle,
                FontWeight = _fontWeight,
                FontFamily = _textFontFamily
            })

            using (var textLayout = new CanvasTextLayout(args.DrawingSession, _lyric.LyricLine.CurrentLyric, textFormat, (float)sender.Size.Width, (float)sender.Size.Height))
            {
                args.DrawingSession.DrawTextLayout(textLayout, 0, 0, _lyricColor);

                var cl = new CanvasCommandList(sender);
                using (CanvasDrawingSession clds = cl.CreateDrawingSession())
                {
                    clds.DrawTextLayout(textLayout, 0, 0, _accentLyricColor);
                }

                var accentLyric = new CropEffect
                {
                    Source = cl,
                    SourceRectangle = new Rect(textLayout.LayoutBounds.Left, textLayout.LayoutBounds.Top, GetCropWidth(_currentTime, _lyric.LyricLine, textLayout), textLayout.LayoutBounds.Height),
                };
                var shadow = new ColorMatrixEffect
                {
                    Source = new GaussianBlurEffect
                    {
                        BlurAmount = _blurAmount,
                        Source = accentLyric,
                        BorderMode = EffectBorderMode.Soft
                    },
                    ColorMatrix = GetColorMatrix(_shadowColor)
                };
                args.DrawingSession.DrawImage(shadow);
                args.DrawingSession.DrawImage(accentLyric);
            }
        }

        private Matrix5x4 GetColorMatrix(Color color)
        {
            var matrix = new Matrix5x4();

            var R = ((float)color.R - 128) / 128;
            var G = ((float)color.G - 128) / 128;
            var B = ((float)color.B - 128) / 128;

            matrix.M11 = R;
            matrix.M12 = G;
            matrix.M13 = B;

            matrix.M21 = R;
            matrix.M22 = G;
            matrix.M23 = B;

            matrix.M31 = R;
            matrix.M32 = G;
            matrix.M33 = B;

            matrix.M44 = 1;

            return matrix;
        }

        private double GetCropWidth(TimeSpan currentTime, ILyricLine lyric, CanvasTextLayout textLayout)
        {
            if (lyric is KaraokeLyricsLine kLyric)
            {
                var wordInfos = (List<KaraokeWordInfo>)kLyric.WordInfos;
                var time = TimeSpan.Zero;
                var currentLyric = wordInfos.Last();
                //获取播放中单词在歌词的位置
                foreach (var item in wordInfos)
                {
                    if (item.Duration + time > currentTime)
                    {
                        currentLyric = item;
                        break;
                    }
                    time += item.Duration;
                }

                var index = wordInfos.IndexOf(currentLyric);
                var position = wordInfos.GetRange(0, index).Sum(p => p.CurrentWords.Length);
                var startTime = TimeSpan.FromMilliseconds(wordInfos.GetRange(0, index).Sum(p => p.Duration.TotalMilliseconds));
                //获取已经播放的长度
                var playedWidth = textLayout.GetCharacterRegions(0, position).Sum(p => p.LayoutBounds.Width);
                //获取正在播放单词的长度
                var currentWidth = textLayout.GetCharacterRegions(position, currentLyric.CurrentWords.Length).Sum(p => p.LayoutBounds.Width);
                //计算占比
                var playingWidth = _easeFunction.Ease((currentTime - startTime) / currentLyric.Duration) * currentWidth;
                //求和
                var width = playedWidth + playingWidth;

                if (width < 0)
                    width = 0;

                return width;
            }
            else
            {
                return textLayout.LayoutBounds.Width;
            }
        }

        private void CanvasControl_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
        {

        }
    }
}
