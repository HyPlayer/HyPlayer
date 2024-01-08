using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls.LyricControl
{
    public sealed partial class LyricControl : UserControl
    {
        public LyricRenderOption? LyricRenderOption;

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
            if (HyPlayList.LyricPos < 0 || HyPlayList.LyricPos >= HyPlayList.Lyrics.Count)
                return;
            LyricRenderOption ??= new LyricRenderOption
            {
                FontSize = _fontSize,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center,
                FontStyle = _fontStyle,
                FontWeight = _fontWeight,
                FontFamily = _textFontFamily,
                BlurAmount = _blurAmount,
                EaseFunction = _easeFunction,
                HighlightColor = _accentLyricColor,
                LyricIdleColor = _lyricColor,
                ShadowColor = _shadowColor,

            };
            LyricRenderComposer.RenderOnDrawingSession(args.DrawingSession,
                                                       HyPlayList.Lyrics[HyPlayList.LyricPos],
                                                       HyPlayList.Player.PlaybackSession.Position, LyricRenderOption.GetValueOrDefault(),
                                                       sender.Size, QuickRenderMode);
        }

        private void CanvasControl_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender,
                                          Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
        {
        }
    }
}