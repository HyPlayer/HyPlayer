#nullable enable
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.UI.Dialogs;

/// <summary>Edits built-in 高亮歌词特效 parameters with a local live preview.</summary>
public sealed partial class FocusedLyricEffectSettingsDialog : ContentDialog
{
    private readonly ILyricRenderSettingsService _settings = Ioc.Default.GetRequiredService<ILyricRenderSettingsService>();
    private readonly LyricSettingsPreview _preview = new();
    private FocusedTextSettings _draft;

    public FocusedLyricEffectSettingsDialog()
    {
        _draft = _settings.Current.Focused;
        InitializeComponent();
        BuildEditor();
        Preview();
    }

    private void BuildEditor()
    {
        EditorPanel.Children.Clear();
        LyricRenderSettingsEditor.Choice(EditorPanel, "高亮歌词预设", _draft.Preset,
            new (string, LyricHighlightPreset)[] { ("HyPlayer 默认", LyricHighlightPreset.Default), ("柔和抬升", LyricHighlightPreset.SoftLift) },
            value => { _draft = FocusedTextSettings.ForPreset(value); BuildEditor(); Preview(); });
        LyricRenderSettingsEditor.Choice(EditorPanel, "扫词方式", _draft.RevealMode,
            new (string, HighlightRevealMode)[] { ("连续羽化", HighlightRevealMode.RectangleClip), ("逐字推进", HighlightRevealMode.GlyphStep), ("整词渐亮", HighlightRevealMode.WholeWord) },
            value => { _draft = _draft with { RevealMode = value }; Preview(); });
        LyricRenderSettingsEditor.Choice(EditorPanel, "无逐字时间的高亮", _draft.UntimedHighlight,
            new (string, UntimedHighlightMode)[] { ("不高亮", UntimedHighlightMode.DoNotHighlight), ("整行高亮", UntimedHighlightMode.WholeLine), ("推断词时间", UntimedHighlightMode.InferWords) },
            value => { _draft = _draft with { UntimedHighlight = value }; Preview(); });
        LyricRenderSettingsEditor.Choice(EditorPanel, "音译高亮", _draft.Transliteration,
            new (string, TransliterationProgressMode)[] { ("跟随正文", TransliterationProgressMode.FollowMain), ("整行高亮", TransliterationProgressMode.WholeLine) },
            value => { _draft = _draft with { Transliteration = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "羽化宽度 (DIP)", _draft.FeatherDip, 0, 200, value => { _draft = _draft with { FeatherDip = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "扫词时间偏移 (ms)", _draft.RevealTimeOffsetMs, -1000, 1000, value => { _draft = _draft with { RevealTimeOffsetMs = value }; Preview(); });
        LyricRenderSettingsEditor.Toggle(EditorPanel, "启用抬升", _draft.LiftEnabled, value => { _draft = _draft with { LiftEnabled = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "抬升高度 (DIP)", _draft.LiftHeight, 0, 32, value => { _draft = _draft with { LiftHeight = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "字形重叠", _draft.LiftOverlap, 0, 1, value => { _draft = _draft with { LiftOverlap = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "整词阈值 (ms)", _draft.WholeWordThresholdMs, 0, 10000, value => { _draft = _draft with { WholeWordThresholdMs = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "抬升时间偏移 (ms)", _draft.LiftTimeOffsetMs, -1000, 1000, value => { _draft = _draft with { LiftTimeOffsetMs = value }; Preview(); });
        LyricRenderSettingsEditor.Slider(EditorPanel, "结束延长 (ms)", _draft.LiftFinishDurationMs, 0, 2000, value => { _draft = _draft with { LiftFinishDurationMs = value }; Preview(); });
        LyricRenderSettingsEditor.Choice(EditorPanel, "抬升单位", _draft.LiftUnit,
            new (string, GlyphLiftUnit)[] { ("自动", GlyphLiftUnit.Auto), ("逐字", GlyphLiftUnit.Glyph), ("整词", GlyphLiftUnit.Word) },
            value => { _draft = _draft with { LiftUnit = value }; Preview(); });
        LyricRenderSettingsEditor.Choice(EditorPanel, "抬升运动", _draft.LiftMotion,
            new (string, GlyphLiftMotion)[] { ("保持", GlyphLiftMotion.Hold), ("往返", GlyphLiftMotion.Pulse) },
            value => { _draft = _draft with { LiftMotion = value }; Preview(); });
        LyricRenderSettingsEditor.Choice(EditorPanel, "无逐字时间的抬升", _draft.UntimedLift,
            new (string, UntimedLiftMode)[] { ("不抬升", UntimedLiftMode.DoNotLift), ("整行抬升", UntimedLiftMode.WholeLine), ("推断词时间", UntimedLiftMode.InferWords) },
            value => { _draft = _draft with { UntimedLift = value }; Preview(); });
        LyricRenderSettingsEditor.Toggle(EditorPanel, "长音强调", _draft.LongNoteEmphasis, value => { _draft = _draft with { LongNoteEmphasis = value }; Preview(); });
    }

    private LyricRenderSettings Draft() => _settings.Current with { Focused = _draft.Normalize() };
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
