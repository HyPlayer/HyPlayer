#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Controls;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Pipeline;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.LyricRenderer.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using NumberBox = Microsoft.UI.Xaml.Controls.NumberBox;
using NumberBoxSpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode;
using NumberBoxValueChangedEventArgs = Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
    public string TargetSummary => Definition.Targets.Count == 0
        ? "未选择目标"
        : $"{Definition.Targets.Count} 个目标";

    public bool IsEnabled
    {
        get => Definition.IsEnabled;
        set
        {
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
        InitializeComponent();
        _draft = _profiles.CreateDraft();
        InitializeAddMenu();
        LoadFocusedText(_draft.FocusedText);
        InitializePreview();
        _preview.SetEffectProfile(_profiles.EffectiveProfile);
        PreviewModeCombo.SelectedIndex = 0;
        if (Operations.Count > 0) OperationList.SelectedIndex = 0;
    }

    public ObservableCollection<FocusedTextOperationItem> Operations { get; } = [];

    private void InitializeAddMenu()
    {
        foreach (var descriptor in _profiles.FocusedTextDescriptors.OrderBy(item => item.DisplayName))
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

        if (mode == "Inferred") _draft.FocusedText.UntimedLineMode = UntimedLyricLineMode.InferWords;
        else if (mode == "Direct") _draft.FocusedText.UntimedLineMode = UntimedLyricLineMode.DirectHighlight;
        SelectEnum(UntimedModeCombo, _draft.FocusedText.UntimedLineMode.ToString());
        MarkDirtyAndPreview();
    }

    private void LoadFocusedText(FocusedTextEffectDefinition definition)
    {
        _loading = true;
        foreach (var item in Operations) item.PropertyChanged -= Operation_PropertyChanged;
        Operations.Clear();
        _draft.FocusedText = LyricEffectPresets.CloneFocusedText(definition);
        foreach (var operation in _draft.FocusedText.Operations)
            Operations.Add(CreateItem(operation));
        SelectEnum(UntimedModeCombo, _draft.FocusedText.UntimedLineMode.ToString());
        SelectEnum(RevealModeCombo, _draft.FocusedText.HighlightRevealMode.ToString());
        SelectEnum(TransliterationModeCombo, _draft.FocusedText.TransliterationMode.ToString());
        _loading = false;
    }

    private static void SelectEnum(ComboBox combo, string value)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, value, StringComparison.Ordinal));
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

        var targets = new SettingsExpander
        {
            Header = "作用目标",
            Description = "同一节点可以同时作用于多个文本状态",
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        foreach (var (target, label) in TargetOptions)
        {
            var checkBox = new CheckBox
            {
                Content = label,
                Tag = target,
                IsChecked = item.Definition.Targets.Contains(target),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            checkBox.Checked += TargetCheckBox_Changed;
            checkBox.Unchecked += TargetCheckBox_Changed;
            targets.Items.Add(new SettingsCard
            {
                Header = label,
                Content = checkBox,
                ContentAlignment = ContentAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }
        EditorPanel.Children.Add(targets);

        if (item.Descriptor is { } descriptor)
        {
            foreach (var parameterDescriptor in descriptor.Parameters)
                AddParameterEditor(item, parameterDescriptor);
            if (item.Definition.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift)
                AddMotionEditor(item);
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
            parameter = new LyricOperationParameterDefinition { Expression = descriptor.DefaultExpression };
            item.Definition.Parameters[descriptor.Key] = parameter;
        }

        var expander = new SettingsExpander
        {
            Header = descriptor.DisplayName,
            Description = $"默认值：{descriptor.DefaultExpression}",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        if (descriptor.ValueType == LyricExpressionValueType.Scalar &&
            double.TryParse(parameter.Expression, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal))
        {
            var number = new NumberBox
            {
                Value = literal,
                Minimum = descriptor.Minimum ?? double.MinValue,
                Maximum = descriptor.Maximum ?? double.MaxValue,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Tag = descriptor.Key,
                MinWidth = 180
            };
            number.ValueChanged += ParameterNumber_ValueChanged;
            expander.Items.Add(new SettingsCard
            {
                Header = "常用数值",
                Description = "修改后写入字面量表达式",
                Content = number,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }

        var expression = new TextBox
        {
            Text = parameter.Expression,
            Tag = descriptor.Key,
            FontFamily = new FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        expression.TextChanged += ParameterExpression_TextChanged;
        expander.Items.Add(new SettingsCard
        {
            Header = "高级表达式",
            Description = descriptor.ValueType == LyricExpressionValueType.Color
                ? "可自定义颜色：rgba(红, 绿, 蓝, 透明度)，透明度支持 0–1 或 0–255；也可读取 line/frame/text/word/glyph/fx"
                : "可读取 line/frame/text/word/glyph/fx",
            Content = expression,
            ContentAlignment = ContentAlignment.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        EditorPanel.Children.Add(expander);
    }

    private void AddMotionEditor(FocusedTextOperationItem item)
    {
        var combo = new ComboBox { MinWidth = 180, Tag = "motion" };
        combo.Items.Add("Hold");
        combo.Items.Add("Pulse");
        combo.SelectedItem = item.Definition.Options.TryGetValue("motion", out var motion) ? motion : "Hold";
        combo.SelectionChanged += Motion_SelectionChanged;
        EditorPanel.Children.Add(new SettingsCard
        {
            Header = "抬升运动",
            Description = "Hold 抬起并保持；Pulse 抬起后回落",
            Content = combo,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
    }

    private void AddScriptEditor(FocusedTextOperationItem item)
    {
        var placement = new ComboBox { MinWidth = 180 };
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

    private void TargetCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || OperationList.SelectedItem is not FocusedTextOperationItem item) return;
        var checkBox = (CheckBox)sender;
        var target = (string)checkBox.Tag;
        if (checkBox.IsChecked == true)
        {
            if (!item.Definition.Targets.Contains(target)) item.Definition.Targets.Add(target);
        }
        else item.Definition.Targets.Remove(target);
        item.NotifyTargetsChanged();
        MarkDirtyAndPreview();
    }

    private void ParameterNumber_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || OperationList.SelectedItem is not FocusedTextOperationItem item || double.IsNaN(args.NewValue)) return;
        item.Definition.Parameters[(string)sender.Tag].Expression = args.NewValue.ToString(CultureInfo.InvariantCulture);
        RebuildEditor();
        MarkDirtyAndPreview();
    }

    private void ParameterExpression_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || OperationList.SelectedItem is not FocusedTextOperationItem item) return;
        var box = (TextBox)sender;
        item.Definition.Parameters[(string)box.Tag].Expression = box.Text;
        MarkDirtyAndPreview();
    }

    private void Motion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || OperationList.SelectedItem is not FocusedTextOperationItem item ||
            ((ComboBox)sender).SelectedItem is not string motion) return;
        item.Definition.Options["motion"] = motion;
        MarkDirtyAndPreview();
    }

    private void GlobalMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (UntimedModeCombo.SelectedItem is ComboBoxItem { Tag: string untimed })
            _draft.FocusedText.UntimedLineMode = Enum.Parse<UntimedLyricLineMode>(untimed);
        if (RevealModeCombo.SelectedItem is ComboBoxItem { Tag: string reveal })
            _draft.FocusedText.HighlightRevealMode = Enum.Parse<HighlightRevealMode>(reveal);
        if (TransliterationModeCombo.SelectedItem is ComboBoxItem { Tag: string transliteration })
            _draft.FocusedText.TransliterationMode = Enum.Parse<TransliterationProgressMode>(transliteration);
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
                    parameter => new LyricOperationParameterDefinition { Expression = parameter.DefaultExpression })
            };
        }
        var item = CreateItem(definition);
        Operations.Add(item);
        OperationList.SelectedItem = item;
        MarkDirtyAndPreview();
    }

    private void DeleteOperation_Click(object sender, RoutedEventArgs e)
    {
        if (OperationList.SelectedItem is not FocusedTextOperationItem item) return;
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
        if (!_saved) _profiles.CancelPreview();
        foreach (var item in Operations) item.PropertyChanged -= Operation_PropertyChanged;
        _preview.Clear();
        PreviewCanvas.RemoveFromVisualTree();
    }
}
