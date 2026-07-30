#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Controls;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.RollingCalculators;
using HyPlayer.LyricRenderer.Pipeline;
using Microsoft.Graphics.Canvas.UI.Xaml;
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
public partial class LyricEffectOperationItem : INotifyPropertyChanged
{
    private string _status = string.Empty;
    private LyricRenderOperationDescriptor? _descriptor;

    public LyricEffectOperationItem(
        LyricRenderOperationDefinition definition,
        LyricRenderOperationDescriptor? descriptor)
    {
        Definition = definition;
        _descriptor = descriptor;
    }

    public LyricRenderOperationDefinition Definition { get; }

    public string InstanceId => Definition.InstanceId;

    public string TypeId => Definition.TypeId;

    public LyricRenderOperationDescriptor? Descriptor => _descriptor;

    public string Description => Descriptor?.Description ?? TypeId;

    public string CategoryLabel => Descriptor?.Category switch
    {
        LyricRenderOperationCategory.Draw => "绘制节点",
        LyricRenderOperationCategory.Effect => "特效节点",
        _ => "未知节点"
    };

    public bool IsRequired => Descriptor?.IsRequired == true;

    public bool CanToggle => !IsRequired;

    public bool CanDuplicate => !IsRequired;

    public bool CanDelete => !IsRequired;

