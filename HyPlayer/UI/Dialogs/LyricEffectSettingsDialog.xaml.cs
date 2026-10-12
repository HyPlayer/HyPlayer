#nullable enable
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.UI.Dialogs;

/// <summary>Edits built-in 行切换特效 parameters with a local live preview.</summary>
public sealed partial class LyricEffectSettingsDialog : ContentDialog
{
    private readonly ILyricRenderSettingsService _settings = Ioc.Default.GetRequiredService<ILyricRenderSettingsService>();
    private readonly LyricSettingsPreview _preview = new();
    private LyricLineEffectSettings _draft;

    public LyricEffectSettingsDialog()
    {
        _draft = _settings.Current.Line;
        InitializeComponent();
        BuildEditor();
        Preview();
    }

    private void BuildEditor()
    {
        EditorPanel.Children.Clear();
        LyricRenderSettingsEditor.Choice(EditorPanel, "行特效预设", _draft.Preset,
            new (string, LyricLinePreset)[] { ("HyPlayer 默认", LyricLinePreset.Default), ("清晰", LyricLinePreset.Clear), ("柔和景深", LyricLinePreset.SoftDepth), ("立体层叠", LyricLinePreset.Layered), ("无特效", LyricLinePreset.None) },
            value => { _draft = LyricLineEffectSettings.ForPreset(value); BuildEditor(); Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "辉光半径", _draft.GlowRadius, 0, 64, value => { _draft = _draft with { GlowRadius = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "辉光透明度", _draft.GlowOpacity, 0, 1, value => { _draft = _draft with { GlowOpacity = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "近处透明度", _draft.NearOpacity, 0, 1, value => { _draft = _draft with { NearOpacity = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "远处透明度", _draft.FarOpacity, 0, 1, value => { _draft = _draft with { FarOpacity = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "模糊上限", _draft.MaximumBlur, 0, 64, value => { _draft = _draft with { MaximumBlur = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "远处缩放", _draft.DistantScale, 0.1, 1, value => { _draft = _draft with { DistantScale = value }; Preview(); });
        LyricRenderSettingsEditor.Toggle(EditorPanel, "悬停背景", _draft.HoverBackground, value => { _draft = _draft with { HoverBackground = value }; Preview(); });
        LyricRenderSettingsEditor.Toggle(EditorPanel, "行背景", _draft.Background, value => { _draft = _draft with { Background = value }; Preview(); });
        LyricRenderSettingsEditor.Toggle(EditorPanel, "进度下划线", _draft.ProgressUnderline, value => { _draft = _draft with { ProgressUnderline = value }; Preview(); });
    }

    private LyricRenderSettings Draft() => _settings.Current with { Line = _draft.Normalize() };
    private void Preview() => _preview.Settings(Draft());
    private void Preview_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) => _preview.Draw(args);
    private void Preview_SizeChanged(object sender, SizeChangedEventArgs args) => _preview.Size(args);
    private void Preview_Unloaded(object sender, RoutedEventArgs args) => _preview.Dispose();
    private void Dialog_Save(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try { _settings.Commit(Draft()); }
        catch (Exception error) { args.Cancel = true; StatusText.Text = $"保存失败：{error.Message}"; }
    }
    private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _preview.Dispose();
        PreviewCanvas.RemoveFromVisualTree();
    }
}
