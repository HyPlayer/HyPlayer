using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using WinRT;

namespace HyPlayer.Classes;

/// <summary>
/// 歌词渲染画刷管理器，提供 Accent / Idle 等主题画刷与颜色。
/// 继承 <see cref="ObservableObject"/> 以支持 XAML 绑定与属性变更通知。
/// </summary>
public class BrushManagement : ObservableObject
{
    private readonly Setting _setting;
    private SolidColorBrush? _accentBrush;
    private SolidColorBrush? _idleBrush;
    private Windows.UI.Color? _karaokAccentBrush;
    private bool _isBright;

    public BrushManagement(Setting setting)
    {
        _setting = setting;
    }

    /// <summary>
    /// 是否为亮色主题。变更时会联动刷新 <see cref="AccentTheme"/>、<see cref="AccentBrush"/>、<see cref="IdleBrush"/>。
    /// </summary>
    public bool IsBright
    {
        get => _isBright;
        set
        {
            if (SetProperty(ref _isBright, value))
            {
                OnPropertyChanged(nameof(AccentTheme));
                OnPropertyChanged(nameof(AccentBrush));
                OnPropertyChanged(nameof(IdleBrush));
            }
        }
    }

    /// <summary>
    /// 基于 <see cref="IsBright"/> 计算的主题枚举，供 XAML 绑定使用。
    /// </summary>
    public ElementTheme AccentTheme => IsBright ? ElementTheme.Light : ElementTheme.Dark;

    /// <summary>
    /// 当前强调画刷（用户自定义或系统主题色）。
    /// </summary>
    public SolidColorBrush AccentBrush
    {
        get
        {
            if (_setting.pureLyricFocusingColor is { } customColor)
                return new SolidColorBrush(customColor);

            return _accentBrush
                ?? Application.Current.Resources["SystemControlPageTextBaseHighBrush"]?.As<SolidColorBrush>()
                ?? new SolidColorBrush(Windows.UI.Colors.White);
        }
        set => SetProperty(ref _accentBrush, value);
    }

    /// <summary>
    /// 当前默认/非激活画刷（用户自定义或系统主题色）。
    /// </summary>
    public SolidColorBrush IdleBrush
    {
        get
        {
            if (_setting.pureLyricIdleColor is { } customColor)
                return new SolidColorBrush(customColor);

            return _idleBrush
                ?? Application.Current.Resources["TextFillColorTertiaryBrush"]?.As<SolidColorBrush>()
                ?? new SolidColorBrush(Windows.UI.Colors.Gray);
        }
        set => SetProperty(ref _idleBrush, value);
    }

    /// <summary>
    /// 卡拉OK歌词专用强调色（用户自定义或系统主题色）。
    /// </summary>
    public Windows.UI.Color KaraokAccentBrush
    {
        get
        {
            if (_setting.karaokLyricFocusingColor is { } customColor)
                return customColor;

            return _karaokAccentBrush
                ?? (Application.Current.Resources["SystemControlPageTextBaseHighBrush"]?.As<SolidColorBrush>())!.Color;
        }
        set => SetProperty(ref _karaokAccentBrush, value);
    }
}
