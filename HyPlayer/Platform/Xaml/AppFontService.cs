using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace HyPlayer.Platform.Xaml;

/// <summary>
///     Publishes the UI font selected in settings to the XAML resources and, for the text that keeps the
///     framework font, to the elements themselves.
///     <para>
///         Control templates read <c>ContentControlThemeFontFamily</c> and the text styles in
///         <c>Resources/ThemeFont.xaml</c> read <c>AppFontFamily</c>, so both keys are overridden. Elements
///         whose font comes from a built-in (theme scoped) style keep <c>Segoe UI</c>, so pages are also
///         walked once after navigation and on list container recycling.
///     </para>
///     Theme resources are resolved when an element is created, so the font has to be applied on startup;
///     changing it at runtime only affects elements created afterwards.
/// </summary>
public sealed class AppFontService : IAppFontService
{
    /// <summary>
    ///     Resource key consumed by the implicit text styles in Resources/ThemeFont.xaml.
    /// </summary>
    public const string AppFontFamilyKey = "AppFontFamily";

    private const string ContentControlThemeFontFamilyKey = "ContentControlThemeFontFamily";

    private static readonly string[] ThemeKeys = ["Default", "Light", "Dark", "HighContrast"];

    /// <summary>Font family names that mean "the framework default" and are therefore safe to replace.</summary>
    private static readonly string[] DefaultFontNames = ["Segoe UI", "XamlAutoFontFamily", "Microsoft YaHei UI"];

    private static readonly ConditionalWeakTable<object, object> Hooked = [];

    private FontFamily _appliedFont = FontFamily.XamlAutoFontFamily;

    private bool _hasAppFont;

    public string AppliedFontFamily { get; private set; } = string.Empty;

    public void Apply(string fontFamily)
    {
        AppliedFontFamily = fontFamily?.Trim() ?? string.Empty;
        _hasAppFont = !string.IsNullOrEmpty(AppliedFontFamily);
        _appliedFont = _hasAppFont ? new FontFamily(AppliedFontFamily) : FontFamily.XamlAutoFontFamily;

        // Fully qualified: the enclosing HyPlayer namespace contains a namespace named Application.
        var resources = Windows.UI.Xaml.Application.Current?.Resources;
        if (resources == null || !_hasAppFont)
            return;

        // Text styles in Resources/ThemeFont.xaml read AppFontFamily; control templates read
        // ContentControlThemeFontFamily. Theme dictionaries are written as well because {ThemeResource}
        // lookups prefer them over plain entries.
        resources[AppFontFamilyKey] = _appliedFont;
        resources[ContentControlThemeFontFamilyKey] = _appliedFont;
        foreach (var themeKey in ThemeKeys)
        {
            try
            {
                if (!resources.ThemeDictionaries.ContainsKey(themeKey)
                    || resources.ThemeDictionaries[themeKey] is not ResourceDictionary themeDictionary)
                    continue;

                themeDictionary[AppFontFamilyKey] = _appliedFont;
                themeDictionary[ContentControlThemeFontFamilyKey] = _appliedFont;
            }
            catch (Exception)
            {
                // A read-only theme dictionary keeps the framework default; the plain entries still apply.
            }
        }
    }

    public void ApplyToRoot(Control root)
    {
        if (root == null || !_hasAppFont)
            return;

        root.FontFamily = _appliedFont;
    }

    public void ApplyToCurrentWindow()
    {
        if (Window.Current?.Content is Control content)
            ApplyToRoot(content);
    }

    public void ApplyToTree(DependencyObject root)
    {
        if (root == null || !_hasAppFont)
            return;

        Walk(root, 0);
    }

    public void ScheduleTreeApply(DependencyObject root)
    {
        ApplyToTree(root);
        _ = WalkLaterAsync(root);
    }

