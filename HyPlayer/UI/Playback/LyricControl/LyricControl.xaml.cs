using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.UI.Playback.LyricControl;

public sealed partial class LyricControl : UserControl
{
    public LyricRenderOption? LyricRenderOption;

    public LyricControl()
    {
        this.InitializeComponent();
    }

    private void LyricControl_Unloaded(object sender, RoutedEventArgs e)
    {
        CanvasControl.RemoveFromVisualTree();
    }

    private void CanvasControl_Draw(ICanvasAnimatedControl sender,
        CanvasAnimatedDrawEventArgs args)
    {
        var lyric = Ioc.Default.GetRequiredService<ILyricService>();
        var player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
        if (lyric.CurrentLyricIndex < 0 || lyric.CurrentLyricIndex >= lyric.CurrentLyricInfo.Lyrics.Count ||
            player.PrimaryAudioInputNode == null)
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
            ShadowColor = _shadowColor
        };
        LyricRenderComposer.RenderOnDrawingSession(args.DrawingSession,
            lyric.CurrentLyricInfo.Lyrics[lyric.CurrentLyricIndex],
            player.PrimaryAudioInputNode.Position, LyricRenderOption.GetValueOrDefault(),
            sender.Size, QuickRenderMode);
    }

    private void CanvasControl_Update(ICanvasAnimatedControl sender,
        CanvasAnimatedUpdateEventArgs args)
    {
    }
}