using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graphics.Canvas.Text;
using static HyPlayer.Features.Settings.Settings;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Widgets;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class WidgetSettingsPage : Page
{
    private readonly GameBarSettings _settings;

    public WidgetSettingsPage()
    {
        this.InitializeComponent();
        _settings = new GameBarSettings(Dispatcher);
        Unloaded += WidgetSettingsPage_Unloaded;
    }

    private void WidgetSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _settings.PropertyChanged -= OnSettingsChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _settings.PropertyChanged += OnSettingsChanged;
        FontComboBox.ItemsSource = GetAllFonts();
    }

    private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
    {
        if (WidgetPage.Instance == null) return;
        _ = WidgetPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            WidgetPage.Instance.UpdateLyricViewSettings);
    }

    private List<FontInfo> GetAllFonts()
    {
        var names = CanvasTextFormat.GetSystemFontFamilies();
        var displayNames = CanvasTextFormat.GetSystemFontFamilies(new[] { "zh-cn" });
        var models = new List<FontInfo>();
        for (var i = 0; i < names.Length; i++)
            models.Add(new FontInfo
            {
                Name = displayNames[i],
                Value = names[i]
            });

        return models.OrderBy(t => t.Name).ToList();
    }
}
