using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Features.Widgets;

public partial class GameBarSettings(CoreDispatcher dispatcher) : ObservableObject
{
    private readonly ApplicationDataContainer _container =
        ApplicationData.Current.LocalSettings.CreateContainer("game-bar", ApplicationDataCreateDisposition.Always);

    private readonly CoreDispatcher _dispatcher = dispatcher;

    public string LyricFontFamily
    {
        get => GetSettings(nameof(LyricFontFamily), "Microsoft YaHei UI");
        set => SetValue(value);
    }

    public int LyricLineSpacing
    {
        get => GetSettings(nameof(LyricLineSpacing), 0);
        set => SetValue(value);
    }

    public int LyricSize
    {
        get => GetSettings(nameof(LyricSize), 0);
        set => SetValue(value);
    }

    public int TranslationSize
    {
        get => GetSettings(nameof(TranslationSize), 0);
        set => SetValue(value);
    }

    public int RomajiSize
    {
        get => GetSettings(nameof(RomajiSize), 15);
        set => SetValue(value);
    }

    public int LyricAlignment
    {
        get => GetSettings(nameof(LyricAlignment), 0);
        set => SetValue(value);
    }

    public bool EnableTranslation
    {
        get => GetSettings(nameof(EnableTranslation), true);
        set => SetValue(value);
    }

    public bool EnableTransliteration
    {
        get => GetSettings(nameof(EnableTransliteration), true);
        set => SetValue(value);
    }

    private void SetValue<T>(T value, [CallerMemberName] string? name = null)
    {
        if (name is null) return;
        if (_container.Values.TryGetValue(name, out var current) && Equals(current, value)) return;
        _container.Values[name] = value;
        _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => OnPropertyChanged(name));
    }

    public T GetSettings<T>(string propertyName, T defaultValue)
    {
        try
        {
            var success = _container.Values.TryGetValue(propertyName, out var value);
            if (success) return (T)value;

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
}
