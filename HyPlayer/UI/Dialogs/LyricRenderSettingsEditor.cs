#nullable enable
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.RollingCalculators;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.UI.Dialogs;

/// <summary>Shared numeric controls and a local preview; editing does not change saved settings.</summary>
internal static class LyricRenderSettingsEditor
{
    public static void Slider(StackPanel panel, string label, double value, double minimum, double maximum, Action<float> changed)
    {
        var caption = new TextBlock { Text = $"{label}：{value:0.##}" };
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value };
        slider.ValueChanged += (_, args) =>
        {
            caption.Text = $"{label}：{args.NewValue:0.##}";
            changed((float)args.NewValue);
        };
        panel.Children.Add(caption);
        panel.Children.Add(slider);
    }

    public static void Toggle(StackPanel panel, string label, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { Header = label, IsOn = value };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        panel.Children.Add(toggle);
    }

    public static void Choice<T>(StackPanel panel, string label, T value, (string Name, T Value)[] choices, Action<T> changed) where T : struct
    {
        var combo = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in choices)
        {
            var option = new ComboBoxItem { Content = item.Name, Tag = item.Value };
            combo.Items.Add(option);
            if (item.Value.Equals(value)) combo.SelectedItem = option;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T selected }) changed(selected);
        };
        panel.Children.Add(combo);
    }
}

internal sealed class LyricSettingsPreview : IDisposable
{
    private readonly LyricRenderView _view = new();
    private bool _closed;

    public LyricSettingsPreview()
    {
        _view.EnableRenderThreadUpdates();
        _view.Context.LineRollingEaseCalculator = new CircleEaseRollingCalculator();
        _view.Context.LyricPaddingTopRatio = 0.12f;
        _view.Context.LyricWidthRatio = 0.92f;
        _view.Context.LineSpacing = 8;
        _view.Context.IsPlaying = true;
        _view.ChangeRenderFontSize(25, 14, 14);
        _view.ChangeRenderColor(Colors.White, Color.FromArgb(255, 255, 213, 79), Colors.Black);
        _view.SetLyricLines(
        [
            new TextRenderingLyricLine { Text = "沿着光亮缓缓向前", Transliteration = "yan zhe guang liang huan huan xiang qian", StartTime = 0, EndTime = 3000, KeyFrames = [0, 3000] },
            new TextRenderingLyricLine { Text = "Keep moving into the light", Translation = "继续向光亮前行", StartTime = 3000, EndTime = 6000, KeyFrames = [3000, 6000] },
            new TextRenderingLyricLine { Text = "这一行很长，用来观察换行后的羽化扫词和柔和抬升", StartTime = 6000, EndTime = 9000, KeyFrames = [6000, 9000] }
        ]);
    }

    public void Settings(LyricRenderSettings settings) => _view.SetRenderSettings(settings);
    public void Size(SizeChangedEventArgs args)
    {
        if (args.NewSize.Width > 0 && args.NewSize.Height > 0)
            _view.Redesign((float)args.NewSize.Width, (float)args.NewSize.Height, 96);
    }
    public void Draw(CanvasAnimatedDrawEventArgs args)
    {
        if (_closed) return;
        var time = (long)(args.Timing.TotalTime.TotalMilliseconds % 9000);
        if (time < _view.Context.CurrentLyricTime) _view.ReflowTime(0);
        _view.Context.CurrentLyricTime = time;
        _view.Draw(args.DrawingSession, args.Timing);
    }
    public void Dispose()
    {
        _closed = true;
        _view.ReleaseResources();
    }
}
