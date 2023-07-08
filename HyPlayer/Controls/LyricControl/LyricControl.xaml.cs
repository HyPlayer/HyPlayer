using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
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
using HyPlayer.HyPlayControl;
using LyricParser.Abstraction;
using Microsoft.Graphics.Canvas.Geometry;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls.LyricControl
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

        private void CanvasControl_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender,
                                        Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
        {
            _currentTime = HyPlayList.Player.PlaybackSession.Position - HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.StartTime;//更新播放进度

            using (var textFormat = new CanvasTextFormat
                   {
                       FontSize = _fontSize,
                       HorizontalAlignment = _horizontalTextAlignment,
                       VerticalAlignment = _verticalTextAlignment,
                       Options = CanvasDrawTextOptions.EnableColorFont,
                       WordWrapping = CanvasWordWrapping.Wrap,
                       Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                       FontStyle = _fontStyle,
                       FontWeight = _fontWeight,
                       FontFamily = _textFontFamily
                   })

            using (var textLayout = new CanvasTextLayout(args.DrawingSession, _lyric.LyricLine.CurrentLyric, textFormat,
                                                         (float)sender.Size.Width, (float)sender.Size.Height))
            {
                args.DrawingSession.DrawTextLayout(textLayout, 0, 0, _lyricColor);
                var cl = new CanvasCommandList(sender);
                using (CanvasDrawingSession clds = cl.CreateDrawingSession())
                {
                    clds.DrawTextLayout(textLayout, 0, 0, _accentLyricColor);
                }

                // 获取单词的高亮 Rect 组
                var highlightGeometry = CreateHighlightGeometry(_currentTime, _lyric.LyricLine, textLayout, sender);
                var textGeometry = CanvasGeometry.CreateText(textLayout);
                var highlightTextGeometry = highlightGeometry.CombineWith(textGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
                //args.DrawingSession.FillGeometry(textGeometry, _lyricColor);
                args.DrawingSession.FillGeometry(highlightTextGeometry, _accentLyricColor);
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

        private CanvasGeometry CreateHighlightGeometry(TimeSpan currentTime, ILyricLine lyric,
                                                       CanvasTextLayout textLayout, ICanvasResourceCreator canvas)
        {
            if (lyric is KaraokeLyricsLine karaokeLyricsLine)
            {
                var wordInfos = (List<KaraokeWordInfo>)karaokeLyricsLine.WordInfos;
                var time = TimeSpan.Zero;
                var currentLyric = wordInfos.Last();
                var geos = new List<CanvasGeometry>();
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
                var letterPosition = wordInfos.GetRange(0, index).Sum(p => p.CurrentWords.Length);
                var regions = textLayout.GetCharacterRegions(0, letterPosition);
                foreach (var region in regions)
                {
                    geos.Add(CanvasGeometry.CreateRectangle(canvas, region.LayoutBounds));
                }

                // 获取当前字符的 Bound
                //获取正在播放单词的长度
                var currentRegions = textLayout.GetCharacterRegions(letterPosition, currentLyric.CurrentWords.Length);
                if (currentRegions is { Length: > 0 })
                {
                    var startTime = TimeSpan.FromMilliseconds(wordInfos.GetRange(0, index).Sum(p => p.Duration.TotalMilliseconds));

                    var currentPercentage = (_currentTime - startTime) / currentLyric.Duration;
                    var lastRect = CanvasGeometry.CreateRectangle(
                        canvas, (float)currentRegions[0].LayoutBounds.Left,
                        (float)currentRegions[0].LayoutBounds.Top,
                        (float)(currentRegions.Sum(t => t.LayoutBounds.Width)*currentPercentage),
                        (float)currentRegions.Sum(t => t.LayoutBounds.Height));
                    geos.Add(lastRect);
                }
                
                return CanvasGeometry.CreateGroup(canvas, geos.ToArray());
            }
            else
            {
                return CanvasGeometry.CreateRectangle(canvas, (float)textLayout.LayoutBounds.Left,(float)textLayout.LayoutBounds.Top, (float)textLayout.LayoutBounds.Width,
                                                      (float)textLayout.LayoutBounds.Height);
            }
        }
        
        private void CanvasControl_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender,
                                          Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
        {
        }
    }
}