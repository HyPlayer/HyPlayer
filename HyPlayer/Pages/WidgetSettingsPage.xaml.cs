using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using static HyPlayer.Pages.Settings;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class WidgetSettingsPage : Page
    {

        public WidgetSettingsPage()
        {
            this.InitializeComponent();
            _settings = new GameBarSettings(Dispatcher);
            Unloaded += WidgetSettingsPage_Unloaded;

        }

        private void WidgetSettingsPage_Unloaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
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
            _ = WidgetPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, WidgetPage.Instance.UpdateLyricViewSettings);
        }

        private readonly GameBarSettings _settings;

        private List<FontInfo> GetAllFonts()
        {
            var names = CanvasTextFormat.GetSystemFontFamilies();
            var displayNames = CanvasTextFormat.GetSystemFontFamilies(new[] { "zh-cn" });
            var models = new List<FontInfo>();
            for (var i = 0; i < names.Length; i++)
            {
                models.Add(new FontInfo
                {
                    Name = displayNames[i],
                    Value = names[i]
                });
            }

            return models.OrderBy(t => t.Name).ToList();
        }


    }
    public class GameBarSettings(CoreDispatcher dispatcher) : INotifyPropertyChanged
    {
        public GameBarSettings Instance { get; set; }

        private readonly ApplicationDataContainer _container = ApplicationData.Current.LocalSettings.CreateContainer("game-bar", ApplicationDataCreateDisposition.Always);

        private readonly CoreDispatcher _dispatcher = dispatcher;

        public string LyricFontFamily
        {
            get => GetSettings(nameof(LyricFontFamily), "Microsoft YaHei UI");
            set => SetValue(value);
        }

        public int LyricLineSpacing
        {
            get => GetSettings(nameof(LyricLineSpacing), 0);
            set
            {
                SetValue(value);
                OnPropertyChanged();
            }
        }

        public int LyricSize
        {
            get => GetSettings(nameof(LyricSize), 0);
            set
            {
                SetValue(value);
                OnPropertyChanged();
            }
        }

        public int TranslationSize
        {
            get => GetSettings(nameof(TranslationSize), 0);
            set
            {
                SetValue(value);
                OnPropertyChanged();
            }
        }
        public int RomajiSize
        {
            get => GetSettings(nameof(RomajiSize), 15);
            set
            {
                SetValue(value);
                OnPropertyChanged();
            }
        }

        public int LyricAlignment
        {
            get => GetSettings(nameof(LyricAlignment), 0);
            set
            {
                SetValue(value);
                OnPropertyChanged();
            }
        }


        public bool EnableTranslation
        {
            get => GetSettings(nameof(EnableTranslation), true);
            set
            {
                SetValue(value);
                OnPropertyChanged();
            }
        }

        public bool EnableTransliteration
        {
            get => GetSettings(nameof(EnableTransliteration), true);
            set
            {
                SetValue(value);
                OnPropertyChanged();
            }
        }

        private void SetValue<T>(T value, [CallerMemberName] string name = null)
        {
            if (name is null) return;
            _container.Values[name] = value;
        }

#nullable enable
        public event PropertyChangedEventHandler? PropertyChanged;
#nullable restore
        public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
        }

        public static T GetSettings<T>(string propertyName, T defaultValue)
        {
            try
            {
                var success = ApplicationData.Current.LocalSettings.Values.TryGetValue(propertyName, out object value);
                if (success)
                {
                    return (T)value;
                }

                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
