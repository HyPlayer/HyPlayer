#nullable enable

using CommunityToolkit.WinUI.Controls;
using HyPlayer.LyricEffects.Models;
using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace HyPlayer.UI.Dialogs;

internal static class LyricParameterEditorFactory
{
    private static readonly IReadOnlyDictionary<string, (string Key, string Label, string Default)[]> CurveArguments =
        new Dictionary<string, (string, string, string)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["exponential"] = [("exponent", "指数", "2")],
            ["elastic"] = [("springiness", "弹性强度", "6"), ("oscillations", "振荡次数", "1")],
            ["bounce"] = [("bounces", "回弹次数", "3"), ("bounciness", "回弹强度", "2")]
        };

    public static SettingsExpander Create(
        LyricOperationParameterDescriptor descriptor,
        LyricOperationParameterDefinition parameter,
        Action changed,
        Action rebuild)
    {
        var expander = new SettingsExpander
        {
            Header = descriptor.DisplayName,
            Description = string.IsNullOrWhiteSpace(descriptor.Description)
                ? $"默认值：{descriptor.DefaultExpression}"
                : $"{descriptor.Description} 默认值：{descriptor.DefaultExpression}",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var expression = ExpressionBox(parameter.Expression);
        AutomationProperties.SetAutomationId(expression, $"LyricParameter_{descriptor.Key}");
        expression.PlaceholderText = descriptor.DefaultExpression;
        expression.TextChanged += (_, _) =>
        {
            parameter.Expression = expression.Text;
            changed();
        };
        expander.Items.Add(Card(
            "高级表达式",
            descriptor.ValueType == LyricExpressionValueType.Color
                ? "可使用 fx.Rgba(红, 绿, 蓝, 透明度)；透明度接受 0–1 或 0–255"
                : "可读取当前 Expression API 上下文",
            expression,
            ContentAlignment.Vertical));

        if (!descriptor.SupportsTransition || descriptor.ValueType == LyricExpressionValueType.Text)
            return expander;

        var enabled = new ToggleSwitch { IsOn = parameter.Transition is not null };
        AutomationProperties.SetAutomationId(enabled, $"LyricTransition_{descriptor.Key}");
        enabled.Toggled += (_, _) =>
        {
            parameter.Transition = enabled.IsOn ? new LyricTransitionDefinition() : null;
            changed();
            rebuild();
        };
        expander.Items.Add(Card("启用 Transition", "Scalar 与 Color 都按表达式结果平滑过渡", enabled));
        if (parameter.Transition is not { } transition) return expander;

        var duration = ExpressionBox(transition.DurationMs);
        AutomationProperties.SetAutomationId(duration, $"LyricTransitionDuration_{descriptor.Key}");
        duration.PlaceholderText = "500";
        duration.TextChanged += (_, _) =>
        {
            transition.DurationMs = duration.Text;
            changed();
        };
        expander.Items.Add(Card("DurationMs", "表达式；在 Transition 启动时快照", duration, ContentAlignment.Vertical));

        var easing = Combo(["linear", "circle", "sine", "exponential", "elastic", "bounce"], transition.EasingId);
        AutomationProperties.SetAutomationId(easing, $"LyricTransitionEasing_{descriptor.Key}");
        easing.SelectionChanged += (_, _) =>
        {
            if (easing.SelectedItem is not string selected || selected == transition.EasingId) return;
            transition.EasingId = selected;
            changed();
            rebuild();
        };
        expander.Items.Add(Card("缓动函数", "曲线参数同样使用表达式", easing));

        var mode = Combo(["in", "out", "inout"], transition.Mode);
        AutomationProperties.SetAutomationId(mode, $"LyricTransitionMode_{descriptor.Key}");
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is string selected)
            {
                transition.Mode = selected;
                changed();
            }
        };
        expander.Items.Add(Card("缓动模式", "进入、退出或两端", mode));

        if (!CurveArguments.TryGetValue(transition.EasingId, out var arguments)) return expander;
        foreach (var (key, label, defaultExpression) in arguments)
        {
            if (!transition.Arguments.TryGetValue(key, out var source))
                transition.Arguments[key] = source = defaultExpression;
            var box = ExpressionBox(source);
            AutomationProperties.SetAutomationId(box, $"LyricTransitionArgument_{descriptor.Key}_{key}");
            box.PlaceholderText = defaultExpression;
            box.TextChanged += (_, _) =>
            {
                transition.Arguments[key] = box.Text;
                changed();
            };
            expander.Items.Add(Card(label, "表达式；在 Transition 启动时快照", box, ContentAlignment.Vertical));
        }

        return expander;
    }

    private static TextBox ExpressionBox(string value) => new()
    {
        Text = value,
        FontFamily = new FontFamily("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox Combo(string[] values, string selected)
    {
        var combo = new ComboBox { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values) combo.Items.Add(value);
        combo.SelectedItem = selected;
        return combo;
    }

    private static SettingsCard Card(
        string header,
        string description,
        UIElement content,
        ContentAlignment alignment = ContentAlignment.Right) => new()
    {
        Header = header,
        Description = description,
        Content = content,
        ContentAlignment = alignment,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
}