    private void Walk(DependencyObject node, int depth)
    {
        if (node == null || depth > 80)
            return;

        switch (node)
        {
            case TextBlock textBlock:
                if (IsDefaultFont(textBlock.ReadLocalValue(TextBlock.FontFamilyProperty), textBlock.FontFamily))
                {
                    textBlock.FontFamily = _appliedFont;
                }

                break;
            case RichTextBlock richTextBlock:
                if (IsDefaultFont(richTextBlock.ReadLocalValue(RichTextBlock.FontFamilyProperty),
                        richTextBlock.FontFamily))
                {
                    richTextBlock.FontFamily = _appliedFont;
                }

                break;
            case Control control:
                if (IsDefaultFont(control.ReadLocalValue(Control.FontFamilyProperty), control.FontFamily))
                {
                    control.FontFamily = _appliedFont;
                }

                HookContainer(control);
                if (control is Button { Flyout: MenuFlyout buttonMenu })
                    HookMenuFlyout(buttonMenu);
                else if (FlyoutBase.GetAttachedFlyout(control) is MenuFlyout attachedMenu)
                    HookMenuFlyout(attachedMenu);
                break;
            case Hyperlink hyperlink:
                if (hyperlink.ReadLocalValue(Hyperlink.FontFamilyProperty) == DependencyProperty.UnsetValue)
                {
                    hyperlink.FontFamily = _appliedFont;
                }

                break;
        }

        if (node is UIElement { ContextFlyout: MenuFlyout contextMenu })
            HookMenuFlyout(contextMenu);

        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
            Walk(VisualTreeHelper.GetChild(node, i), depth + 1);
    }

    private static bool IsDefaultFont(object localValue, FontFamily? current)
    {
        if (localValue != DependencyProperty.UnsetValue)
            // The app set this font on purpose (icon fonts included); keep it.
            return false;

        if (current == null)
            return true;

        foreach (var name in DefaultFontNames)
            if (string.Equals(current.Source, name, StringComparison.OrdinalIgnoreCase))
                return true;

        return string.IsNullOrEmpty(current.Source);
    }

    /// <summary>
    ///     Menus live in a popup, outside the window tree, so they are patched when they open.
    /// </summary>
    private void HookMenuFlyout(MenuFlyout menu)
    {
        lock (Hooked)
        {
            if (Hooked.TryGetValue(menu, out _))
                return;

            Hooked.Add(menu, menu);
        }

        menu.Opened += OnMenuFlyoutOpened;
        ApplyToMenuItems(menu);
    }

    private void OnMenuFlyoutOpened(object sender, object e)
    {
        if (sender is MenuFlyout menu)
            ApplyToMenuItems(menu);
    }

    private void ApplyToMenuItems(MenuFlyout menu)
    {
        foreach (var item in menu.Items)
            ApplyToMenuFlyoutItem(item);
    }

    private void ApplyToMenuFlyoutItem(MenuFlyoutItemBase item)
    {
        if (item == null)
            return;

        if (IsDefaultFont(item.ReadLocalValue(Control.FontFamilyProperty), item.FontFamily))
            item.FontFamily = _appliedFont;

        if (item is MenuFlyoutSubItem subItem)
            foreach (var child in subItem.Items)
                ApplyToMenuFlyoutItem(child);
    }

    /// <summary>
    ///     Content created after the walk (list containers, navigated pages) is patched when it appears.
    /// </summary>
    private void HookContainer(Control control)
    {
        switch (control)
        {
            case ListViewBase listView:
                lock (Hooked)
                {
                    if (Hooked.TryGetValue(listView, out _))
                        return;

                    Hooked.Add(listView, listView);
                }

                listView.ContainerContentChanging += OnContainerContentChanging;
                break;
            case Frame frame:
                lock (Hooked)
                {
                    if (Hooked.TryGetValue(frame, out _))
                        return;

                    Hooked.Add(frame, frame);
                }

                frame.Navigated += OnFrameNavigated;
                break;
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not DependencyObject container)
            return;

        if (container is FrameworkElement element && !element.IsLoaded)
        {
            // The container is still being prepared; patch it once it (and its template children) exist.
            lock (Hooked)
            {
                if (Hooked.TryGetValue(container, out _))
                    return;

                Hooked.Add(container, container);
            }

            element.Loaded += OnContainerLoaded;
            return;
        }

        // Containers report several phases; later phases add the rest of the data template.
        Walk(container, 0);
    }

    private void OnContainerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            element.Loaded -= OnContainerLoaded;

        if (sender is DependencyObject container)
            Walk(container, 0);
    }

    private void OnFrameNavigated(object sender, Windows.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (sender is not DependencyObject frame)
            return;

        Walk(frame, 0);
        _ = WalkLaterAsync(frame);
    }

    private async Task WalkLaterAsync(DependencyObject root)
    {
        // Page content is usually filled asynchronously after navigation.
        foreach (var delay in (int[])[350, 1200, 3000])
        {
            await Task.Delay(delay);
            var dispatcher = Window.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            await dispatcher.RunAsync(CoreDispatcherPriority.Low, () => Walk(root, 0));
        }
    }
}
