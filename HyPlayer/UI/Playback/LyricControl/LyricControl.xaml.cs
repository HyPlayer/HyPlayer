using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.LyricRenderer;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.UI.Playback.LyricControl;

public sealed partial class LyricControl : UserControl
{
    private readonly LyricClock _lyricClock = new();
    private readonly ILyricService _lyricService = Ioc.Default.GetRequiredService<ILyricService>();
    private readonly AudioGraphPlayer _audioPlayer = Ioc.Default.GetRequiredService<AudioGraphPlayer>();

    public LyricRenderOption? LyricRenderOption { get; set; }

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
        var node = _audioPlayer.PrimaryAudioInputNode;
        if (_lyricService.CurrentLyricIndex < 0 || _lyricService.CurrentLyricIndex >= _lyricService.CurrentLyricInfo.Lyrics.Count ||
            node == null)
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
            _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex],
            TimeSpan.FromMilliseconds(_lyricClock.Next(node.Position.TotalMilliseconds,
                _audioPlayer.GlobalPlaybackStatus == PlaybackStatus.Playing, node.PlaybackSpeedFactor)),
            LyricRenderOption.GetValueOrDefault(),
            sender.Size, QuickRenderMode);
    }

    private void CanvasControl_Update(ICanvasAnimatedControl sender,
        CanvasAnimatedUpdateEventArgs args)
    {
    }
}