    public string DisplayName
    {
        get => Definition.DisplayName;
        set
        {
            if (Definition.DisplayName == value) return;
            Definition.DisplayName = value;
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => Definition.IsEnabled;
        set
        {
            if (IsRequired && !value) return;
            if (Definition.IsEnabled == value) return;
            Definition.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public void UpdateDescriptor(LyricRenderOperationDescriptor descriptor)
    {
        _descriptor = descriptor;
        OnPropertyChanged(nameof(TypeId));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CategoryLabel));
        OnPropertyChanged(nameof(IsRequired));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanDuplicate));
        OnPropertyChanged(nameof(CanDelete));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed partial class LyricEffectSettingsDialog : ContentDialog
{
    private readonly ILyricEffectProfileService _profiles =
        Ioc.Default.GetRequiredService<ILyricEffectProfileService>();
    private readonly LyricRenderView _preview = new();
    private LyricEffectProfileDocument _draft;
    private CancellationTokenSource? _previewDebounce;
    private TextBox? _scriptEditor;
    private bool _loading;
    private bool _saved;
    private bool _closed;
    private bool _hasUnsavedChanges;
    private string? _pendingProfileReplacement;

    public LyricEffectSettingsDialog()
    {
        InitializeComponent();
        _draft = _profiles.CreateDraft();
        InitializePresetMenus();
        LoadOperations(_draft);
        InitializePreview();
        _preview.SetEffectProfile(_profiles.EffectiveProfile);
        if (Operations.Count > 0) OperationList.SelectedIndex = 0;
    }

    private void InitializePresetMenus()
    {
        foreach (var preset in LyricEffectPresets.OperationPresets)
        {
            var item = new MenuFlyoutItem { Text = preset.Name, DataContext = preset };
            item.Click += AddOperationPreset_Click;
            OperationPresetFlyout.Items.Add(item);
        }

        foreach (var preset in LyricEffectPresets.ProfilePresets)
        {
            var item = new MenuFlyoutItem { Text = preset.Name, DataContext = preset };
            item.Click += ReplaceProfilePreset_Click;
            ProfilePresetFlyout.Items.Add(item);
        }
    }

    public ObservableCollection<LyricEffectOperationItem> Operations { get; } = [];

    private void InitializePreview()
    {
        _preview.Context.LineRollingEaseCalculator = new CircleEaseRollingCalculator();
        _preview.Context.LyricPaddingTopRatio = 0.12f;
        _preview.Context.LyricWidthRatio = 0.92f;
        _preview.Context.LineSpacing = 8;
        _preview.Context.IsPlaying = true;
        _preview.Context.Effects.CacheRenderTarget = false;
        _preview.Context.Effects.SimpleLineScanning = false;
        _preview.ChangeRenderFontSize(25, 14, 14);
        _preview.ChangeRenderColor(Colors.White, Color.FromArgb(255, 255, 213, 79), Colors.Black);
        _preview.SetLyricLines(
        [
            new TextRenderingLyricLine { Text = "Lorem ipsum dolor sit amet", StartTime = 0, EndTime = 3000, KeyFrames = [0, 3000], Typography = new(){ Alignment  = TextAlignment.Start} },
            new TextRenderingLyricLine { Text = "consectetur adipisicing elit", Translation = "测试测试测试测试", StartTime = 3000, EndTime = 6000, KeyFrames = [3000, 6000], Typography = new(){ Alignment  = TextAlignment.Start} },
            new TextRenderingLyricLine { Text = "sed do eiusmod tempor incididunt ut labore et dolore magna aliqua", StartTime = 6000, EndTime = 9000, KeyFrames = [6000, 9000] , Typography = new(){ Alignment  = TextAlignment.Start}}
        ]);
    }

    private void LoadOperations(LyricEffectProfileDocument profile)
    {
        _loading = true;
        foreach (var item in Operations) item.PropertyChanged -= Operation_PropertyChanged;
        Operations.Clear();
        _draft = LyricEffectPresets.CloneProfile(profile);
        foreach (var operation in _draft.Operations)
        {
            Operations.Add(CreateOperationItem(operation));
        }
        _loading = false;
    }

    private LyricEffectProfileDocument BuildDocument()
    {
        var document = LyricEffectPresets.CloneProfile(_draft);
        document.Operations = Operations.Select(item => item.Definition).ToList();
        return document;
    }

    private void Operation_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_loading) return;
        MarkDirtyAndPreview();
    }

    private void MarkDirtyAndPreview()
    {
        if (_loading || _closed) return;
        _hasUnsavedChanges = true;
        _pendingProfileReplacement = null;
        _previewDebounce?.Cancel();
        var cancellation = _previewDebounce = new CancellationTokenSource();
        _ = PreviewAfterDelayAsync(cancellation.Token);
    }

    private async Task PreviewAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            if (!cancellationToken.IsCancellationRequested) ApplyPreview();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private LyricProfileCompileResult ApplyPreview()
    {
        var result = _profiles.Preview(BuildDocument());
        UpdateDiagnostics(result.Diagnostics);
        if (result.IsSuccess)
        {
            StatusText.Text = result.Diagnostics.Count == 0
                ? "预览已更新"
                : string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message));
            _preview.SetEffectProfile(result.Profile!);
        }
        return result;
    }

    private void UpdateDiagnostics(IReadOnlyList<LyricProfileDiagnostic> diagnostics)
    {
        foreach (var item in Operations)
            item.Status = _profiles.Descriptors.Any(descriptor => descriptor.TypeId == item.TypeId)
                ? string.Empty
                : "当前未安装此节点，保存时会原样保留";

        foreach (var group in diagnostics.Where(item => item.InstanceId is not null).GroupBy(item => item.InstanceId))
        {
            var operation = Operations.FirstOrDefault(item => item.InstanceId == group.Key);
            if (operation is null) continue;
            var diagnostic = group.First();
            operation.Status = diagnostic.Line > 0
                ? $"{diagnostic.Parameter} ({diagnostic.Line}:{diagnostic.Column}) {diagnostic.Message}"
                : diagnostic.Message;
        }

        var errors = diagnostics.Where(item => item.Severity == LyricProfileDiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            StatusText.Text = string.Join(Environment.NewLine, errors.Select(item => item.Message));
    }

    private void OperationList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebuildEditor();

    private void RebuildEditor()
    {
        EditorPanel.Children.Clear();
        _scriptEditor = null;
        if (OperationList.SelectedItem is not LyricEffectOperationItem item)
        {
            EditorPanel.Children.Add(new TextBlock { Text = "请从左侧选择一个节点。", Opacity = 0.7 });
            return;
        }

        var definition = item.Definition;
        var descriptor = item.Descriptor;
        if (descriptor is null)
        {
            EditorPanel.Children.Add(CreateSettingsCard(
                item.DisplayName,
                item.TypeId,
                new TextBlock
                {
                    Text = "当前版本没有注册此扩展节点。其参数和扩展字段会原样保留，但不会参与渲染。",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.Orange)
                },
                ContentAlignment.Vertical));
            return;
        }

        if (!descriptor.IsEditable)
        {
            EditorPanel.Children.Add(CreateSettingsCard(
                $"{item.CategoryLabel} · {item.DisplayName}",
                descriptor.Description,
                new TextBlock
                {
                    Text = "这是内置必需绘制节点，不可编辑、关闭、复制或删除；可以在左侧拖动调整顺序。",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.Gray)
                },
                ContentAlignment.Vertical));
            return;
        }

        var nodeExpander = new SettingsExpander
        {
            Header = $"{item.CategoryLabel} · {item.DisplayName}",
            Description = descriptor.Description,
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var nameBox = new TextBox
        {
            Text = definition.DisplayName,
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        nameBox.TextChanged += (_, _) => item.DisplayName = nameBox.Text;
        nodeExpander.Items.Add(CreateSettingsCard("节点名称", "用于在当前特效链中识别该节点", nameBox));

        if (descriptor.Category == LyricRenderOperationCategory.Effect)
            nodeExpander.Items.Add(CreateEffectTypeCard(item));

        EditorPanel.Children.Add(nodeExpander);

        foreach (var parameterDescriptor in descriptor.Parameters)
        {
            if (!definition.Parameters.TryGetValue(parameterDescriptor.Key, out var parameter))
            {
                parameter = CreateParameterDefinition(parameterDescriptor);
                definition.Parameters[parameterDescriptor.Key] = parameter;
            }

            AddExpressionEditor(parameterDescriptor, parameter);
        }

        if (descriptor.SupportsScript)
            AddScriptEditor(definition);
    }

    private SettingsCard CreateEffectTypeCard(LyricEffectOperationItem item)
    {
        var combo = new ComboBox
        {
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var descriptor in _profiles.Descriptors
                     .Where(value => value.Category == LyricRenderOperationCategory.Effect && value.IsEditable)
                     .OrderBy(value => value.DisplayName))
        {
            var option = new ComboBoxItem { Content = descriptor.DisplayName, Tag = descriptor };
            combo.Items.Add(option);
            if (descriptor.TypeId == item.TypeId)
                combo.SelectedItem = option;
        }

        combo.SelectionChanged += EffectType_SelectionChanged;
        return CreateSettingsCard("特效类型", "切换类型会用新特效的默认表达式重置参数", combo);
    }

    private static SettingsCard CreateSettingsCard(
        string header,
        string description,
        UIElement content,
        ContentAlignment contentAlignment = ContentAlignment.Right) => new()
        {
            Header = header,
            Description = description,
            Content = content,
            ContentAlignment = contentAlignment,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

    private static LyricOperationParameterDefinition CreateParameterDefinition(
        LyricOperationParameterDescriptor descriptor) => new()
        {
            Expression = descriptor.DefaultExpression,
            Transition = descriptor.SupportsTransition ? new LyricTransitionDefinition() : null
        };

    private void AddExpressionEditor(
        LyricOperationParameterDescriptor descriptor,
        LyricOperationParameterDefinition parameter)
    {
        var description = string.IsNullOrWhiteSpace(descriptor.Description)
            ? $"默认值：{descriptor.DefaultExpression}"
            : $"{descriptor.Description} 默认值：{descriptor.DefaultExpression}";
        var expander = new SettingsExpander
        {
            Header = descriptor.DisplayName,
            Description = description,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var expressionBox = ExpressionBox(parameter.Expression);
        expressionBox.Tag = descriptor.Key;
        expressionBox.PlaceholderText = descriptor.DefaultExpression;
        expressionBox.TextChanged += ParameterExpression_TextChanged;
        expander.Items.Add(CreateSettingsCard(
            "表达式",
            $"值类型：{descriptor.ValueType}",
            expressionBox,
            ContentAlignment.Vertical));

        if (descriptor.SupportsTransition)
            AddTransitionEditor(expander, descriptor.Key, parameter);

        EditorPanel.Children.Add(expander);
    }

    private static TextBox ExpressionBox(string value) => new()
    {
        Text = value,
        FontFamily = new FontFamily("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private void EffectType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading ||
            OperationList.SelectedItem is not LyricEffectOperationItem item ||
            ((ComboBox)sender).SelectedItem is not ComboBoxItem { Tag: LyricRenderOperationDescriptor descriptor } ||
            descriptor.TypeId == item.TypeId)
            return;

        _loading = true;
        try
        {
            item.Definition.TypeId = descriptor.TypeId;
            item.Definition.Parameters = descriptor.Parameters.ToDictionary(
                parameter => parameter.Key,
                parameter => CreateParameterDefinition(parameter));
            item.Definition.Options.Clear();
            item.Definition.Script = null;
            item.Definition.ExtensionData = null;
            item.DisplayName = descriptor.DisplayName;
            item.UpdateDescriptor(descriptor);
            item.Status = string.Empty;
        }
        finally
        {
            _loading = false;
        }

        RebuildEditor();
        MarkDirtyAndPreview();
    }

    private void ParameterExpression_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || OperationList.SelectedItem is not LyricEffectOperationItem item) return;
        var textBox = (TextBox)sender;
        var key = (string)textBox.Tag;
        item.Definition.Parameters[key].Expression = textBox.Text;
        MarkDirtyAndPreview();
    }

    private void AddTransitionEditor(
        SettingsExpander expander,
        string key,
        LyricOperationParameterDefinition parameter)
    {
        var enabled = new ToggleSwitch
        {
            IsOn = parameter.Transition is not null,
            Tag = key
        };
        enabled.Toggled += TransitionEnabled_Toggled;
        expander.Items.Add(CreateSettingsCard(
            "启用缓动",
            "表达式结果变化时平滑过渡到新值",
            enabled));

        if (parameter.Transition is not { } transition) return;

        var duration = ExpressionBox(transition.DurationMs.ToString(CultureInfo.InvariantCulture));
        duration.MinWidth = 140;
        duration.Tag = new TransitionTag(key, TransitionProperty.Duration);
        duration.TextChanged += TransitionText_TextChanged;
        expander.Items.Add(CreateSettingsCard(
            "缓动时长",
            "单位：毫秒，范围 0–60000",
            duration));

        expander.Items.Add(CreateSettingsCard(
            "缓动函数",
            "控制过渡速度曲线",
            TransitionCombo(
                key,
                TransitionProperty.Easing,
                transition.EasingId,
                ["linear", "circle", "sine", "exponential", "elastic", "bounce"])));
        expander.Items.Add(CreateSettingsCard(
            "缓动模式",
            "控制曲线作用于进入、退出或两端",
            TransitionCombo(
                key,
                TransitionProperty.Mode,
                transition.Mode,
                ["in", "out", "inout"])));
    }

    private ComboBox TransitionCombo(
        string key,
        TransitionProperty property,
        string selected,
        IReadOnlyList<string> values)
    {
        var combo = new ComboBox
        {
            MinWidth = 180,
            Tag = new TransitionTag(key, property),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var value in values)
            combo.Items.Add(value);
        combo.SelectedItem = selected;
        combo.SelectionChanged += TransitionCombo_SelectionChanged;
        return combo;
    }

    private void TransitionEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || OperationList.SelectedItem is not LyricEffectOperationItem item) return;
        var toggle = (ToggleSwitch)sender;
        var parameter = item.Definition.Parameters[(string)toggle.Tag];
        parameter.Transition = toggle.IsOn ? new LyricTransitionDefinition() : null;
        RebuildEditor();
        MarkDirtyAndPreview();
    }

    private void TransitionText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OperationList.SelectedItem is not LyricEffectOperationItem item) return;
        var textBox = (TextBox)sender;
        var tag = (TransitionTag)textBox.Tag;
        if (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
            item.Definition.Parameters[tag.Key].Transition!.DurationMs = Math.Clamp(duration, 0, 60_000);
        MarkDirtyAndPreview();
    }

    private void TransitionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OperationList.SelectedItem is not LyricEffectOperationItem item) return;
        var combo = (ComboBox)sender;
        if (combo.SelectedItem is not string value) return;
        var tag = (TransitionTag)combo.Tag;
        var transition = item.Definition.Parameters[tag.Key].Transition!;
        if (tag.Property == TransitionProperty.Easing) transition.EasingId = value;
        else transition.Mode = value;
        MarkDirtyAndPreview();
    }

    private void AddScriptEditor(LyricRenderOperationDefinition definition)
    {
        var expander = new SettingsExpander
        {
            Header = "绘图脚本",
            Description = "编辑自定义绘制命令及其相对歌词内容的位置",
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var placement = new ComboBox
        {
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        placement.Items.Add("BehindSource");
        placement.Items.Add("AboveSource");
        placement.SelectedItem = definition.Options.TryGetValue("placement", out var value) ? value : "AboveSource";
        placement.SelectionChanged += (_, _) =>
        {
            if (placement.SelectedItem is string selected)
            {
                definition.Options["placement"] = selected;
                MarkDirtyAndPreview();
            }
        };
        expander.Items.Add(CreateSettingsCard(
            "绘制位置",
            "BehindSource 位于歌词后方，AboveSource 位于歌词上方",
            placement));

        _scriptEditor = new TextBox
        {
            Text = definition.Script ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_scriptEditor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_scriptEditor, ScrollBarVisibility.Auto);
        _scriptEditor.TextChanged += (_, _) =>
        {
            definition.Script = _scriptEditor.Text;
            MarkDirtyAndPreview();
        };
        expander.Items.Add(CreateSettingsCard(
            "脚本内容",
            "使用 Expression API v1 绘制当前歌词行",
            _scriptEditor,
            ContentAlignment.Vertical));

        var insertPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (name, source) in ScriptInsertions)
        {
            var button = new Button { Content = name, Tag = source, Padding = new Thickness(7, 3, 7, 3) };
            button.Click += InsertScript_Click;
            insertPanel.Children.Add(button);
        }
        expander.Items.Add(CreateSettingsCard(
            "插入命令",
            "在脚本末尾插入常用绘制命令",
            insertPanel,
            ContentAlignment.Vertical));
        EditorPanel.Children.Add(expander);
    }

    private void InsertScript_Click(object sender, RoutedEventArgs e)
    {
        if (_scriptEditor is null) return;
        var source = (string)((Button)sender).Tag;
        var prefix = _scriptEditor.Text.Length == 0 || _scriptEditor.Text.EndsWith('\n') ? string.Empty : Environment.NewLine;
        _scriptEditor.Text += prefix + source;
        _scriptEditor.SelectionStart = _scriptEditor.Text.Length;
    }

    private void OperationList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        MarkDirtyAndPreview();

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(OperationInstanceId(sender), -1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(OperationInstanceId(sender), 1);

    private static string OperationInstanceId(object sender) => (string)((FrameworkElement)sender).Tag;

    private void Move(string instanceId, int direction)
    {
        var item = Operations.FirstOrDefault(value => value.InstanceId == instanceId);
        if (item is null) return;
        var index = Operations.IndexOf(item);
        var target = index + direction;
        if (target < 0 || target >= Operations.Count) return;
        Operations.Move(index, target);
        OperationList.SelectedItem = item;
        MarkDirtyAndPreview();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var source = Operations.FirstOrDefault(item => item.InstanceId == OperationInstanceId(sender));
        if (source is null || source.IsRequired) return;
        var clone = CreateOperationItem(LyricEffectPresets.CloneOperation(source.Definition));
        var index = Operations.IndexOf(source) + 1;
        Operations.Insert(index, clone);
        OperationList.SelectedItem = clone;
        MarkDirtyAndPreview();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var item = Operations.FirstOrDefault(value => value.InstanceId == OperationInstanceId(sender));
        if (item is null || item.IsRequired) return;
        var index = Operations.IndexOf(item);
        item.PropertyChanged -= Operation_PropertyChanged;
        Operations.Remove(item);
        if (Operations.Count > 0) OperationList.SelectedIndex = Math.Min(index, Operations.Count - 1);
        MarkDirtyAndPreview();
    }

    private void AddOperationPreset_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not LyricOperationPreset preset) return;
        var operation = CreateOperationItem(LyricEffectPresets.CloneOperation(preset.Operation));
        Operations.Add(operation);
        OperationList.SelectedItem = operation;
        MarkDirtyAndPreview();
    }

    private void ReplaceProfilePreset_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not LyricProfilePreset preset) return;
        var name = preset.Name;
        if (_hasUnsavedChanges && _pendingProfileReplacement != name)
        {
            _pendingProfileReplacement = name;
            StatusText.Text = $"当前链有未保存修改。再次点击“{name}”的替换按钮以确认。";
            return;
        }

        LoadOperations(LyricEffectPresets.CloneProfile(preset.Profile, renewInstanceIds: true));
        _hasUnsavedChanges = true;
        _pendingProfileReplacement = null;
        if (Operations.Count > 0) OperationList.SelectedIndex = 0;
        MarkDirtyAndPreview();
    }

    private LyricEffectOperationItem CreateOperationItem(LyricRenderOperationDefinition definition)
    {
        var descriptor = _profiles.Descriptors.FirstOrDefault(value => value.TypeId == definition.TypeId);
        var item = new LyricEffectOperationItem(definition, descriptor);
        item.PropertyChanged += Operation_PropertyChanged;
        if (descriptor is null)
            item.Status = "当前未安装此节点，保存时会原样保留";
        return item;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".hylfx");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            LoadOperations(await _profiles.ImportAsync(file));
            _hasUnsavedChanges = true;
            if (Operations.Count > 0) OperationList.SelectedIndex = 0;
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
            var json = _profiles.Export(BuildDocument());
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = string.IsNullOrWhiteSpace(_draft.Name) ? "lyric-effects" : _draft.Name
            };
            picker.FileTypeChoices.Add("HyPlayer 歌词特效", new List<string> { ".hylfx" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, json, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            StatusText.Text = $"已导出到 {file.Name}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"导出失败：{exception.Message}";
        }
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        _preview.Redesign((float)e.NewSize.Width, (float)e.NewSize.Height, 96);
    }

    private void PreviewCanvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        var previewTime = (long)(args.Timing.TotalTime.TotalMilliseconds % 9000);
        if (previewTime < _preview.Context.CurrentLyricTime)
        {
            _preview.Context.CurrentLyricTime = previewTime;
            _preview.ReflowTime(0);
        }
        else
        {
            _preview.Context.CurrentLyricTime = previewTime;
        }

        _preview.Draw(args.DrawingSession, args.Timing);
    }

    private async void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            _previewDebounce?.Cancel();
            var result = await _profiles.CommitAsync(BuildDocument());
            UpdateDiagnostics(result.Diagnostics);
            if (!result.IsSuccess)
            {
                args.Cancel = true;
                return;
            }

            _saved = true;
            _hasUnsavedChanges = false;
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

    private void Dialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _previewDebounce?.Cancel();
        _profiles.CancelPreview();
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

    private sealed record TransitionTag(string Key, TransitionProperty Property);

    private enum TransitionProperty
    {
        Duration,
        Easing,
        Mode
    }

    private static readonly (string Name, string Source)[] ScriptInsertions =
    [
        ("矩形", "FillRectangle(0, 0, line.Width, line.Height, fx.Rgba(255, 255, 255, 0.08));"),
        ("圆角", "FillRoundedRectangle(0, 0, line.Width, line.Height, 6, line.AccentColor);"),
        ("线条", "DrawLine(0, line.Height, line.Width * line.Progress, line.Height, line.AccentColor, 2);"),
        ("文本", "DrawText(line.Text, 0, 0, 16, line.IdleColor);"),
        ("变换", "Save(); Translate(line.AnchorX, line.AnchorY); Rotate(5, 0, 0); Restore();")
    ];
}
