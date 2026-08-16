#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Controls;
using HyPlayer.Features.Lyrics.Effects;
using ObservableCollections;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Pipeline;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.LyricRenderer.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using WinRT;

namespace HyPlayer.UI.Dialogs;

[GeneratedBindableCustomProperty]
public partial class FocusedTextOperationItem : INotifyPropertyChanged
{
    private readonly FocusedTextOperationDescriptor? _descriptor;

    public FocusedTextOperationItem(
        FocusedTextOperationDefinition definition,
        FocusedTextOperationDescriptor? descriptor)
    {
        Definition = definition;
        _descriptor = descriptor;
    }

    public FocusedTextOperationDefinition Definition { get; }
    public FocusedTextOperationDescriptor? Descriptor => _descriptor;
    public string InstanceId => Definition.InstanceId;
    public string DisplayName => Definition.DisplayName;
    public string ToggleAutomationId => $"FocusedEffectEnabled_{InstanceId}";
    public bool IsRequired => Descriptor?.IsRequired == true;
    public bool CanToggle => !IsRequired;
    public string TargetSummary => Definition.Targets.Count == 0
        ? IsRequired ? "必需结构节点" : "未选择目标"
        : $"{Definition.Targets.Count} 个目标";

    public bool IsEnabled
    {
        get => Definition.IsEnabled;
        set
        {
            if (IsRequired) return;
            if (Definition.IsEnabled == value) return;
            Definition.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public void NotifyTargetsChanged() => OnPropertyChanged(nameof(TargetSummary));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed partial class FocusedLyricEffectSettingsDialog : ContentDialog
{
    private static readonly (string Target, string Label)[] TargetOptions =
    [
        (FocusedTextTargets.LyricHighlighted, "正文 · 已高亮"),
        (FocusedTextTargets.LyricCurrentHighlighted, "正文 · 当前 Word 正在高亮"),
        (FocusedTextTargets.LyricCurrentPending, "正文 · 当前 Word 尚未高亮"),
        (FocusedTextTargets.LyricUnhighlighted, "正文 · 未高亮"),
        (FocusedTextTargets.TransliterationHighlighted, "音译 · 已高亮"),
        (FocusedTextTargets.TransliterationCurrentHighlighted, "音译 · 当前 Word 正在高亮"),
        (FocusedTextTargets.TransliterationCurrentPending, "音译 · 当前 Word 尚未高亮"),
        (FocusedTextTargets.TransliterationUnhighlighted, "音译 · 未高亮"),
        (FocusedTextTargets.Translation, "翻译")
    ];

    private readonly ILyricEffectProfileService _profiles =
        Ioc.Default.GetRequiredService<ILyricEffectProfileService>();
    private readonly LyricRenderView _preview = new();
    private LyricEffectProfileDocument _draft;
    private CancellationTokenSource? _previewDebounce;
    private bool _loading;
    private bool _saved;
    private bool _closed;

    public FocusedLyricEffectSettingsDialog()
    {
        OperationsView = Operations.ToNotifyCollectionChanged();
        InitializeComponent();
        _draft = _profiles.CreateDraft();
        InitializeAddMenu();
        LoadFocusedText(_draft.FocusedText);
        InitializePreview();
        _preview.SetEffectProfile(_profiles.EffectiveProfile);
        PreviewModeCombo.SelectedIndex = 0;
        if (Operations.Count > 0) OperationList.SelectedIndex = 0;
    }

    public ObservableList<FocusedTextOperationItem> Operations { get; } = [];
    public NotifyCollectionChangedSynchronizedViewList<FocusedTextOperationItem> OperationsView { get; }

    private void InitializeAddMenu()
    {
        foreach (var descriptor in _profiles.FocusedTextDescriptors
                     .Where(item => !item.IsRequired)
                     .OrderBy(item => item.DisplayName))
        {
            var menuItem = new MenuFlyoutItem { Text = descriptor.DisplayName, Tag = descriptor };
            menuItem.Click += AddOperation_Click;
            AddOperationFlyout.Items.Add(menuItem);
        }
    }

    private void InitializePreview()
    {
        _preview.Context.LineRollingEaseCalculator = new CircleEaseRollingCalculator();
        _preview.Context.LyricPaddingTopRatio = 0.12f;
        _preview.Context.LyricWidthRatio = 0.94f;
        _preview.Context.LineSpacing = 8;
        _preview.Context.IsPlaying = true;
        _preview.Context.EnableTransliteration = true;
        _preview.Context.EnableTranslation = true;
        _preview.ChangeRenderFontSize(27, 15, 15);
        _preview.ChangeRenderColor(Colors.White, Color.FromArgb(255, 255, 213, 79), Colors.Black);
        SetPreviewLyrics("Timed");
    }

    private void SetPreviewLyrics(string mode)
    {
        var line = new TextRenderingLyricLine
        {
            Text = "光影 follows every word",
            Transliteration = "guang ying follows every word",
            Translation = "光影跟随每一个词",
            StartTime = 0,
            EndTime = 6000,
            KeyFrames = [0, 6000],
            Typography = new() { Alignment = TextAlignment.Start }
        };
        if (mode == "Timed")
        {
            line.Tokens =
            [
                new("光影 ", 0, 1400, "guang ying "),
                new("follows ", 1400, 2900, "follows "),
                new("every ", 2900, 4300, "every "),
                new("word", 4300, 6000, "word")
            ];
        }
        _preview.SetLyricLines([line]);

        MarkDirtyAndPreview();
    }

    private void LoadFocusedText(FocusedTextEffectDefinition definition)
    {
        _loading = true;
        foreach (var item in Operations) item.PropertyChanged -= Operation_PropertyChanged;
        Operations.Clear();
        _draft.FocusedText = LyricEffectPresets.CloneFocusedText(definition);
        Operations.AddRange(_draft.FocusedText.Operations.Select(CreateItem));
        _loading = false;
    }

    private FocusedTextOperationItem CreateItem(FocusedTextOperationDefinition definition)
    {
        var descriptor = _profiles.FocusedTextDescriptors.FirstOrDefault(item => item.TypeId == definition.TypeId);
        var item = new FocusedTextOperationItem(definition, descriptor);
        item.PropertyChanged += Operation_PropertyChanged;
        return item;
    }

    private LyricEffectProfileDocument BuildDocument()
    {
        var result = LyricEffectPresets.CloneProfile(_draft);
        result.FocusedText.Operations = Operations.Select(item => item.Definition).ToList();
        return result;
    }

    private void Operation_PropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirtyAndPreview();

    private void MarkDirtyAndPreview()
    {
        if (_loading || _closed) return;
        _previewDebounce?.Cancel();
        _previewDebounce?.Dispose();
        var cancellation = _previewDebounce = new CancellationTokenSource();
        _ = PreviewAfterDelayAsync(cancellation.Token);
    }

    private async Task PreviewAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            var result = _profiles.Preview(BuildDocument());
            if (result.IsSuccess)
            {
                _preview.SetEffectProfile(result.Profile!);
                StatusText.Text = result.Diagnostics.Count == 0 ? "预览已更新" :
                    string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message));
            }
            else
            {
                StatusText.Text = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OperationList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebuildEditor();

    private void RebuildEditor()
    {
        EditorPanel.Children.Clear();
        if (OperationList.SelectedItem is not FocusedTextOperationItem item)
        {
            EditorPanel.Children.Add(new TextBlock { Text = "请从左侧选择一个节点。", Opacity = 0.7 });
            return;
        }

        if (item.Descriptor?.SupportsTargets != false)
        {
            var targetPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalSpacing = 6,
                VerticalSpacing = 6
            };
            foreach (var (target, label) in TargetOptions)
            {
                var toggle = new ToggleButton
                {
                    Content = label,
                    Tag = target,
                    IsChecked = item.Definition.Targets.Contains(target),
                    Padding = new Thickness(10, 5, 10, 5)
                };
                AutomationProperties.SetAutomationId(toggle, $"FocusedTarget_{target}");
                toggle.Click += TargetToggle_Click;
                targetPanel.Children.Add(toggle);
            }
            EditorPanel.Children.Add(new SettingsCard
            {
                Header = "作用目标",
                Description = "可同时选择多个 TargetState",
                Content = targetPanel,
                ContentAlignment = ContentAlignment.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }

        if (item.Descriptor is { } descriptor)
        {
            AddOperationOptions(item);
            foreach (var parameterDescriptor in descriptor.Parameters)
                AddParameterEditor(item, parameterDescriptor);
            if (descriptor.SupportsScript)
                AddScriptEditor(item);
        }
        else
        {
            EditorPanel.Children.Add(new TextBlock
            {
                Text = "当前版本没有注册此节点；保存时会原样保留。",
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void AddParameterEditor(FocusedTextOperationItem item, LyricOperationParameterDescriptor descriptor)
    {
        if (!item.Definition.Parameters.TryGetValue(descriptor.Key, out var parameter))
        {
            parameter = new LyricOperationParameterDefinition
            {
                Expression = descriptor.DefaultExpression
            };
            item.Definition.Parameters[descriptor.Key] = parameter;
        }

        EditorPanel.Children.Add(LyricParameterEditorFactory.Create(
            descriptor,
            parameter,
            MarkDirtyAndPreview,
            RebuildEditor));
    }

    private void AddOperationOptions(FocusedTextOperationItem item)
    {
        if (item.Definition.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal)
        {
            AddOption(item, "无 Word 高亮", "决定不含真实 Word 时间戳时如何处理", "untimedMode",
                nameof(UntimedHighlightMode.WholeLine),
                [nameof(UntimedHighlightMode.DoNotHighlight), nameof(UntimedHighlightMode.WholeLine), nameof(UntimedHighlightMode.InferWords)]);
            AddOption(item, "推进方式", "矩形裁切、逐 GlyphUnit 或整 Word", "revealMode",
                nameof(HighlightRevealMode.RectangleClip),
                [nameof(HighlightRevealMode.RectangleClip), nameof(HighlightRevealMode.GlyphStep), nameof(HighlightRevealMode.WholeWord)]);
            AddOption(item, "音译推进", "跟随正文 Word 映射或整行显示", "transliterationMode",
                nameof(TransliterationProgressMode.FollowMain),
                [nameof(TransliterationProgressMode.FollowMain), nameof(TransliterationProgressMode.WholeLine)]);
        }
        else if (item.Definition.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift)
        {
            AddOption(item, "无 Word 抬升", "不抬升、整行抬升或自动拆词", "untimedMode",
                nameof(UntimedLiftMode.DoNotLift),
                [nameof(UntimedLiftMode.DoNotLift), nameof(UntimedLiftMode.WholeLine), nameof(UntimedLiftMode.InferWords)]);
            AddOption(item, "抬升单元", "Auto 按 Word Duration 与阈值选择", "liftUnit",
                nameof(GlyphLiftUnit.Auto),
                [nameof(GlyphLiftUnit.Auto), nameof(GlyphLiftUnit.Glyph), nameof(GlyphLiftUnit.Word)]);
            AddOption(item, "抬升运动", "Hold 保持；Pulse 抬起后回落", "motion",
                nameof(GlyphLiftMotion.Hold), [nameof(GlyphLiftMotion.Hold), nameof(GlyphLiftMotion.Pulse)]);
            AddOption(item, "抬升曲线", "作用于当前 GlyphLift 的 LiftProgress", "easingId", "linear",
                ["linear", "circle", "sine", "exponential", "elastic", "bounce"]);
            AddOption(item, "曲线模式", "EaseIn、EaseOut 或 EaseInOut", "easingMode", "in",
                ["in", "out", "inout"]);
        }
    }

    private void AddOption(
        FocusedTextOperationItem item,
        string header,
        string description,
        string key,
        string fallback,
        string[] values)
    {
        var combo = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(combo, $"FocusedOption_{key}");
        foreach (var value in values) combo.Items.Add(value);
        combo.SelectedItem = item.Definition.Options.GetValueOrDefault(key, fallback);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not string selected) return;
            item.Definition.Options[key] = selected;
            MarkDirtyAndPreview();
        };
        EditorPanel.Children.Add(new SettingsCard
        {
            Header = header,
            Description = description,
            Content = combo,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
    }

    private void AddScriptEditor(FocusedTextOperationItem item)
    {
        var placement = new ComboBox { MinWidth = 180 };
        AutomationProperties.SetAutomationId(placement, "FocusedScriptPlacement");
        placement.Items.Add("BehindGlyph");
        placement.Items.Add("AboveGlyph");
        placement.SelectedItem = item.Definition.Options.TryGetValue("placement", out var currentPlacement)
            ? currentPlacement
            : "AboveGlyph";
        placement.SelectionChanged += (_, _) =>
        {
            if (_loading || placement.SelectedItem is not string selected) return;
            item.Definition.Options["placement"] = selected;
            MarkDirtyAndPreview();
        };
        EditorPanel.Children.Add(new SettingsCard
        {
            Header = "绘制位置",
            Description = "在当前 GlyphUnit 后方或前方执行",
            Content = placement,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var box = new TextBox
        {
            Text = item.Definition.Script ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 160,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(box, "FocusedGlyphScript");
        box.TextChanged += (_, _) =>
        {
            item.Definition.Script = box.Text;
            MarkDirtyAndPreview();
        };
        EditorPanel.Children.Add(new SettingsCard
        {
            Header = "Glyph 绘图脚本",
            Description = "在 GlyphUnit 局部坐标中执行受限绘图命令",
            Content = box,
            ContentAlignment = ContentAlignment.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
    }

    private void TargetToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || OperationList.SelectedItem is not FocusedTextOperationItem item) return;
        var toggle = (ToggleButton)sender;
        var target = (string)toggle.Tag;
        if (toggle.IsChecked == true)
        {
            if (!item.Definition.Targets.Contains(target)) item.Definition.Targets.Add(target);
        }
        else item.Definition.Targets.Remove(target);
        item.NotifyTargetsChanged();
        MarkDirtyAndPreview();
    }

    private void PreviewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PreviewModeCombo.SelectedItem is not ComboBoxItem { Tag: string mode }) return;
        SetPreviewLyrics(mode);
    }

    private void AddOperation_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is not FocusedTextOperationDescriptor descriptor) return;
        FocusedTextOperationDefinition definition;
        if (descriptor.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift)
        {
            definition = LyricEffectPresets.CloneFocusedOperation(LyricEffectPresets.CreateFocusedGlyphLift());
        }
        else
        {
            definition = new FocusedTextOperationDefinition
            {
                TypeId = descriptor.TypeId,
                DisplayName = descriptor.DisplayName,
                Targets = [FocusedTextTargets.LyricCurrentHighlighted],
                Parameters = descriptor.Parameters.ToDictionary(
                    parameter => parameter.Key,
                    parameter => new LyricOperationParameterDefinition
                    {
                        Expression = parameter.DefaultExpression
                    })
            };
        }
        var item = CreateItem(definition);
        Operations.Add(item);
        OperationList.SelectedItem = item;
        MarkDirtyAndPreview();
    }

    private void DeleteOperation_Click(object sender, RoutedEventArgs e)
    {
        if (OperationList.SelectedItem is not FocusedTextOperationItem item || item.IsRequired) return;
        var index = Operations.IndexOf(item);
        item.PropertyChanged -= Operation_PropertyChanged;
        Operations.Remove(item);
        if (Operations.Count > 0) OperationList.SelectedIndex = Math.Min(index, Operations.Count - 1);
        MarkDirtyAndPreview();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int direction)
    {
        if (OperationList.SelectedItem is not FocusedTextOperationItem item) return;
        var index = Operations.IndexOf(item);
        var target = index + direction;
        if (target < 0 || target >= Operations.Count) return;
        Operations.Move(index, target);
        OperationList.SelectedItem = item;
        MarkDirtyAndPreview();
    }

    private void OperationList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        MarkDirtyAndPreview();

    private void ResetPreset_Click(object sender, RoutedEventArgs e)
    {
        LoadFocusedText(LyricEffectPresets.CreateDefaultFocusedText());
        if (Operations.Count > 0) OperationList.SelectedIndex = 0;
        MarkDirtyAndPreview();
    }

    private void SoftLiftPreset_Click(object sender, RoutedEventArgs e)
    {
        LoadFocusedText(LyricEffectPresets.CreateSoftLiftFocusedText());
        if (Operations.Count > 0) OperationList.SelectedIndex = 0;
        MarkDirtyAndPreview();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".hylfx");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            _draft = await _profiles.ImportAsync(file);
            LoadFocusedText(_draft.FocusedText);
            if (Operations.Count > 0) OperationList.SelectedIndex = 0;
            StatusText.Text = "已导入完整配置；保存后整体链和聚焦链都会更新。";
            MarkDirtyAndPreview();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"导入失败：{exception.Message}";
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = string.IsNullOrWhiteSpace(_draft.Name) ? "lyric-effects" : _draft.Name
            };
            picker.FileTypeChoices.Add("HyPlayer 歌词特效", new List<string> { ".hylfx" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, _profiles.Export(BuildDocument()), Windows.Storage.Streams.UnicodeEncoding.Utf8);
            StatusText.Text = $"已导出到 {file.Name}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"导出失败：{exception.Message}";
        }
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            _preview.Redesign((float)e.NewSize.Width, (float)e.NewSize.Height, 96);
    }

    private void PreviewCanvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        var time = (long)(args.Timing.TotalTime.TotalMilliseconds % 6000);
        if (time < _preview.Context.CurrentLyricTime)
        {
            _preview.Context.CurrentLyricTime = time;
            _preview.ReflowTime(0);
        }
        else _preview.Context.CurrentLyricTime = time;
        _preview.Draw(args.DrawingSession, args.Timing);
    }

    private async void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            _previewDebounce?.Cancel();
            var result = await _profiles.CommitAsync(BuildDocument());
            if (!result.IsSuccess)
            {
                StatusText.Text = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message));
                args.Cancel = true;
                return;
            }
            _saved = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"保存失败：{exception.Message}";
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _previewDebounce?.Cancel();
        _previewDebounce?.Dispose();
        _previewDebounce = null;
        if (!_saved) _profiles.CancelPreview();
        foreach (var item in Operations) item.PropertyChanged -= Operation_PropertyChanged;
        _preview.Clear();
        PreviewCanvas.RemoveFromVisualTree();
    }
}
